// Documentation-only generator. Enum spellings come from the implementation.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const docs = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const src = path.join(docs, '..', 'src');
const read = file => fs.readFileSync(path.join(src, file), 'utf8');
const number = (minimum, maximum) => ({ type: 'number', minimum, maximum });
const integer = (minimum, maximum) => ({ type: 'integer', minimum, maximum });
const bool = { type: 'boolean' };
const str = maxLength => ({ type: 'string', maxLength });
const choice = values => ({ type: 'string', enum: values.split(' ') });
const object = (properties, required = []) => ({ type: 'object', additionalProperties: false, properties, ...(required.length ? { required } : {}) });
const tuple = (items, count) => ({ type: 'array', items, minItems: count, maxItems: count });
const vec = tuple(number(-1e6, 1e6), 2);
const rgba = { type: 'array', prefixItems: [number(-107, 107), number(-107, 107), number(-107, 107), number(0, 1)], items: false, minItems: 4, maxItems: 4 };
function enumeration(file, name) {
  const body = read(file).match(new RegExp('enum\\s+' + name + '\\s*\\{([^}]+)\\}'))?.[1];
  if (!body) throw Error(`Missing enum ${name}`);
  const values = body.replace(/\[[^\]]*\]/g, '').replace(/\/\/[^\n]*/g, '').split(',').map(v => v.trim().split(/[\s=]/)[0]).filter(Boolean);
  return { type: 'string', enum: values };
}
const noiseEnum = name => enumeration('Layers/NoiseLayerBehaviour.cs', name);
const normalEnum = name => enumeration('Layers/NormalMapLayerBehaviour.cs', name);
const defs = {
  color: rgba,
  gradientStops: { type: 'array', minItems: 1, maxItems: 64, items: object({ time: number(0, 1), color: rgba, midpoint: number(.01,.99), alphaMidpoint: number(.01,.99) }, ['time', 'color']), description: 'Stops must have strictly increasing times.' },
  gradient: { oneOf: [{ $ref: '#/$defs/gradientStops' }, object({ colors: { $ref: '#/$defs/gradientStops' }, alphas: {type:'array', minItems:1, maxItems:64, items:object({time:number(0,1),alpha:number(0,1),midpoint:number(.01,.99)},['time','alpha'])}, mode:choice('Classic Linear Perceptual Fixed'), smoothness:number(0,1), colorSpace:choice('Gamma Linear') }, ['colors'])] },
  gradientOptions: object({ type: enumeration('Layers/GradientLayerBehaviour.cs', 'GradientType'), repetitions: number(.00001, 1000), wrap: choice('Repeat PingPong'), mode: choice('Classic Linear Perceptual Fixed'), smoothness:number(0,1) }),
  noise: object({ noiseType: noiseEnum('NoiseType'), seed: integer(-2147483648, 2147483647), scale: number(.01, 1000), offset: tuple(number(-10000, 10000), 2),
    fractal: noiseEnum('FractalType'), octaves: integer(1, 8), lacunarity: number(1, 4), gain: number(0, 1), weightedStrength: number(0, 1), pingPongStrength: number(.01, 8),
    cellularDistance: noiseEnum('CellularDistance'), cellularReturn: noiseEnum('CellularReturn'), cellularJitter: number(0, 1), warp: noiseEnum('WarpType'), warpStrength: number(0, 100),
    encoding: noiseEnum('OutputEncoding'), inverted: bool, dimensions: noiseEnum('NoiseDimensions'), direction: number(-180, 180), whiteNoiseColor: noiseEnum('WhiteNoiseColor'), whiteNoiseSize: number(1, 1024) }),
  blur: object({ mode: choice('Gaussian Linear Circular'), strength: number(0, 4), radius: number(0, 256), distance: number(0, 512), angle: number(-180, 180), arc: number(0, 360),
    center: tuple(number(0, 1), 2), direction: choice('Centered Forward Backward'), edges: choice('Transparent Clamp Repeat Mirror') }),
  sharpen: object({ algorithm: choice('Gaussian Adaptive'), strength: number(0, 4), radius: number(0, 32), threshold: number(0, 1), noiseReduction: number(0, 1), haloSuppression: number(0, 1), channelMode: choice('RGB Luminance'), edges: choice('Transparent Clamp Repeat Mirror') }),
  shape: object({ kind: choice('Rectangle Ellipse Polygon Star Line'), fill: bool, fillColor: rgba, stroke: bool, strokeColor: rgba, strokeWidth: number(0, 8192), roundness: number(0, 1),
    cornerRoundness: tuple(number(0, 1), 4), linkCorners: bool, sides: integer(3, 32), innerRadius: number(.01, 1) }),
  makeSeamless: object({ horizontal: choice('Off LeftToRight RightToLeft'), vertical: choice('Off BottomToTop TopToBottom'), blendWidth: number(.001, .5), falloff: number(.25, 4) }),
  normalMap: object({ mode: normalEnum('GenerationMode'), sourceChannel: normalEnum('HeightChannel'), inputSpace: normalEnum('InputSpace'), edges: normalEnum('EdgeMode'),
    derivative: normalEnum('DerivativeFilter'), alphaMode: normalEnum('AlphaMode'), output: normalEnum('OutputMode'), encoding: normalEnum('OutputEncoding'),
    strength: number(0, 128), blackLevel: number(0, 1), whiteLevel: number(.0001, 16), gamma: number(.05, 8), smoothing: number(0, 64), mediumRadius: number(.5, 128), largeRadius: number(.5, 512),
    fineDetail: number(0, 8), mediumDetail: number(0, 8), largeDetail: number(0, 8), lightRemoval: number(0, 1), inverted: bool, flipX: bool, flipY: bool, ignoreTransparent: bool }),
  transform: object({ position: { ...vec, description: 'Parent-local offset in canvas-pixel units; default [0,0], X right, Y up.' }, scale: { ...vec, description: 'Each absolute component must be at least 0.00001.' }, pivot: vec, rotation: number(-360000, 360000), matrix: tuple(number(-1e15,1e15),9), tiling: choice('Clip Repeat Mirror Source Clamp Unbounded') }),
  fx: object({ name: str(4096), enabled: bool,
    gradients: { type: 'object', additionalProperties: { $ref: '#/$defs/gradient' } },
    textures: { type: 'object', additionalProperties: object({ layer: str(64) }, ['layer']) },
    code: { type: 'string', minLength: 1, maxLength: 65536, description: 'Portable ApplyFX HLSL, at most 64 KiB UTF-8 and 32 parameters. Declare values with // @param. Conditional/define directives and explicitly allowlisted built-in includes are supported; other includes must be expanded by Copy as Portable. No asset GUIDs.' } }, ['code'])
};
defs.transform.allOf = [{ if: { required: ['matrix'] }, then: { not: { anyOf: ['position','scale','rotation'].map(key => ({required:[key]})) } } }];
const ref = name => ({ $ref: '#/$defs/' + name });
const common = { enabled: bool, clippingMask: bool, opacity: number(0, 1), blend: enumeration('Utils.cs', 'BlendMode'), colorRange: choice('Standard HDR'), blendRange: choice('Standard HDR'),
  swizzle: tuple({ type: 'string', enum: ['R', 'G', 'B', 'A', '1-R', '1-G', '1-B', '1-A', '0', '1', 'R * A', 'G * A', 'B * A'] }, 4) };
const metric = enumeration('Utils.cs', 'DistanceMetric');
const extra = {
  color: { color: rgba, fillMode: choice('Color UV') }, gradient: { gradient: ref('gradient'), gradientOptions: ref('gradientOptions') }, noise: { noise: ref('noise') }, shape: { shape: ref('shape') },
  blur: { blur: ref('blur') }, sharpen: { sharpen: ref('sharpen') }, makeSeamless: { makeSeamless: ref('makeSeamless') }, normalMap: { normalMap: ref('normalMap') },
  outline: { metric, color: rgba, outlineWidth: number(0, 16384), outlineSoftness: number(0, 16384), outlinePosition: enumeration('Layers/OutlineLayerBehaviour.cs', 'OutlinePosition'), outlineOffset: number(-16384, 16384), fillCenter: bool, fillColor: rgba },
  sdf: { metric, sourceChannel: enumeration('Layers/SDFLayerBehaviour.cs', 'SourceChannel'), threshold: integer(0, 255), distancePosition: enumeration('Layers/SDFLayerBehaviour.cs', 'DistancePosition'), inverted: bool, maxDistance: number(0, 16384), sourceOffset: tuple(number(-16384,16384),2), sourceEdges: choice('Transparent Clamp Repeat Mirror'), contourOffset: number(-16384,16384), insideDistance: number(0,16384), outsideDistance: number(0,16384), profile: str(65536), gradient: ref('gradient') },
  shaderProcessor: {}, drawing: {}, file: {}, group: { compositing: choice('PassThrough Isolated') }
};
defs.layer = { oneOf: Object.entries(extra).map(([type, properties]) => {
  const fields = { type: { const: type }, id: { ...str(64), minLength: 1 }, name: str(128), properties: object({ ...common, ...properties, ...(type !== 'group' ? { filter: choice('Source Point Bilinear Trilinear') } : {}) }) };
  if (type === 'group') fields.children = { type: 'array', minItems: 1, maxItems: 128, items: ref('layer') };
  fields.transform = ref('transform');
  fields.fx = { type: 'array', maxItems: 16, items: ref('fx') };
  if (['outline', 'sdf', 'blur', 'sharpen', 'normalMap', 'makeSeamless'].includes(type)) fields.target = { ...str(64), minLength: 1 };
  if (type === 'drawing') fields.url = { type: 'string', maxLength: 2048, pattern: '^https?://',
    description: 'Absolute http(s) link to a PNG or JPEG. Downloaded on paste after confirmation, keeping source resolution. If neither scale nor matrix is specified, fit to the canvas; otherwise preserve the explicit transform.' };
  if (type === 'shaderProcessor') fields.properties.properties.clippingMask = { const: false };
  if (type === 'drawing' || type === 'file') fields.contentOmitted = bool;
  if (type === 'file') fields.asset = object({
    guid: { type: 'string', pattern: '^[0-9a-fA-F]{32}$' },
    localId: { type: 'string', pattern: '^[+-]?[0-9]+$', maxLength: 20 }
  }, ['guid']);
  const result = object(fields, ['type']);
  if (type === 'drawing' || type === 'file') result.allOf = [{
    if: { required: ['contentOmitted'], properties: { contentOmitted: { const: true } } },
    then: { not: { anyOf: [{ required: ['url'] }, { required: ['asset'] }] } }
  }];
  return result;
}) };
const schema = {
  $schema: 'https://json-schema.org/draft/2020-12/schema',
  title: 'WhimTex clipboard layer JSON, version 1',
  description: '1 MiB maximum; 128 total layers, 8 nested groups, 16 total shaders, 16 linked images. IDs must be unique, targets must resolve without cycles. Canvas at most 16,777,216 pixels. A Drawing layer with url downloads one image (PNG or JPEG, at most 64 MB and 16 megapixels) after a confirmation. Unity also checks cross-field and shader constraints.',
  ...object({ format: { const: 'whimtex.layers' }, version: { const: 1 }, canvas: object({ width: integer(1, 16384), height: integer(1, 16384), filter: choice('Point Bilinear Trilinear') }, ['width', 'height']), layers: { type: 'array', minItems: 1, maxItems: 128, items: ref('layer') } }, ['format', 'version', 'layers']),
  $defs: defs
};
const output = path.join(docs, 'AI', 'layers.schema.json');
const serialized = JSON.stringify(schema, null, 2) + '\n';
if (process.argv.includes('--check')) {
  if (fs.readFileSync(output, 'utf8').replace(/\r\n/g, '\n') !== serialized) throw Error('Clipboard schema is stale. Run this script without --check.');
} else { fs.mkdirSync(path.dirname(output), { recursive: true }); fs.writeFileSync(output, serialized); }
console.log('Clipboard schema ' + (process.argv.includes('--check') ? 'checked.' : 'generated.'));
