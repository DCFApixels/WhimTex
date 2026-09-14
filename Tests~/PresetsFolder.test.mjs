import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../' + path, import.meta.url), 'utf8');
const settings = read('src/WhimTexUserSettings.cs');
const ui = read('src/WhimTexUserSettingsWindow.cs');
assert.ok(settings.includes('PresetsFolder => EditorPrefs.GetString(PresetsFolderKey, DefaultPresetsFolder)'));
assert.ok(settings.includes('EditorPrefs.SetString(PresetsFolderKey, value)'));
assert.ok(settings.includes('Environment.SpecialFolder.LocalApplicationData'));
assert.ok(settings.includes('"DCFApixels"') && settings.includes('"WhimTex", "Presets"'),
  'Renamed default preset folder');
assert.ok(settings.includes('Directory.Exists(legacy) ? legacy : folder'),
  'Presets saved before the rename stay discoverable until they are moved by hand');
assert.ok(settings.includes('Path.IsPathFullyQualified(value)'));
assert.ok(settings.indexOf('Path.GetFullPath(value)') < settings.indexOf('EditorPrefs.SetString(PresetsFolderKey, value)'));
assert.ok(settings.indexOf('File.Exists(value)') < settings.indexOf('EditorPrefs.SetString(PresetsFolderKey, value)'));
assert.ok(ui.includes('EditorUtility.OpenFolderPanel'));
assert.ok(ui.includes('if (!string.IsNullOrEmpty(selected)) SetPresetsFolder(selected)'));
assert.ok(ui.includes('isDelayed = true'));
assert.ok(ui.includes('new Button(WhimTexUserSettings.ResetPreviewAppearance)'));
const previewReset = settings.split('internal static void ResetPreviewAppearance()')[1];
assert.ok(!previewReset.includes('PresetsFolderKey'), 'Appearance reset preserves library path');
assert.ok(settings.includes('EditorPrefs.DeleteKey(PresetsFolderKey);\n            ResetPreviewAppearance();') ||
          settings.includes('EditorPrefs.DeleteKey(PresetsFolderKey);\r\n            ResetPreviewAppearance();'));
for (const forbidden of ['Directory.CreateDirectory', 'Directory.Delete', 'Directory.Move', 'File.Delete', 'File.Move', 'AssetDatabase.', 'Application.dataPath'])
  assert.ok(!(settings + ui).includes(forbidden), forbidden);
console.log('Preset folder preference/UI/source checks passed (Unity not executed).');

assert.ok(settings.includes('EditorPrefs.GetBool(ShowMantaKey, true)'));
assert.ok(settings.includes('EditorPrefs.SetBool(ShowMantaKey, value)'));
assert.ok(previewReset.includes('EditorPrefs.DeleteKey(ShowMantaKey)'));
assert.ok(previewReset.includes('showManta = true;'));
assert.ok(ui.includes('new Toggle("Clean Preview Background")'));
assert.ok(ui.includes('cleanBackground?.SetValueWithoutNotify(!WhimTexUserSettings.ShowManta)'));
assert.ok(ui.includes('WhimTexUserSettings.ShowManta = !evt.newValue'));
assert.ok(ui.includes('TextureCompositorWindow.ConfirmResetEditorSettings(this)'));
const editor = read('src/TextureCompositorWindow.cs');
assert.ok(!editor.includes('menu.AddItem(new GUIContent("Reset WhimTex Settings…")'));
assert.match(editor, /OnPreviewAppearanceChanged\(\)\s*\{\s*toolkitPreviewCanvas\?\.RefreshBackdropVisibility\(\)/);
const preview = read('src/TextureCompositorWindow.UI.cs');
assert.match(preview, /toolbar.Add\(export\);\s*Button userSettings = WhimTexUI.CreateToolbarButton\(string.Empty, WhimTexUserSettingsWindow.Open, 26f\);/);
assert.ok(preview.includes('userSettings.Add(new LayerActionIcon(LayerActionIcon.Kind.Settings));'));
assert.ok(preview.includes('toolbar.Add(userSettings);'));
assert.match(preview, /Add\(backdrop\);\s*insetShadow = new PreviewInsetShadow\(\);\s*Add\(insetShadow\);\s*RefreshBackdropVisibility\(\);/);
assert.ok(preview.includes('backdrop.EnableInClassList("whimtex-preview-backdrop--hidden", !WhimTexUserSettings.ShowManta)'));
