import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = name => readFileSync(new URL(`../src/${name}`, import.meta.url), 'utf8');
const asset = read('TextureCompositor.Assets.cs');
const legacy = read('Editor/Legacy/TextureCompositor.LegacyAssetWriter.cs');
const window = read('TextureCompositorWindow.cs');
const live = read('TextureCompositorWindow.LiveOutput.cs');
const change = read('CompositorOutputChange.cs');
assert.match(asset, /liveOutput.Publish\(source\);\s*NotifyOutputTextureChanged\(\)/);
assert.match(asset, /previous\?\.Dispose\(\);\s*if \(previous != null\) NotifyOutputTextureChanged\(\)/);
// Legacy .asset writing is intentionally isolated from the TIFF/runtime path.
// Keep this assertion so the migration fixture remains available, while ensuring
// the normal asset code cannot accidentally regain the retired writer flow.
assert.match(legacy, /AssetDatabase.ImportAsset\(path,[\s\S]*?NotifyOutputTextureChanged\(\);\s*Changed\?\.Invoke\(this\)/);
assert.match(legacy, /SaveLegacyAssetForCompatibility/);
assert.doesNotMatch(asset, /SaveLegacyAssetForCompatibility|AssetDatabase\.CreateAsset\(/);
assert.match(asset, /source == null \|\| outputTexture == null/);
for (const op of ['+=', '-=']) assert.ok(window.includes(`TextureCompositor.OutputTextureChanged ${op} OnOutputTextureChanged`));
assert.match(live, /!change.ShouldRefresh\(compositor\)/);
assert.match(live, /outputDependencyDirty = true;\s*RequestPreview\(\)/);
assert.match(window, /UpdatePreview\(\)\s*\{\s*if \(outputDependencyDirty\)\s*\{\s*outputDependencyDirty = false;\s*ReleaseEffectCache\(\)/);
assert.match(change, /consumer == source/);
assert.match(change, /layer\?\.IsGroup == true && UsesTexture\(layer.layers, texture\)/);
assert.match(change, /visited.Add\(dependency\)/);
assert.match(change, /return !DependsOnTexture\(source.layers, consumer.OutputTexture/);
assert.ok(!/AssetDatabase|EditorPrefs|MarkChanged|SetDirty|Undo\./.test(change));
const handler = live.split('private void OnOutputTextureChanged')[1].split('private bool CanPublishLiveOutput')[0];
assert.ok(!/MarkChanged|SetDirty|Undo\.|UpdatePreview\(|ReleaseEffectCache\(/.test(handler));
console.log('Output notifications, lazy cache invalidation, File/group matching and cycle guard source contracts passed.');
