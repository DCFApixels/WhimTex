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
const transformVec = tuple(number(-1e15, 1e15), 2);
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
  text: object({ text: str(8192), fontFamily: str(4096), fontStyle: choice('Normal Bold Italic BoldAndItalic'),
    casing: { ...enumeration('Layers/TextLayerBehaviour.cs', 'TextCasing'), default: 'Normal', description: 'Display-only casing. SmallCaps draws lowercase as smaller capitals; stored text is unchanged.' },
    edgeMode: { ...enumeration('Layers/TextLayerBehaviour.cs', 'TextEdgeMode'), default: 'HintedAA', description: 'Glyph rasterization: HintedAA (default) uses grid-fitted antialiasing; Antialiased uses antialiasing without hinting; Pixel uses binary glyph coverage, snapped glyph placement and nearest sampling by default. Explicit filtering and FX can soften Pixel output; this is not final-alpha quantization. Omitted fields in partial updates retain their values.' },
    fontSize: { ...number(1,2048), description: 'Fixed size, or minimum size with Frame Auto Size enabled.' },
    maxFontSize: { ...number(1,2048), default: 256, description: 'Maximum size with Frame Auto Size enabled. Range must contain a whole-pixel size.' },
    characterHorizontalScale: { ...number(.01,10), default: 1, description: 'Glyph-width and advance multiplier. Height and additive em spacing are unchanged. Used by wrapping, Auto Size and Ellipsis.' },
    spacing: { ...object({ character: number(-1,10), word: number(-1,10), line: number(-1,10), paragraph: number(-1,10) }),
      description: 'Extra spacing in em of the effective font size. All default to zero. Paragraph applies only after explicit breaks. Partial patches preserve omitted values.' },
    alignment: choice('UpperLeft UpperCenter UpperRight MiddleLeft MiddleCenter MiddleRight LowerLeft LowerCenter LowerRight'),
    layoutMode: enumeration('Layers/TextLayerBehaviour.cs', 'TextLayoutMode'), frameSize: tuple(number(1,32768),2),
    layoutOffset: { ...vec, default: [0,0], description: 'Layout translation in local document pixels before rasterization, separate from the layer transform. Used by Point/Frame layout handles and preserved across Point/Frame switches. Frame resize changes this offset and frameSize, never the layer transform.' },
    wrapping: enumeration('Layers/TextLayerBehaviour.cs', 'TextWrapping'),
    overflow: { ...enumeration('Layers/TextLayerBehaviour.cs', 'TextOverflowMode'), default: 'None', description: 'Frame overflow: None keeps text outside the frame, Clip cuts it, Ellipsis shortens visible text with an ellipsis. Ignored for Point text.' }, justify: bool,
    autoSize: { ...bool, default: false, description: 'Fit text into Frame between fontSize and maxFontSize. Ignored for Point text.' }, color: rgba }),
  fillPattern: object({ shape: choice('Triangles Squares Hexagons Circles'), circleLayout: choice('Square Dense'),
    size: { oneOf: [number(1,16384), { type: 'array', items: number(1,16384), minItems: 2, maxItems: 2 }] }, linkSize: bool,
    rotation: number(-360000,360000), offset: vec, seamless: bool,
    gap: number(0,.99), roundness: number(0,1), bulge: number(0,1), distanceRange: number(.001,16),
    position: choice('Outside Inside Center Signed'), inverted: bool, profile: str(4096), gradient: { $ref: '#/$defs/gradient' },
    cellColor: choice('Uniform Random Pattern'), colorBlend: choice('Multiply ReplaceRGB'), seed: integer(-2147483648,2147483647),
    variation: number(0,1), palette: { $ref: '#/$defs/gradient' } }),
  color: rgba,
  gradientStops: { type: 'array', minItems: 1, maxItems: 64, items: object({ time: number(0, 1), color: rgba, midpoint: number(.01,.99), alphaMidpoint: number(.01,.99) }, ['time', 'color']), description: 'Stops must have strictly increasing times.' },
  gradient: { oneOf: [{ $ref: '#/$defs/gradientStops' }, object({ colors: { $ref: '#/$defs/gradientStops' }, alphas: {type:'array', minItems:1, maxItems:64, items:object({time:number(0,1),alpha:number(0,1),midpoint:number(.01,.99)},['time','alpha'])}, mode:choice('Classic Linear Perceptual Fixed'), wrapMode:choice('Clamp Repeat Mirror'), smoothness:number(0,1), colorSpace:choice('Gamma Linear') }, ['colors'])] },
  noise: object({ noiseType: noiseEnum('NoiseType'), seed: integer(-2147483648, 2147483647), scale: { oneOf: [number(.01, 1000), { type: 'array', items: number(.01, 1000), minItems: 2, maxItems: 3 }] },
    field: { ...noiseEnum('Field'), default: 'Value', description: 'Value: scalar/grain; Curl: swirling flow; GradientVector: derivative; CellDirection: nearest cellular center. Unsupported stored Noise Type temporarily uses OpenSimplex2 (CellDirection uses Cellular). Hidden settings are retained.' },
    vectorOutput: { ...noiseEnum('VectorOutput'), default: 'PackedVector', description: 'Linear RGB = XYZ * 0.5 + 0.5 or signed XYZ. No gamma or implicit clipping. Alpha 1. Color Range and combining layers/groups require HDR for signed or out-of-range values.' },
    normalize: { ...bool, default: false, description: 'Vector fields only. Normalize nonzero vectors before Strength; zero remains zero.' },
    strength: { ...number(0, 100), default: 1, description: 'Vector magnitude multiplier after optional normalization.' },
    linkScale: { ...bool, default: true, description: 'Scale chain in the UI. Explicit API scale values are applied literally, even when linked.' },
    periodic: { ...noiseEnum('PeriodicAxes'), default: 'None', description: 'UI Seamless: X joins left/right, Y joins top/bottom, XY joins both. Ignored in OneD and White/Blue Noise; retained when inactive. Z never repeats.' },
    periodic1D: { ...bool, default: false, description: 'UI Seamless in OneD: repeat along the projected noise axis, including Fractal and Warp. Independent of periodic; ignored outside OneD and for White/Blue. Angled stripes need not tile at canvas edges.' },
    offset: { type: 'array', items: number(-10000, 10000), minItems: 2, maxItems: 3, default: [0,0,0], description: '[x,y] preserves Z in updates; [x,y,z] sets all axes. Z selects a ThreeD slice. XY uses noise-space units, or canvas pixels for White/Blue.' },
    fractal: noiseEnum('FractalType'), octaves: integer(1, 8), lacunarity: number(1, 4), gain: number(0, 1), weightedStrength: number(0, 1), pingPongStrength: number(.01, 8),
    cellularDistance: noiseEnum('CellularDistance'), cellularReturn: noiseEnum('CellularReturn'), cellularJitter: number(0, 1), warp: noiseEnum('WarpType'), warpSeed: { ...integer(-2147483648, 2147483647), default: 1337, description: 'Independent seed for Domain Warp. Ignored when Warp is disabled or its Strength is zero.' }, warpStrength: number(0, 100), warpScale: { oneOf: [number(.01, 1000), { type: 'array', items: number(.01, 1000), minItems: 2, maxItems: 3 }], default: [1,1,1], description: 'XYZ multipliers of Noise Scale. Number sets active axes; [x,y] preserves Z; [x,y,z] sets all axes. Inspect returns XYZ. Seamless fits resulting warp periods; Z never repeats.' }, linkWarpScale: { ...bool, default: true, description: 'Warp Scale chain: UI and Random All preserve proportions; explicit API axes apply literally.' },
    encoding: { ...noiseEnum('OutputEncoding'), default: 'LinearData', description: 'UI Output for Field Value only. Gradient maps monochrome noise through its RGBA/HDR palette. Color White/Blue temporarily renders stored Gradient as ColorValues. Vector fields use vectorOutput instead.' },
    inverted: { ...bool, default: false, description: 'Scalar: 1-value before palette. Vector: reverse direction before packing. Applies to every output.' },
    gradient: { $ref: '#/$defs/gradient', description: 'Default opaque black at 0, white at 1, Perceptual interpolation. Replaces the complete palette; does not change encoding. Retained in other output modes.' },
    dimensions: { ...noiseEnum('NoiseDimensions'), default: 'TwoD', description: 'OneD stripes, TwoD field or ThreeD slice at Offset Z. White/Blue temporarily renders stored ThreeD as TwoD; vector fields render stored OneD as TwoD. Inactive values are retained.' }, direction: number(-180, 180), grainColor: noiseEnum('GrainColor'), grainSize: number(1, 1024) }),
  blur: object({ mode: choice('Gaussian Linear Circular'), strength: number(0, 4), radius: number(0, 256), distance: number(0, 512), angle: number(-180, 180), arc: number(0, 360),
    center: tuple(number(0, 1), 2), direction: choice('Centered Forward Backward'), edges: choice('Transparent Clamp Repeat Mirror') }),
  sharpen: object({ algorithm: choice('Gaussian Adaptive'), strength: number(0, 4), radius: number(0, 32), threshold: number(0, 1), noiseReduction: number(0, 1), haloSuppression: number(0, 1), channelMode: choice('RGB Luminance'), edges: choice('Transparent Clamp Repeat Mirror') }),
  shapeCorner: object({ style: choice('Round Bevel'), amount: number(0, 1) }),
  shape: object({ kind: choice('Rectangle Ellipse Polygon Star Line Arc Sector'),
    fill: { ...bool, default: true, description: 'Overall body-fill gate. Disabling it retains the complete fill style.' }, fillColor: rgba,
    fillMode: { ...enumeration('Layers/VectorModel.cs', 'VectorFillMode'), default: 'Solid', description: 'Shared Shape/Vector fill mode. None disables fill; Solid uses fillColor; the five gradient modes use gradient. Inactive settings are retained in every JSON write mode.' },
    gradient: { $ref: '#/$defs/gradient', description: 'Replaces the complete palette; default black-to-white Perceptual/Clamp. Uses existing wrapMode after the circular-only gradientRepetitions multiplier.' },
    gradientStart: { ...vec, default: [0, 0], description: 'Normalized layer-local y-up start, or center for nonlinear gradients. Allowed outside 0..1. Shape uses current canvas width/height before layer Transform; Vector uses referenceSize.' },
    gradientEnd: { ...vec, default: [1, 0], description: 'Normalized endpoint setting radius and orientation in reference pixels. Square uses max(abs(rotated coordinates)); Diamond uses sumabs. Circular advances CCW from the start-to-end ray, which is its seam. Equal anchors give t=0 for every gradient type.' },
    gradientRepetitions: { ...number(.0001, 3.40282347e38), default: 1, description: 'Finite circular-gradient repetitions, at least 0.0001; default 1. CircularGradient only: one period spans 360/gradientRepetitions degrees from the gradientStart-to-gradientEnd ray, before palette wrapping. Retained and validated in every fill mode, including disabled fills.' },
    stroke: bool, strokeColor: rgba, strokeWidth: number(0, 8192),
    strokeMode: { ...enumeration('Layers/VectorModel.cs', 'VectorFillMode'), default: 'Solid', description: 'Independent stroke paint. None disables paint without clearing settings; Solid uses strokeColor; the five gradient modes use strokeGradient. stroke remains an additional gate. Inactive settings are retained in every JSON write mode.' },
    strokeGradient: { $ref: '#/$defs/gradient', description: 'Replaces only the complete stroke palette; default black-to-white Perceptual/Clamp. Independent HDR/alpha stops, midpoints, interpolation, smoothness, colorSpace and wrapMode. Wrapping follows the circular-only strokeGradientRepetitions multiplier.' },
    strokeGradientStart: { ...tuple(number(-3.40282347e38, 3.40282347e38), 2), default: [0, 0], description: 'Finite normalized layer-local y-up start, or center for nonlinear gradients; allowed outside 0..1. Shape uses canvas dimensions before Transform; Vector and Boolean Group use referenceSize. Independent of fill anchors and layer transform.' },
    strokeGradientEnd: { ...tuple(number(-3.40282347e38, 3.40282347e38), 2), default: [1, 0], description: 'Finite local endpoint setting radius and orientation from strokeGradientStart. Same five gradient geometries as fill. Circular advances CCW from this ray, with its seam on the ray. Equal anchors give t=0.' },
    strokeGradientRepetitions: { ...number(.0001, 3.40282347e38), default: 1, description: 'Finite circular stroke repetitions, at least 0.0001. One period spans 360/strokeGradientRepetitions degrees before palette wrapping. Retained and validated with stroke false and in every strokeMode; affects CircularGradient only.' },
    arcThickness: { ...number(0, 8192), default: 8, description: 'Arc body thickness in canvas pixels, independent of strokeWidth. Transform controls the centerline ellipse.' },
    strokePosition: { ...choice('Inside Center Outside'), default: 'Inside', description: 'Alignment relative to the body contour. Shape retains Inside=0, Center=1, Outside=2; do not cast Vector position values.' },
    strokeJoin: { ...enumeration('Layers/VectorModel.cs', 'VectorStrokeJoin'), default: 'Round', description: 'Join of the outline, independent of body corner settings.' },
    strokeCap: { ...enumeration('Layers/VectorModel.cs', 'VectorStrokeCap'), default: 'Butt', description: 'Caps of stroke dashes. Separate from lineCap, which shapes the Line/Arc body ends.' },
    miterLimit: { ...number(1, 10000), default: 4, description: 'Maximum miter ratio before a Miter join becomes Bevel. Retained for other joins.' },
    dashPattern: { type: ['array', 'null'], maxItems: 256, items: { type: 'number', exclusiveMinimum: 0, maximum: 10000000 }, default: [],
      description: 'Alternating dash/gap lengths, always in multiples of strokeWidth, independent of stroke position. Empty/null is solid; odd lists repeat twice. Replaces the whole pattern. Shape has no dashPatternRelative field.' },
    dashOffset: { ...number(-10000000, 10000000), default: 0, description: 'Phase along the contour in document pixels, never a stroke-width multiplier. Shape width and dash metrics stay fixed under layer transforms; Vector scales its local geometry.' },
    lineCap: { ...choice('Butt Round Square'), default: 'Round', description: 'Line/Arc body end caps, independent of strokeCap.' }, edgeMode: choice('Antialiased Step'),
    feather: { ...number(0, 8192), default: 0 }, featherPosition: { ...choice('Inside Outside Centered'), default: 'Centered' },
    rectangleCorners: { type: 'array', minItems: 4, maxItems: 4, items: { $ref: '#/$defs/shapeCorner' } },
    polygonCorners: { type: 'array', minItems: 3, maxItems: 32, items: { $ref: '#/$defs/shapeCorner' } },
    outerCorner: { $ref: '#/$defs/shapeCorner' }, innerCorner: { $ref: '#/$defs/shapeCorner' },
    linkCorners: bool, sides: integer(3, 32), innerRadius: number(.01, 1), startAngle: number(-360000, 360000), sweepAngle: number(0, 360) }),
  makeSeamless: object({ poissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), mirrorPoissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), offsetPoissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), mode: choice('Mirror ScreenedPoisson OffsetBlend PatchQuilting'), processRed: bool, processGreen: bool, processBlue: bool, processAlpha: bool, horizontal: choice('Off LeftToRight RightToLeft'), vertical: choice('Off BottomToTop TopToBottom'), blendWidth: number(.001, .5), mirrorTransitionStart: number(-1, .95), falloff: number(.25, 4), mirrorContrastCompensation: bool, mirrorContrast: number(0, 1), mirrorSeamCorrection: bool, mirrorAutoRadius: bool, mirrorCorrectionRadius: number(.005, .25), leftEdge: bool, rightEdge: bool, bottomEdge: bool, topEdge: bool, screeningRadius: number(.005, .25), edgeWidth: number(.02, .5), offsetTransitionStart: number(-1, .95), quiltingEdges: choice('AllEdges TopAndBottom LeftAndRight None'), quiltingWidth: number(.02, .45), quiltingAlongSearch: number(0, .25), quiltingFeather: number(0, 100), quiltingContrastCompensation: bool, quiltingContrast: number(0, 1), quiltingQuality: choice('Draft Normal High'), quiltingSeed: { type: 'integer', minimum: -2147483648, maximum: 2147483647 }, quiltingChannels: choice('Linked Independent'), quiltingSeamCorrection: bool, quiltingPoissonEdges: choice('AllEdges TopAndBottom LeftAndRight None'), quiltingCorrectionRadius: number(.005, .25), histogramContrast: number(0, 1), offsetContrastCompensation: bool, offsetSeamCorrection: bool, offsetAutoRadius: bool, offsetCorrectionRadius: number(.005, .25) }),
  normalMap: object({ mode: normalEnum('GenerationMode'), sourceChannel: normalEnum('HeightChannel'), inputSpace: normalEnum('InputSpace'), edges: normalEnum('EdgeMode'),
    derivative: normalEnum('DerivativeFilter'), alphaMode: normalEnum('AlphaMode'), output: normalEnum('OutputMode'), encoding: normalEnum('OutputEncoding'),
    strength: number(0, 128), blackLevel: number(0, 1), whiteLevel: number(.0001, 16), gamma: number(.05, 8), smoothing: number(0, 64), mediumRadius: number(.5, 128), largeRadius: number(.5, 512),
    fineDetail: number(0, 8), mediumDetail: number(0, 8), largeDetail: number(0, 8), lightRemoval: number(0, 1), inverted: bool, flipX: bool, flipY: bool, ignoreTransparent: bool }),
  transform: object({ reset: bool, originalAspect: bool, position: { ...transformVec, description: 'Parent-local offset in canvas-pixel units; default [0,0], X right, Y up.' }, scale: { ...transformVec, description: 'Each absolute component must be at least 0.00001.' }, pivot: transformVec, rotation: number(-360000, 360000), matrix: tuple(number(-1e15,1e15),9), tiling: { ...enumeration('Utils.cs', 'TransformTilingMode'), default: 'Source', description: 'New layers and Reset store Source. Intrinsic policy, independent of cache textures: geometry/procedural sources Unbounded; Drawing/Text/groups/image effects Clip; File bound texture U/V wrap modes. Explicit modes are preserved.' } }),

};
const seamless = defs.makeSeamless.properties;
Object.assign(defs.noise.properties.scale, { default: [8,8,1], description: 'Scalar sets XY, or XYZ in active ThreeD. [x,y] preserves Z; [x,y,z] sets all axes literally. Inspect returns XYZ, including inactive Z. XY units span the shorter canvas side; Z multiplies Offset Z to select a ThreeD slice and defaults to 1 in older files. Ignored by White/Blue. Seamless fits complete lattice cells per octave/warp, so small Scale changes can quantize.' });
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
  seamlessDescriptions[field] = 'Channels: false restores this input channel after seam processing, before normal layer FX/compositing. All false bypasses seam processing.';
for (const [field, description] of Object.entries(seamlessDescriptions)) seamless[field].description = description;
defs.transform.allOf = [{ if: { required: ['matrix'] }, then: { not: { anyOf: [
  ...['position','scale','rotation'].map(key => ({required:[key]})),
  { required: ['originalAspect'], properties: { originalAspect: { const: true } } }
] } } }];
const experimentalCornerContract = " CornerFit (ID 12, UI Corner Fit AA) and Spring (ID 13, UI Spring Simulation) are separate experimental raster-only scalar modes for SDF and Outline. CornerFit starts with raw Contour geometry, fits sides over multiple pixels and verifies candidate corners by local AA re-rasterization. Spring first applies automatic spring relaxation to the raw contour, inspired by Matusovic, Parakkat and Eisemann, Interactive Depixelization of Pixel Art through Spring Simulation (2023), DOI 10.1111/cgf.14743, then uses the same AA-tested corner proposals. The paper concerns interactive depixelization, not SDF generation; Spring is not the full interactive tool or a C++ replica. Both reject intersecting or unconfirmed extensions and retain the raw contour for ambiguous cases. Only AA-verified corners receive bounded sharp joins with continuous, local endpoint domains. miterLimit (finite 1..64, default 4) bevels excessive validated joins; it does not validate uncertain corners. Threshold is supported through coverage remapping of the selected source channel; soft/noncoverage masks give an approximate fit. SDF uses its threshold; Outline uses fixed byte threshold 128. Both use the existing scalar downstream controls and support Gradient/LinearData, never MultiChannelData. SDF contourMergeDistance is validated and retained but ignored. CornerFit is an experimental hybrid, not a known guaranteed reconstruction algorithm; neither mode promises universal sharp recovery. Round AA remains the default at ID 4 and the previous eight modes are unchanged.";
const contourContract = " Contour (ID 11, UI Contour AA) is an experimental Euclidean distance field to finite straight segments extracted by raw marching squares from alpha or the selected source channel. Threshold crossings are linearly interpolated to subpixel endpoints. It applies no contour fitting, smoothing, simplification, corner recovery, merge/union, miter extension or MSDF channel construction. Expanded corners round. It measures the current raster threshold boundary and does not restore lost detail. It is a separate algorithm, not an AAEDT modification or a sharp-miter mode. It uses the existing source-channel, threshold, SourceEdges, offset and rendering wrapper; SDF exposes threshold and Outline uses fixed byte threshold 128. miterLimit and SDF contourMergeDistance are retained and validated but ignored. SDF supports Gradient/LinearData; MultiChannelData remains MTSDF-only. Quality comparison is experimental, with no guaranteed improvement across all inputs. Round AA remains the default at ID 4; AAEDT remains ID 10 and all existing modes are unchanged.";
const aaedtContract = ' AAEDT (ID 10, UI AAEDT + 8SED) is an experimental anti-aliased Euclidean sweep-and-update transform with 8-neighbor vector propagation, adapted from Stefan Gustavson edtaa3 in a portable Burst job. It estimates the raster boundary; wide external contours remain round. It bypasses segment reconstruction and sharp/miter extensions; miterLimit and SDF contourMergeDistance are retained and validated but ignored. Source alpha (or the selected source channel) is assumed to be area coverage of a crisp edge, sampled with a box filter; blur, feather and arbitrary opacity do not give exact geometry. For coverage a in 0..1 and normalized byte threshold t=threshold/255, 0<t<1 uses c=0.5*a/t when a<=t, else c=0.5+0.5*(a-t)/(1-t). This monotonic piecewise remap sends t to 0.5 and preserves 0/1 endpoints. Thresholds 0 and 255 use binary coverage: 1 only when the selected byte-range source value exceeds threshold, otherwise 0. Outline uses fixed threshold 128. SDF supports Gradient/LinearData; MultiChannelData remains MTSDF-only. The default Round AA is unchanged. No accuracy or performance superiority is claimed.';
const distanceMetric = { ...enumeration('Utils.cs', 'DistanceMetric'), default: 'EuclideanAntialiased',
  description: 'All ten modes are antialiased. EuclideanAntialiased is UI Round AA (ID 4), preserving the original crossing-seed EDT rather than distance to continuous contour segments. Sharp (5) and Bevel (6) are contour-offset fields, not Euclidean SDFs. Diamond (7) is axis-dependent L1; Square (8) is axis-dependent L-infinity. Sharp/Bevel/Diamond/Square/MTSDF use fixed 0.25-pixel contour simplification. Sharp/Bevel/Diamond/Square/MTSDF fit straight flanks to multiple raw isocontour samples by orthogonal least squares before simplification; sampled flank deviation <= 0.6 px and supported acute caps have an engineering allowance <= sqrt(0.5) px and tip displacement within the measured 8-pixel base support, only when independently straight 16-pixel flanks agree with the base fits and sqrt(0.5) px / cosHalfTurn fits in that support; otherwise tip distance <= min(2 px, 0.6 px / cosHalfTurn). Uncertain flanks are preserved. Closed filled contours undergo NonZero union at 1e-6-pixel precision; holes and genuinely separate islands remain, while regions overlapping after reconstruction share one boundary. Paths with perimeter below 4 px bypass simplification; open and zero-area contours bypass region union. Post-union cleanup uses a 0.05-pixel budget and rejects chords crossing nonincident original or replacement boundaries or changing containment/contact states. MTSDF resolves each contour median before combining contours; alpha remains the nearest true distance. Away from the contour, a common-channel bound certifies safe RGB interpolation; suspect cells receive nine finite-contour distance checks. Geometric sign/amplification errors fall back to true distance, color-only incompatibility preserves the scalar median, and alpha is unchanged. Scalar MTSDF uses the corrected RGB median; near-boundary texels are unchanged. Reconstruction can change subpixel detail and is not an exact original matte. MTSDF (9) has corner-preserving RGB perpendicular/pseudo distances and true signed Euclidean contour distance in alpha. Old IDs 0..3 are removed.' };
const miterLimit = { type: 'number', minimum: 1, maximum: 64, default: 4,
description: 'Finite dimensionless miter-length limit relative to the contour offset. Used by Sharp, CornerFit and Spring; excessive joins are beveled. Rbf retains but ignores this value. Same field on SDF and Outline. CornerFit/Spring apply it only to AA-verified corners with bounded local endpoint influence; it does not validate uncertain corners. In Sharp, values above 4 cannot override the separate raster angular-safety cap; unsupported or very acute corners can round.' };
const rasterGuardContract = ' Supported concave fits may minimally shift one wall outward within the existing 0.6-pixel envelope to preserve a nearby hole, then recheck finite-tip/topology bounds. After a component merge, one bounded convex-cap enclosure pass checks arc samples and exact unsampled union vertices, without another 0.25-pixel reduction. Closed components with perimeter below 4 pixels keep finite geometry but do not generate distant Sharp/Bevel miter or MTSDF pseudo extensions. Sharp/MTSDF endpoint extensions require at least 2 pixels of straight support on both flanks after union (successive direction dot >= 0.995), with angular amplification capped at 4. Unsupported or above-cap endpoints use finite segment distance, even inside a large merged contour. Sharp resolves finite ownership within each contour before combining contour fields. Very acute or uncertain corners can round; requested Miter Limit above 4 cannot override raster safety. This is a bounded raster reconstruction, not an exact inverse.';
distanceMetric.description = distanceMetric.description.replace('All ten modes', 'All twelve modes') + rasterGuardContract + aaedtContract + contourContract + experimentalCornerContract +
' Rbf (ID 14, UI RBF Your SDF) is an experimental 2D adaptation of Cheng and Gingold, arXiv:2609.06209v3 with a WhimTex sharp-offset stage. It seeds cubic RBF interpolation with raster-derived Contour distances, refines implied tangent-circle points in fixed overlapping patches, extracts a half-pixel zero contour, fits supported flanks and normalizes closed regions, then applies experimental unrestricted sharp joins. Fractional contour points are passed directly, without re-rasterizing a mask. This is not the authors full implementation or a Euclidean distance field. RBF omits angle and flank-support caps, nearest-boundary ownership and miter beveling; extra rays and overlaps may appear. Numerical degeneracy and contour-validity checks remain. Rounded input or recovered contour segments are not guaranteed to become sharp. Contour Merge and Miter Limit are ignored. Max Distance remaps the retained scalar field; Changing the retained Miter Limit value does not rebuild RBF geometry or distance. Source or threshold changes reconstruct it. No universal corner-recovery or quality advantage is claimed. Refinement is limited to 16,777,216 samples; one-dimensional input uses Round AA. Gradient/LinearData are supported; MultiChannelData remains MTSDF-only.';
distanceMetric.description += ' RbfDual (ID 15, UI RBF Dual Contouring) is a separate experimental raster-only scalar algorithm for SDF and Outline. It shares the Rbf fit, extracts half-pixel cell connectivity using direct continuous RBF root searches, analytic blended normals and a bounded 2D quadratic error function (QEF), then uses the shared sharp-distance stage. It is not a faithful implementation of Occupancy-Based Dual Contouring (2024); no upstream code was copied. Existing Rbf is unchanged. The same source, threshold, sharpMode, miterLimit and scalar output controls apply. Contour Merge is retained but ignored. Max Distance remaps the retained field; the metric participates in the preparation cache key. Gradient/LinearData are supported; MultiChannelData remains MTSDF-only. Sharp-corner recovery and a quality advantage over Rbf are not guaranteed.';
const sharpMode = { ...enumeration('Layers/SDFLayerBehaviour.cs', 'SharpDistanceMode'), default: 'Unrestricted',
  description: 'Corner Mode applies to Sharp, MTSDF, CornerFit, Spring, Rbf and RbfDual; default Unrestricted. Other algorithms retain but ignore it. Current Sharp/MTSDF quality prototype: closed raster contours use line fits over 2/4/8/16-pixel supports, requiring agreement across scales in the two wall-normal directions with bounded longitudinal uncertainty. Supported vertices receive sharp extensions; uncertain joins receive small tangent quadratic caps (at most 0.15px displacement) and finite endpoint distances. This replaces their older fixed 8/16-pixel fitting and 2-pixel endpoint eligibility rules. Contour Merge happens before adaptive fitting. Both Corner Mode policies retain these geometric checks and MTSDF per-contour ownership. GuardedSharp additionally retains scalar finite-edge ownership and MTSDF selective interpolation correction: proximity-aware edge coloring, protected corner texels, analytic channel-crossing candidates verified against the same pseudo-distance field, RGB median and alpha unchanged at integer texels. Unrestricted omits that interpolation pass and scalar finite ownership. Miter Limit bevels scalar joins only in GuardedSharp; MTSDF ignores it. Max Distance is display remapping only. CornerFit, Spring, Rbf and RbfDual are unchanged. Very acute low-resolution corners may still round or differ with threshold; no universal reconstruction guarantee. This supersedes earlier Sharp/MTSDF fitting and correction descriptions.' };
miterLimit.description = 'Finite 1..64, default 4. In GuardedSharp, bevels joins above this ratio for Sharp, CornerFit, Spring, Rbf and RbfDual. Ignored in Unrestricted and MTSDF. No separate fixed angular rounding cap.';
distanceMetric.description += ' Current prototype override: sharpMode selects extension policy; its contract supersedes the earlier fixed-cap/correction description.';
distanceMetric.description += sharpMode.description;
defs.outline = object({
  metric: distanceMetric, sharpMode, miterLimit, sourceChannel: enumeration('Layers/SDFLayerBehaviour.cs', 'SourceChannel'),
  color: rgba, outlineWidth: number(0,16384), outlineSoftness: number(0,16384),
  outlinePosition: enumeration('Layers/OutlineLayerBehaviour.cs', 'OutlinePosition'), outlineOffset: number(-16384,16384),
  fillCenter: { ...bool, default: false }, fillColor: rgba
});
defs.sdf = object({
  metric: distanceMetric, sharpMode, miterLimit, sourceChannel: enumeration('Layers/SDFLayerBehaviour.cs', 'SourceChannel'),
  contourMergeDistance: { ...number(0,2), default: 0,
    description: 'Experimental SDF-only contour merge in current raster/render pixels, independent of distance remap width. Finite 0..2 inclusive; default 0 disables it. Applies only to Sharp and MTSDF; other modes retain but ignore it, with the same validation. One pass merges disjoint consecutive-vertex pairs within one contour, shortest eligible pairs first, to their midpoints. Each pair is within the requested distance; each original vertex is used at most once. Never merges different loops or builds multi-vertex clusters. A topology conflict rolls back the whole batch. Existing safety protections remain. May shift short details and does not guarantee restored sharpness.' },
  threshold: { ...integer(0, 255), description: 'Byte threshold 0..255. In AAEDT, normalize to t=threshold/255 and remap coverage monotonically so t maps to 0.5 while 0/1 remain unchanged. Extreme thresholds use binary coverage: 1 only when the selected byte-range source value exceeds threshold, otherwise 0. CornerFit and Spring also support the threshold through coverage remapping; fitting is approximate for soft masks or values that do not represent area coverage. Remaining modes keep their existing threshold behavior.' },
  distancePosition: enumeration('Layers/SDFLayerBehaviour.cs', 'DistancePosition'),
  inverted: bool, maxDistance: number(0, 16384), sourceOffset: tuple(number(-16384,16384),2),
  sourceEdges: choice('Transparent Clamp Repeat Mirror'), contourOffset: number(-16384,16384),
  insideDistance: number(0,16384), outsideDistance: number(0,16384), profile: str(4096),
  encoding: { ...enumeration('Layers/SDFLayerBehaviour.cs', 'OutputEncoding'), default: 'Gradient',
    description: 'Gradient/LinearData use the RGB median for MTSDF, then Position, normalization, Inverted and Profile; Gradient samples the palette, LinearData has alpha 1. MultiChannelData requires resulting metric MTSDF and writes raw linear RGBA as saturate(0.5 + 0.5*d/maxDistance) (0 selects auto), bypassing Position, Contour Offset, inside/outside ranges, Inverted, Profile and palette. RGB is perpendicular/pseudo distance; alpha is true signed Euclidean distance to the reconstructed subpixel contour, not coverage, binary-bitmap distance or an exact original matte. MTSDF uses constrained multi-sample corner fitting followed by fixed 0.25-pixel simplification. Sampled flank deviation <= 0.6 px; supported acute caps have an engineering allowance <= sqrt(0.5) px and tip displacement within the measured 8-pixel base support, only when independently straight 16-pixel flanks agree with the base fits and sqrt(0.5) px / cosHalfTurn fits in that support; otherwise tip distance <= min(2 px, 0.6 px / cosHalfTurn). Closed filled contours undergo NonZero union before generating edge colors. Holes and genuinely separate islands remain; overlapping reconstructed regions share one boundary. Tiny paths bypass simplification; open and zero-area contours bypass union. MTSDF resolves each contour median before combining contours; alpha remains the nearest true distance. Away from the contour, a common-channel bound certifies safe RGB interpolation; suspect cells receive nine finite-contour distance checks. Geometric sign/amplification errors fall back to true distance, color-only incompatibility preserves the scalar median, and alpha is unchanged. Scalar MTSDF uses the corrected RGB median; near-boundary texels are unchanged. Use blend Overwrite for raw RGBA composition/export because Normal treats alpha as coverage and can alter data. Standalone ordinary FX/effect-layer inputs receive rendered RGBA.' },
  gradient: { $ref: '#/$defs/gradient' }
});
defs.sdf.description = 'Partial live settings patch. Omitted fields retain their values; the resulting layer must use MTSDF for MultiChannelData. This schema can check the pair only when both fields are supplied.';
defs.sdf.properties.encoding.description += rasterGuardContract + sharpMode.description;
defs.sdf.allOf = [{ if: { required: ['encoding'], properties: { encoding: { const: 'MultiChannelData' } } },
  then: { properties: { metric: { const: 'MTSDF' } } } }];
defs.vector = object({
  shapes: { type: 'array', maxItems: 1, items: { $ref: 'document.schema.json#/$defs/VectorShape' } },
  referenceSize: tuple({ type: 'number', exclusiveMinimum: 0, maximum: 10000000 }, 2),
  edgeMode: enumeration('Layers/VectorModel.cs', 'VectorEdgeMode'),
  feather: { ...number(0,8192), default: 0,
    description: 'Finite total soft transition width in vector reference pixels; scales with the vector geometry like stroke width. Smoothstep of signed distance to fill and actual stroke/dash boundaries, not a blur. Zero preserves the existing renderer. Step ignores Feather without clearing either stored setting.' },
  featherPosition: { ...enumeration('Layers/VectorModel.cs', 'VectorFeatherPosition'), default: 'Centered',
    description: 'Inside fades inward, Outside outward, Centered equally across the painted boundary. Layer-wide setting, not a VectorShape or Boolean Group style field. Retained when Feather is zero or Edge Mode is Step.' },
  brushDraft: { anyOf: [{ $ref: 'document.schema.json#/$defs/VectorBrushDraft' }, { type: 'null' }] }
});
defs.vector.description = 'Partial settings.vector patch. One figure per layer; compound contours and holes stay in that figure. Supplied top-level fields replace their values; arrays replace whole arrays. Preserves omitted fields and original brush coverage/frame mapping. Shape/contour/point IDs are unique within the layer; aggregate limits are 8192 contours and 65536 points.';
defs.booleanOperation = { ...enumeration('Layers/BooleanGroupLayerBehaviour.cs', 'VectorBooleanOperation'), default: 'Union',
  description: 'settings.booleanOperation on each Boolean Group operand. Bottom enabled child is the base; higher children apply operations upward. Combine appends separate figures without Union; a subsequent Boolean operation resolves the accumulated region. Outside a Boolean Group this value is retained but inactive.' };
defs.booleanGroup = object({
  style: { type: 'object', allOf: [{ $ref: 'document.schema.json#/$defs/VectorShape' }, { properties: { contours: { type: 'array', maxItems: 0 } } }],
    description: 'Complete replacement of the common style; omit to retain it. Uses all VectorShape fill/stroke fields, but contours must be empty. Geometry comes from children.' },
  referenceSize: tuple({ type: 'number', exclusiveMinimum: 0, maximum: 10000000 }, 2),
  edgeMode: enumeration('Layers/VectorModel.cs', 'VectorEdgeMode'),
  appearance: { ...enumeration('Layers/BooleanGroupLayerBehaviour.cs', 'BooleanGroupAppearance'), default: 'Group', description: 'Combine only: Group applies shared fill/stroke to separate figures; Layers uses original layer appearance. Boolean operations always use Group. Child FX, opacity and blend modes remain ignored.' }
});
defs.booleanGroup.description = 'Partial settings.booleanGroup for type:booleanGroup. Isolated compositing only. Children must be Shape, Vector, editable Text or nested Boolean Groups with closed geometry; raster operands and open contours are rejected. Text uses original source-font outlines and retains editable settings.text; missing or unsupported outlines fail explicitly, without raster tracing. Child styles remain stored and are active only in Combine with appearance:Layers. Child FX remain ignored. Creating through the API uses defaults or the supplied style, without implicit child-style inheritance.';
defs.convertToVector = object({ op: { const: 'convertToVector' }, layer: str(256) }, ['op', 'layer']);
defs.convertToVector.description = 'Convert Shape or Text geometrically on the same layer shell. Both preserve the selected tiling, including Source and explicit Clip. Clip retains its frame; otherwise fitting preserves canvas placement while stored local geometry and transform values may change together. Unsupported Shape feather, stroke transforms/compositing or offset cusps fail before replacement. Text preserves layout as one NonZero compound figure from original unhinted font outlines, including Frame Clip/Ellipsis before frame fitting. Crossing glyphs may be flattened for clipping with 0.01px tolerance. Missing/unsupported source fonts or synthetic bold without a real bold face fail before replacement. Synthetic italic is supported. No raster tracing or variable-axis/color/bitmap-font support is promised.';
defs.blend = enumeration('Utils.cs', 'BlendMode');
defs.pressureBinding = object({
  enabled: bool,
  curve: { ...str(4096), description: 'Pressure response on 0..1. linear, easeIn, easeOut, easeInOut, one, or keys((time,value,inTangent,outTangent,inWeight,outWeight,weightedMode),...). At least two keys; times and values within 0..1. Evaluated output is clamped.' }
});
defs.strokePressures = { type: 'array', minItems: 1, maxItems: 4096, items: number(0,1),
  description: 'Optional one normalized pressure per stroke point; length must match points. Omitted: full pressure. Interpolated at stamp positions; no interactive global curve or smoothing. Not supported by Pencil.' };
defs.smudgeStroke = object({
  op: { const: 'smudgeStroke' }, layer: str(256),
  points: { type: 'array', minItems: 1, maxItems: 4096, items: tuple(number(-1e6, 1e6), 2), description: 'Canvas pixels, top-left origin. One point does not paint.' },
  size: { ...number(1,512), default: 32 }, hardness: { ...number(0,1), default: .8 },
  strength: { ...number(0,1), default: .8 }, flow: { ...number(0,1), default: 1 },
  mixing: { ...number(0,1), default: .25, description: '0: coordinate deformation without cumulative RGB feedback; 1: carried-color mixing. Partial values couple both on every dab.' },
  source: { ...choice('CurrentLayer CurrentAndBelow AllLayers'), default: 'CurrentLayer' }, tiled: { ...bool, default: false },
  writeChannels: { ...integer(0,15), default: 15, description: 'Write mask: R=1, G=2, B=4, A=8. Disabled channels retain their previous straight values. Disabling A also preserves fully transparent pixels.' }
}, ['op', 'layer', 'points']);
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
