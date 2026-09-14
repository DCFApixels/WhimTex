// No Unity compilation: exercise channel arithmetic and packed serialization contracts.
// The GPU/compositor checks live in SwizzleSmoke.cs and require manual shader import first.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const model = read('../src/LayerSwizzle.cs');
const shader = read('../src/Shaders/Hdr.shader');
const names = model.match(/enum SwizzleChannel\s*\{([^}]+)\}/)[1].split(',').map(s => s.trim());
assert.deepEqual(names, ['R','G','B','A','OneMinusR','OneMinusG','OneMinusB','OneMinusA','Zero','One',
  'RMultiplyA','GMultiplyA','BMultiplyA']);
const labels = [...model.match(/Labels = \{([^}]+)\}/)[1].matchAll(/"([^"]+)"/g)].map(m => m[1]);
assert.deepEqual(labels, ['R','G','B','A','1-R','1-G','1-B','1-A','0','1','R * A','G * A','B * A']);
assert.ok(names.length <= 16);

// Translate the scalar HLSL helper, so expected values are checked against the actual source.
const body = shader.match(/float channel\(float4 c, float source\)\s*\{([^}]+)\}/)[1]
  .replace(/\b(float|bool)\s+/g, 'let ')
  .replace(/c\.([rgba])/g, (_,c) => `c[${'rgba'.indexOf(c)}]`);
const channel = new Function('c','source',body);
for (const c of [[.3,.6,.8,.7],[2,-3,6,.25],[2,.5,.25,0],[2,.5,.25,1]]) {
  const expected = [...c,...c.map(v => 1-v),0,1,...c.slice(0,3).map(v => v*c[3])];
  for (let source=0;source<labels.length;source++) assert.equal(channel(c,source),expected[source]);
  assert.deepEqual([10,11,12,9].map(source => channel(c,source)),[c[0]*c[3],c[1]*c[3],c[2]*c[3],1]);
}
const set = (packed,output,value) => (packed & ~(15 << (output*4))) | ((value ^ output) << (output*4));
const get = (packed,output) => ((packed >> (output*4)) & 15) ^ output;
assert.deepEqual([0,1,2,3].map(output=>get(0,output)),[0,1,2,3]);
for (let code=0;code<13**4;code++) {
  let packed=0, n=code;
  const values=[];
  for(let output=0;output<4;output++) {
    const value=n%13; n=Math.floor(n/13); values.push(value);
    packed=set(packed,output,value);
  }
  assert.deepEqual(values.map((_,output)=>get(packed,output)),values);
}
assert.ok(read('../src/Automation/WhimTexApi.Layers.cs').includes('System.Array.IndexOf(LayerSwizzle.Labels'));
assert.ok(read('../src/Automation/WhimTexApi.Inspect.cs').includes('new JArray(LayerSwizzle.Labels)'));
console.log('Swizzle arithmetic, legacy IDs, API labels and all 28,561 packed mappings passed. GPU checks require Unity.');
