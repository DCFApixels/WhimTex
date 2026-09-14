import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = name => readFileSync(new URL(`../src/${name}`, import.meta.url), 'utf8');
const settings = read('WhimTexUserSettings.cs');
const ui = read('WhimTexUserSettingsWindow.cs');
const guides = read('TextureCompositorWindow.Guides.cs');
const snap = read('TextureCompositorWindow.GuideSnapping.cs');
const transform = read('TextureCompositorWindow.Transform.cs');
assert.match(settings, /DefaultSnapRadius = 8f/);
assert.match(settings, /MinimumSnapRadius = 1f/);
assert.match(settings, /MaximumSnapRadius = 64f/);
assert.match(settings, /float.IsNaN\(value\) \|\| float.IsInfinity\(value\)\s*\? DefaultSnapRadius : Mathf.Clamp\(value, MinimumSnapRadius, MaximumSnapRadius\)/);
assert.match(settings, /EditorPrefs.GetFloat\(SnapRadiusKey, DefaultSnapRadius\)/);
assert.match(settings, /EditorPrefs.SetFloat\(SnapRadiusKey, value\)/);
assert.match(settings, /internal static void Reset\(\)[\s\S]*?ResetGuidesAndSnapping\(\)/);
assert.match(ui, /"Snap Radius \(px\)"/);
assert.match(ui, /snapRadius\?\.SetValueWithoutNotify\(WhimTexUserSettings.SnapRadius\)/);
assert.match(snap, /GuideSnapPixels => WhimTexUserSettings.SnapRadius/);
for (const name of ['PivotSnapDistance', 'CanvasSnapDistance'])
    assert.match(transform, new RegExp(`${name} => WhimTexUserSettings.SnapRadius`));
for (const kind of ['Aligned', 'Angled', 'Active']) {
    assert.match(settings, new RegExp(`Save\\(Guide${kind}Key, ref guide${kind}Color, value\\)`));
    assert.match(settings, new RegExp(`EditorPrefs.DeleteKey\\(Guide${kind}Key\\)`));
    assert.ok(ui.includes(`WhimTexUserSettings.Guide${kind}Color`));
    assert.ok(guides.includes(`WhimTexUserSettings.Guide${kind}Color`));
}
assert.match(guides, /if \(deleting\) lineColor = new Color\(1f, .35f, .25f, .9f\)/);
assert.match(read('TextureCompositorWindow.cs'), /OnPreviewAppearanceChanged\(\)[\s\S]*?previewGuideOverlay\?\.MarkDirtyRepaint\(\)/);
console.log('Guide settings persistence, reset, UI and shared snap-radius source contracts passed.');
