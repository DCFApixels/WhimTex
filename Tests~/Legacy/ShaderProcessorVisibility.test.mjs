// Source/control-flow guards; actual compositor pixels are checked by ShaderProcessorSmoke.cs.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = p => readFileSync(new URL('../src/' + p, import.meta.url), 'utf8');
const compositor = read('TextureCompositor.cs');
const standalone = compositor.slice(compositor.indexOf('private RenderTexture RenderStandaloneUncached('), compositor.indexOf('private RenderTexture RenderEffectInput('));
const branch = standalone.slice(standalone.indexOf('if (layer?.Behaviour is ShaderProcessorLayerBehaviour)'), standalone.indexOf('if (layer?.Behaviour is TargetedLayerBehaviour effect)'));
assert.match(branch,/CompositeLayers\(container,[\s\S]*firstIndex: index \+ 1\)/);
assert.match(branch,/if \(!layer.enabled\)\s*\{\s*RenderTexture bypass = input;\s*input = null;\s*return bypass;/);
assert.ok(standalone.indexOf('return bypass;') < standalone.indexOf('layer.Render(context)'));
assert.ok(standalone.indexOf('return bypass;') < standalone.indexOf('FinishStage(raw'));
assert.match(standalone,/finally[\s\S]*if \(input != null && input != accumulatedInput\)[\s\S]*RenderTexture.ReleaseTemporary\(input\)/,
    'Cleanup must release owned inputs, never the caller-owned accumulated input');
assert.match(compositor,/if \(processor.enabled && processor.opacity > 0f/,'Main stack also respects visibility');
assert.match(compositor,/renderStack, includeDisabled: true/,'Ordinary hidden effect sources remain supported');
console.log('Processor visibility: main-stack and effect-input bypass/resource guards passed (source checks).');
