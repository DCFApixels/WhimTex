import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = file => readFileSync(path.join(root, file), 'utf8');
function scan(directory) {
  for (const item of readdirSync(path.join(root, directory), { withFileTypes: true })) {
    const file = path.join(directory, item.name);
    if (item.isDirectory()) scan(file);
    else if (file.endsWith('.cs')) {
      assert.doesNotMatch(read(file), /DCFApixels\.WhimTex\.(?:Preview\.|PreviewTool|PreviewTransformReturnTool|PaintingPreviewScale)/, file);
      assert.doesNotMatch(read(file), /LegacyDataFolder/, file);
    }
  }
}
scan('src');
const settings = read('src/WhimTexUserSettings.cs');
for (const suffix of ['CheckerLight', 'CheckerDark', 'InvalidPixels', 'CheckerSize', 'ShowManta', 'PostFxBackground', 'PostFxBackgroundMode'])
  assert(settings.includes('"DCFApixels.WhimTex.CanvasView.' + suffix + '"'), suffix);
const tools = read('src/TextureCompositorWindow.Tools.cs');
for (const suffix of ['Tool', 'TransformReturnTool', 'PaintToolSettings'])
  assert(tools.includes('"DCFApixels.WhimTex.Canvas.' + suffix + '"'), suffix);
assert(read('src/TextureCompositorWindow.cs').includes('"DCFApixels.WhimTex.Canvas.PaintingScale"'));
assert.doesNotMatch(tools, /blurOpacity/);
assert.match(tools, /JsonUtility\.FromJsonOverwrite\(EditorPrefs\.GetString\(PaintToolSettingsPrefKey\), paintSettings\)/);
const folder = settings.split('internal static string DefaultPresetsFolder')[1].split('internal static bool TrySetPresetsFolder')[0];
assert.match(folder, /Environment\.SpecialFolder\.LocalApplicationData/);
assert.doesNotMatch(folder, /Directory\.|File\.|AssetDatabase\./);
for (const forbidden of ['Directory.Move', 'Directory.Delete', 'File.Move', 'File.Delete'])
  assert(!settings.includes(forbidden), forbidden);
console.log('PASS: canonical Canvas/CanvasView preferences, no old-folder search or blur-settings migration, no library file mutation.');
