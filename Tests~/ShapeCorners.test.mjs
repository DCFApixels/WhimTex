import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../src/' + p, import.meta.url), 'utf8');
const source = read('Layers/ShapeLayerBehaviour.cs');
let body = source.split('internal static Vector4 AdjustCorner(')[1].split('internal override RenderTexture Render')[0];
body = body.slice(body.indexOf('{') + 1, body.lastIndexOf('}'))
    .replace(/\b(?:int|float|double) (\w+) =/g, 'let $1 =')
    .replace(/\((?:float|double)\)/g, '')
    .replace(/(\d)f\b/g, '$1')
    .replace(/Mathf.Max/g, 'Math.max').replace(/Mathf.Min|Math.Min/g, 'Math.min')
    .replace(/values\.([xyzw])/g, (_,c)=>`values[${'xyzw'.indexOf(c)}]`)
    .replace('values += Vector4.one * Math.min(value, 1 - largest);', 'values = values.map(v => v + Math.min(value, 1 - largest));');
const limit = (v,min,max,fallback) => Number.isFinite(v) ? Math.max(min,Math.min(max,v)) : fallback;
const extracted = new Function('values','corner','value','linked','Limit',body);
const adjust = (v,i,n,linked=true)=>extracted([...v],i,n,linked,limit);
const near = (a,b)=>a.forEach((v,i)=>assert.ok(Math.abs(v-b[i]) < 1e-6,`${a} != ${b}`));
near(adjust([.1,.2,.3,.4],0,.2),[.2,.4,.6,.8]);
near(adjust([.1,.2,.3,.4],0,1),[.25,.5,.75,1]);
near(adjust([.1,.2,.3,.4],0,.8,false),[.8,.2,.3,.4]);
near(adjust([0,0,0,0],2,.3),[.3,.3,.3,.3]);
near(adjust([0,.2,.4,.6],0,.3),[.3,.5,.7,.9]);
near(adjust([0,.2,.4,.6],0,.9),[.4,.6,.8,1]);
near(adjust([.1,.2,.3,.4],0,0),[0,0,0,0]);
near(adjust([.1,.2,.3,.4],0,NaN),[.1,.2,.3,.4]);
let checks=0;
for(let index=0;index<4;index++) for(let n=0;n<=100;n++) for(let shift=0;shift<20;shift++) {
    const values=[.05+shift*.01,.25,.5,.85];
    const result=adjust(values,index,n/100);
    result.forEach(v=>assert.ok(Number.isFinite(v)&&v>=0&&v<=1.000001));
    for(let i=1;i<4;i++) assert.ok(Math.abs(result[i]*values[0]-result[0]*values[i])<1e-6);
    const separate=adjust(values,index,n/100,false);
    separate.forEach((v,i)=>assert.equal(v,i===index?n/100:values[i]));
    checks++;
}
const view=read('Layers/Editors/ShapeCornerSettingsView.cs');
assert.match(view,/string\[\] labels = \{ "TL", "TR", "BR", "BL" \}/);
assert.match(view,/new FloatField\(labels\[i\]\)/);
assert.match(view,/"Change Shape Corners", \(\) => layer.cornerRoundness = next/);
assert.match(view,/"Link Shape Corners", \(\) => layer.linkCorners = !layer.linkCorners/);
assert.match(view,/SetValueWithoutNotify\(next\[j\] \* 100f\)/);
assert.match(view,/evt.newValue \/ 100f, layer.linkCorners/);
assert.match(read('Automation/WhimTexApi.Shape.cs'),/\["cornerRoundness"\] = new JArray/);
assert.match(read('Shaders/Shape.shader'),/p.y >= 0 \? \(p.x < 0 \? _ShapeCorners.x : _ShapeCorners.y\)\s*: \(p.x < 0 \? _ShapeCorners.w : _ShapeCorners.z\)/);
console.log(`Shape corners: ${checks} extracted proportional/independent edits plus API, shader ordering and labelled UI contracts passed (GPU is tested separately).`);
