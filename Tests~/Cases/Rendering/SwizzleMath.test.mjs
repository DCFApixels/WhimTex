// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync } from 'node:fs';
const context = new TestContext("SwizzleMath source/reference tests");
const assert = context.assert;
context.case("SwizzleMath original assertion inputs and source contracts", async () => {
  // No Unity compilation: exercise channel arithmetic and packed serialization contracts.
  // GPU/compositor checks live in SwizzleTests.cs.
  const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
  const model = read('../../../src/LayerSwizzle.cs');
  const shader = read('../../../src/Shaders/Hdr.shader');
  const names = model.match(/enum SwizzleChannel\s*\{([^}]+)\}/)[1].split(',').map(s => s.trim());
  assert.deepEqual(names, ['R','G','B','A','OneMinusR','OneMinusG','OneMinusB','OneMinusA','Zero','One',
    'RMultiplyA','GMultiplyA','BMultiplyA','Luminance','LuminanceMultiplyA']);
  const labels = [...model.match(/Labels = \{([^}]+)\}/)[1].matchAll(/"([^"]+)"/g)].map(m => m[1]);
  assert.deepEqual(labels, ['R','G','B','A','1-R','1-G','1-B','1-A','0','1','R * A','G * A','B * A','Luminance','Luminance * A']);
  assert.ok(names.length <= 16);

  // Translate the scalar HLSL helper, so expected values are checked against the actual source.
  const body = shader.match(/float channel\(float4 c, float source\)\s*\{([\s\S]*?)\n        \}/)[1]
    .replace(/\b(float|bool)\s+/g, 'let ')
    .replace(/c\.rgb/g, 'c.slice(0,3)')
    .replace(/c\.([rgba])/g, (_,c) => `c[${'rgba'.indexOf(c)}]`);
  const evaluate = new Function('c','source','dot','float3',body);
  const channel = (c, source) => evaluate(c, source, (a,b) => a.reduce((sum,v,i) => sum+v*b[i],0), (...v) => v);
  const presetSource = read('../../../src/LayerColorSettingsView.cs');
  const presets = new Map([...presetSource.matchAll(/\("([^"]+)", new LayerSwizzle\s*\{([^}]+)\}\)/g)]
    .map(([,name,body]) => {
      const routes = [0,1,2,3];
      for (const [,output,source] of body.matchAll(/\[(\d)\]\s*=\s*SwizzleChannel\.(\w+)/g))
        routes[Number(output)] = names.indexOf(source);
      return [name,routes];
    }));
  assert.equal(presets.has('A'),false,'Luminance to Alpha already covers the A preset');
  for (const c of [[.3,.6,.8,.7],[2,-3,6,.25],[2,.5,.25,0],[2,.5,.25,1]]) {
    const luminance = .2126*c[0]+.7152*c[1]+.0722*c[2];
    const expected = [...c,...c.map(v => 1-v),0,1,...c.slice(0,3).map(v => v*c[3]),luminance,luminance*c[3]];
    for (let source=0;source<labels.length;source++) assert.equal(channel(c,source),expected[source]);
    assert.deepEqual([10,11,12,9].map(source => channel(c,source)),[c[0]*c[3],c[1]*c[3],c[2]*c[3],1]);
    for (const [name,expected] of [
      ['Default without Alpha',[...c.slice(0,3),1]],
      ['R',[luminance,0,0,1]],['G',[0,luminance,0,1]],['B',[0,0,luminance,1]],
      ['Luminance to Alpha',[1,1,1,luminance]]
    ]) {
      assert.ok(presets.has(name),'Builtin preset exists: '+name);
      assert.deepEqual(presets.get(name).map(source => channel(c,source)),expected,name+' ignores original alpha when packing brightness');
    }
  }
  const set = (packed,output,value) => (packed & ~(15 << (output*4))) | ((value ^ output) << (output*4));
  const get = (packed,output) => ((packed >> (output*4)) & 15) ^ output;
  assert.deepEqual([0,1,2,3].map(output=>get(0,output)),[0,1,2,3]);
  for (let code=0;code<labels.length**4;code++) {
    let packed=0, n=code;
    const values=[];
    for(let output=0;output<4;output++) {
      const value=n%labels.length; n=Math.floor(n/labels.length); values.push(value);
      packed=set(packed,output,value);
    }
    assert.deepEqual(values.map((_,output)=>get(packed,output)),values);
  }
  assert.ok(read('../../../src/Automation/WhimTexApi.Layers.cs').includes('System.Array.IndexOf(LayerSwizzle.Labels'));
  assert.ok(read('../../../src/Automation/WhimTexApi.Inspect.cs').includes('new JArray(LayerSwizzle.Labels)'));
  assert.ok(channel([1,0,0,1],13) < channel([0,1,0,1],13), 'Weighted brightness is not a simple RGB average');
  assert.equal(channel([1,1,1,0],13),1, 'Luminance ignores alpha');
  assert.equal(channel([1,1,1,0],14),0, 'Luminance product respects transparent input');

});
await finish(context);
