// Live API/brush field definitions. Enum spellings come from the implementation.
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
  fillPattern: object({ shape: choice('Triangles Squares Hexagons Circles'), circleLayout: choice('Square Dense'),
    size: { oneOf: [number(1,16384), { type: 'array', items: number(1,16384), minItems: 2, maxItems: 2 }] }, linkSize: bool,
    rotation: number(-360000,360000), offset: vec, seamless: bool,
    gap: number(0,.99), roundness: number(0,1), bulge: number(0,1), distanceRange: number(.001,16),
    position: choice('Outside Inside Center Signed'), inverted: bool, profile: str(65536), gradient: { $ref: '#/$defs/gradient' },
    cellColor: choice('Uniform Random Pattern'), colorBlend: choice('Multiply ReplaceRGB'), seed: integer(-2147483648,2147483647),
    variation: number(0,1), palette: { $ref: '#/$defs/gradient' } }),
  color: rgba,
  gradientStops: { type: 'array', minItems: 1, maxItems: 64, items: object({ time: number(0, 1), color: rgba, midpoint: number(.01,.99), alphaMidpoint: number(.01,.99) }, ['time', 'color']), description: 'Stops must have strictly increasing times.' },
  gradient: { oneOf: [{ $ref: '#/$defs/gradientStops' }, object({ colors: { $ref: '#/$defs/gradientStops' }, alphas: {type:'array', minItems:1, maxItems:64, items:object({time:number(0,1),alpha:number(0,1),midpoint:number(.01,.99)},['time','alpha'])}, mode:choice('Classic Linear Perceptual Fixed'), wrapMode:choice('Clamp Repeat Mirror'), smoothness:number(0,1), colorSpace:choice('Gamma Linear') }, ['colors'])] },
  noise: object({ noiseType: noiseEnum('NoiseType'), seed: integer(-2147483648, 2147483647), scale: { oneOf: [number(.01, 1000), tuple(number(.01, 1000), 2)] },
    linkScale: { ...bool, default: true, description: 'Scale chain in the UI. Explicit API scale values are applied literally, even when linked.' },
    periodic: { ...noiseEnum('PeriodicAxes'), default: 'None', description: 'UI Seamless: X joins left/right, Y joins top/bottom, XY joins both. Ignored in OneD and White/Blue Noise; retained when inactive. Z never repeats.' },
    periodic1D: { ...bool, default: false, description: 'UI Seamless in OneD: repeat along the projected noise axis, including Fractal and Warp. Independent of periodic; ignored outside OneD and for White/Blue. Angled stripes need not tile at canvas edges.' },
    offset: { type: 'array', items: number(-10000, 10000), minItems: 2, maxItems: 3, default: [0,0,0], description: '[x,y] preserves Z in updates; [x,y,z] sets all axes. Z selects a ThreeD slice. XY uses noise-space units, or canvas pixels for White/Blue.' },
    fractal: noiseEnum('FractalType'), octaves: integer(1, 8), lacunarity: number(1, 4), gain: number(0, 1), weightedStrength: number(0, 1), pingPongStrength: number(.01, 8),
    cellularDistance: noiseEnum('CellularDistance'), cellularReturn: noiseEnum('CellularReturn'), cellularJitter: number(0, 1), warp: noiseEnum('WarpType'), warpStrength: number(0, 100), warpScale: { oneOf: [number(.01, 1000), tuple(number(.01, 1000), 2)], default: [1,1], description: 'X/Y multipliers of Noise Scale. Z frequency unchanged. Seamless fits the resulting warp periods.' }, linkWarpScale: { ...bool, default: true, description: 'Warp Scale chain: UI and Random All preserve proportions; explicit API axes apply literally.' },
    encoding: { ...noiseEnum('OutputEncoding'), default: 'LinearData', description: 'UI Output. Gradient maps monochrome noise through its RGBA/HDR palette. Color White/Blue temporarily renders stored Gradient as ColorValues.' },
    inverted: { ...bool, default: false, description: 'Reverses noise values in all outputs, before gradient sampling.' },
    gradient: { $ref: '#/$defs/gradient', description: 'Default opaque black at 0, white at 1, Perceptual interpolation. Replaces the complete palette; does not change encoding. Retained in other output modes.' },
    dimensions: { ...noiseEnum('NoiseDimensions'), default: 'TwoD', description: 'OneD stripes, TwoD field or ThreeD slice at Offset Z. White/Blue temporarily renders stored ThreeD as TwoD.' }, direction: number(-180, 180), whiteNoiseColor: noiseEnum('WhiteNoiseColor'), whiteNoiseSize: number(1, 1024) }),
  blur: object({ mode: choice('Gaussian Linear Circular'), strength: number(0, 4), radius: number(0, 256), distance: number(0, 512), angle: number(-180, 180), arc: number(0, 360),
    center: tuple(number(0, 1), 2), direction: choice('Centered Forward Backward'), edges: choice('Transparent Clamp Repeat Mirror') }),
  sharpen: object({ algorithm: choice('Gaussian Adaptive'), strength: number(0, 4), radius: number(0, 32), threshold: number(0, 1), noiseReduction: number(0, 1), haloSuppression: number(0, 1), channelMode: choice('RGB Luminance'), edges: choice('Transparent Clamp Repeat Mirror') }),
  shape: object({ kind: choice('Rectangle Ellipse Polygon Star Line'), fill: bool, fillColor: rgba, stroke: bool, strokeColor: rgba, strokeWidth: number(0, 8192), feather: { ...number(0, 8192), default: 0 }, featherPosition: { ...choice('Inside Outside Centered'), default: 'Centered' }, roundness: number(0, 1),
    cornerRoundness: tuple(number(0, 1), 4), linkCorners: bool, sides: integer(3, 32), innerRadius: number(.01, 1) }),
  makeSeamless: object({ poissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), mirrorPoissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), offsetPoissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), mode: choice('Mirror ScreenedPoisson OffsetBlend PatchQuilting'), processRed: bool, processGreen: bool, processBlue: bool, processAlpha: bool, horizontal: choice('Off LeftToRight RightToLeft'), vertical: choice('Off BottomToTop TopToBottom'), blendWidth: number(.001, .5), mirrorTransitionStart: number(-1, .95), falloff: number(.25, 4), mirrorContrastCompensation: bool, mirrorContrast: number(0, 1), mirrorSeamCorrection: bool, mirrorAutoRadius: bool, mirrorCorrectionRadius: number(.005, .25), leftEdge: bool, rightEdge: bool, bottomEdge: bool, topEdge: bool, screeningRadius: number(.005, .25), edgeWidth: number(.02, .5), offsetTransitionStart: number(-1, .95), quiltingEdges: choice('AllEdges TopAndBottom LeftAndRight None'), quiltingWidth: number(.02, .45), quiltingAlongSearch: number(0, .25), quiltingFeather: number(0, 100), quiltingContrastCompensation: bool, quiltingContrast: number(0, 1), quiltingQuality: choice('Draft Normal High'), quiltingSeed: { type: 'integer', minimum: -2147483648, maximum: 2147483647 }, quiltingChannels: choice('Linked Independent'), quiltingSeamCorrection: bool, quiltingPoissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), quiltingCorrectionRadius: number(.005, .25), histogramContrast: number(0, 1), offsetContrastCompensation: bool, offsetSeamCorrection: bool, offsetAutoRadius: bool, offsetCorrectionRadius: number(.005, .25) }),
  normalMap: object({ mode: normalEnum('GenerationMode'), sourceChannel: normalEnum('HeightChannel'), inputSpace: normalEnum('InputSpace'), edges: normalEnum('EdgeMode'),
    derivative: normalEnum('DerivativeFilter'), alphaMode: normalEnum('AlphaMode'), output: normalEnum('OutputMode'), encoding: normalEnum('OutputEncoding'),
    strength: number(0, 128), blackLevel: number(0, 1), whiteLevel: number(.0001, 16), gamma: number(.05, 8), smoothing: number(0, 64), mediumRadius: number(.5, 128), largeRadius: number(.5, 512),
    fineDetail: number(0, 8), mediumDetail: number(0, 8), largeDetail: number(0, 8), lightRemoval: number(0, 1), inverted: bool, flipX: bool, flipY: bool, ignoreTransparent: bool }),
  transform: object({ position: { ...vec, description: 'Parent-local offset in canvas-pixel units; default [0,0], X right, Y up.' }, scale: { ...vec, description: 'Each absolute component must be at least 0.00001.' }, pivot: vec, rotation: number(-360000, 360000), matrix: tuple(number(-1e15,1e15),9), tiling: choice('Clip Repeat Mirror Source Clamp Unbounded') }),

};
const seamless = defs.makeSeamless.properties;
Object.assign(defs.noise.properties.scale, { default: [8,8], description: 'Scalar sets X and Y equally; [x,y] sets axes independently. Units span the shorter canvas side. Ignored by White/Blue. Seamless fits complete lattice cells per octave/warp, so small Scale changes can quantize.' });
for (const [field, type] of Object.entries({
  mode: 'SeamlessMode', horizontal: 'HorizontalDirection', vertical: 'VerticalDirection',
  poissonEdges: 'PoissonEdges', mirrorPoissonEdges: 'PoissonEdges', offsetPoissonEdges: 'PoissonEdges',
  quiltingEdges: 'PoissonEdges', quiltingPoissonEdges: 'PoissonEdges',
  quiltingQuality: 'QuiltingQuality', quiltingChannels: 'QuiltingChannels'
})) seamless[field] = enumeration('Layers/MakeSeamlessLayerBehaviour.cs', type);
// Defaults describe registry-created layers, not raw field initializers or partial updates.
const seamlessDefaults = {
  mode: 'OffsetBlend', horizontal: 'LeftToRight', vertical: 'BottomToTop',
  processRed: true, processGreen: true, processBlue: true, processAlpha: true,
  blendWidth: .2, falloff: 1, mirrorTransitionStart: -.25,
  mirrorContrastCompensation: false, mirrorContrast: 1, mirrorSeamCorrection: true,
  mirrorAutoRadius: true, mirrorCorrectionRadius: .05, mirrorPoissonEdges: 'AllEdges',
  leftEdge: true, rightEdge: true, bottomEdge: true, topEdge: true,
  edgeWidth: .2, offsetTransitionStart: -.25, offsetContrastCompensation: true, histogramContrast: 1,
  offsetSeamCorrection: true, offsetAutoRadius: true, offsetCorrectionRadius: .05, offsetPoissonEdges: 'AllEdges',
  screeningRadius: .05, poissonEdges: 'AllEdges',
  quiltingEdges: 'AllEdges', quiltingWidth: .2, quiltingAlongSearch: 0, quiltingFeather: 50,
  quiltingContrastCompensation: false, quiltingContrast: 1, quiltingQuality: 'Normal', quiltingSeed: 0,
  quiltingChannels: 'Linked', quiltingSeamCorrection: false, quiltingPoissonEdges: 'AllEdges', quiltingCorrectionRadius: .05
};
for (const [field, value] of Object.entries(seamlessDefaults)) seamless[field] = { ...seamless[field], default: value };
defs.makeSeamless.description = 'Targeted seam-processing layer. Defaults apply to new layers; omitted fields in updates retain their values. Main-pass edges and Poisson correction edges are independent. Percentage UI values use fractions except quiltingFeather.';
const seamlessDescriptions = {
  mode: 'Method. Inactive method settings are retained, not transferred or reset.',
  blendWidth: 'Mirror Blend Width: fraction of each corresponding canvas dimension.',
  edgeWidth: 'Offset Blend Width: fraction of each corresponding canvas dimension, at least two pixels.',
  quiltingWidth: 'Patch Width: fraction of each corresponding canvas dimension.',
  mirrorTransitionStart: 'Mirror Transition Start: fraction of Blend Width, not the canvas. Negative values may reopen the seam; changing this does not toggle Poisson correction.',
  offsetTransitionStart: 'Offset Transition Start: fraction of Blend Width, not the canvas. Negative values may reopen the seam; changing this does not toggle Poisson correction.',
  mirrorAutoRadius: 'Automatic Radius uses max(0.005, blendWidth/4); the manual radius remains stored.',
  offsetAutoRadius: 'Automatic Radius uses max(0.005, edgeWidth/4); the manual radius remains stored.',
  quiltingFeather: 'Feather is 0..100 percent, not a 0..1 fraction or pixels. Share of each cut\'s available safe transition width; not a texture blur.',
  quiltingAlongSearch: 'Along-Seam Search: fraction of usable strip length. Donor displacement tapers to zero at the ends, without wrapping.',
  quiltingChannels: 'Channel Matching: Linked uses one donor/cut for checked premultiplied channels; Independent searches each checked straight channel separately for packed maps.'
};
for (const field of ['poissonEdges', 'mirrorPoissonEdges', 'offsetPoissonEdges', 'quiltingEdges', 'quiltingPoissonEdges'])
  seamlessDescriptions[field] = 'Paired edges: TopAndBottom for vertical tiling, LeftAndRight for horizontal tiling. None bypasses only this pass, independently of other passes.';
for (const field of ['screeningRadius', 'mirrorCorrectionRadius', 'offsetCorrectionRadius', 'quiltingCorrectionRadius'])
  seamlessDescriptions[field] = 'Poisson Radius: fraction of the smaller canvas dimension. Global correction, not a hard band. Manual correction radii are unused while their Automatic Radius is enabled.';
for (const field of ['processRed', 'processGreen', 'processBlue', 'processAlpha'])
  seamlessDescriptions[field] = 'Channels: false restores this input channel after seam processing, before normal layer modifiers/FX/compositing. All false bypasses seam processing.';
for (const [field, description] of Object.entries(seamlessDescriptions)) seamless[field].description = description;
defs.transform.allOf = [{ if: { required: ['matrix'] }, then: { not: { anyOf: ['position','scale','rotation'].map(key => ({required:[key]})) } } }];
defs.sdf = object({
  metric: enumeration('Utils.cs', 'DistanceMetric'), sourceChannel: enumeration('Layers/SDFLayerBehaviour.cs', 'SourceChannel'),
  threshold: integer(0, 255), distancePosition: enumeration('Layers/SDFLayerBehaviour.cs', 'DistancePosition'),
  inverted: bool, maxDistance: number(0, 16384), sourceOffset: tuple(number(-16384,16384),2),
  sourceEdges: choice('Transparent Clamp Repeat Mirror'), contourOffset: number(-16384,16384),
  insideDistance: number(0,16384), outsideDistance: number(0,16384), profile: str(65536),
  encoding: { ...enumeration('Layers/SDFLayerBehaviour.cs', 'OutputEncoding'), default: 'Gradient' },
  gradient: { $ref: '#/$defs/gradient' }
});
defs.blend = enumeration('Utils.cs', 'BlendMode');
const schema = {
  $schema: 'https://json-schema.org/draft/2020-12/schema',
  title: 'WhimTex live API field definitions',
  description: 'Reusable field shapes for live API patches and brush authoring. Not a document or clipboard envelope; use document.schema.json for stored layers.',
  $defs: defs
};
const output = path.join(docs, 'AI', 'agent-fields.schema.json');
const serialized = JSON.stringify(schema, null, 2) + '\n';
if (process.argv.includes('--check')) {
  if (fs.readFileSync(output, 'utf8').replace(/\r\n/g, '\n') !== serialized) throw Error('API field schema is stale. Run this script without --check.');
} else { fs.mkdirSync(path.dirname(output), { recursive: true }); fs.writeFileSync(output, serialized); }
console.log('API field schema ' + (process.argv.includes('--check') ? 'checked.' : 'generated.'));
