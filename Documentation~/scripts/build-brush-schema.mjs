import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const docs=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const number=(minimum,maximum)=>({type:'number',minimum,maximum});
const integer=(minimum,maximum)=>({type:'integer',minimum,maximum});
const choice=values=>({type:'string',enum:values.split(' ')});
const object=(properties,required=[])=>({type:'object',additionalProperties:false,properties,required});
const layers=JSON.parse(fs.readFileSync(path.join(docs,'AI/layers.schema.json'),'utf8'));
const settings=object({
  size:number(1,4096),hardness:number(0,1),spacing:number(.01,4),opacity:number(0,1),flow:number(0,1),
  scatter:number(0,4),scatterBias:number(-1,1),sizeJitter:number(0,1),angleJitter:number(0,180),
  angleOffset:number(-180,180),flipX:number(0,1),flipY:number(0,1),
  rotationMode:choice('Fixed StrokeDirection'),randomAlgorithm:choice('Random Sobol'),
  tipChannel:choice('Alpha Luminance InvertedLuminance Color'),tipSdf:{type:'boolean'},
  mode:choice('Hardness Gradient'),tipGradient:{$ref:'#/$defs/gradient'},tintGradient:{$ref:'#/$defs/gradient'},
  blend:{type:'string',enum:layers.$defs.layer.oneOf[0].properties.properties.properties.blend.enum.filter(x=>x!=='None'&&x!=='Overwrite')},
  blendApplication:choice('Stroke Stamp'),seed:integer(1,2147483647)
});
const common={format:{const:'whimtex.brush'},version:{const:1},name:{type:'string',maxLength:4096},settings:{$ref:'#/$defs/settings'}};
const schema={
  $schema:'https://json-schema.org/draft/2020-12/schema',
  title:'WhimTex clipboard brush JSON, version 1',
  description:'1 MiB UTF-8 JSON maximum. Replaces the current brush, not document layers. HLSL is compiled on the GPU: trust the source. Texture URL downloads require confirmation. Runtime validates shader syntax and gradient ordering.',
  oneOf:[
    object({...common,source:{const:'Standard'},url:{type:'string',maxLength:4096,pattern:'^https?://[^\\s]+$'}},['format','version','source']),
    object({...common,source:{const:'HLSL'},code:{type:'string',minLength:1,maxLength:65536},resolution:integer(32,2048)},['format','version','source','code'])
  ],
  $defs:{settings,gradient:layers.$defs.gradient,gradientStops:layers.$defs.gradientStops}
};
const output=path.join(docs,'AI/brush.schema.json');
const serialized=JSON.stringify(schema,null,2)+'\n';
if(process.argv.includes('--check')){
  if(fs.readFileSync(output,'utf8').replace(/\r\n/g,'\n')!==serialized)throw Error('Brush schema is stale.');
}else fs.writeFileSync(output,serialized);
console.log('Brush schema '+(process.argv.includes('--check')?'checked.':'generated.'));
