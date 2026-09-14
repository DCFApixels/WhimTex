// Source/compatibility checks only: does not compile or invoke Unity.
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const read = p => readFileSync(path.join(root, p), 'utf8');
const pkg = JSON.parse(read('package.json'));
assert.equal(pkg.displayName, 'WhimTex');
assert.equal(pkg.name, 'com.dcfapixels.whimtex');
assert.equal(pkg.documentationUrl, 'https://dcfapixels.github.io/WhimTex/');
assert.equal(pkg.repository.url, 'https://github.com/DCFApixels/WhimTex.git');
assert.match(read('Documentation~/_config.yml'), /^baseurl: \/WhimTex$/m);
assert.match(read('Documentation~/_config.yml'), /^repository: DCFApixels\/WhimTex$/m);
assert.equal(JSON.parse(read('src/DCFApixels.WhimTex.asmdef')).name, 'DCFApixels.WhimTex');
const window = read('src/TextureCompositorWindow.cs');
assert.ok(window.includes('[MenuItem("Window/WhimTex")]'));
assert.match(window, /void OnEnable\(\)\s*\{\s*RefreshDocumentTitle\(true\)/,
  'Restored windows update their persisted title without resetting their document');
assert.match(read('src/TextureCompositorWindow.DocumentTitle.cs'), /WhimTexBranding.WindowTitle\(title\)/);
const commands = read('src/Automation/Pipeline/WhimTexCommands.cs');
for (const id of ['begin', 'sessions', 'live', 'lock', 'describe', 'execute', 'render', 'inspect', 'import_image'])
  assert.ok(commands.includes(`"whimtex_${id}"`), `Stable CLI command: ${id}`);
for (const [file, key] of [
  ['src/WhimTexColorInputs.cs', 'DCFApixels.WhimTex.HdrColorInputs'],
  ['src/WhimTexUserSettings.cs', 'DCFApixels.WhimTex.PresetsFolder'],
  ['src/TextureCompositorWindow.Tools.cs', 'DCFApixels.WhimTex.PaintToolSettings']
]) assert.ok(read(file).includes(`"${key}"`), `Retain persisted preference: ${key}`);
const LEGACY_FOLDER_FALLBACK = /^\s*private const string LegacyDataFolder = "SpriteEditor";$/m;
function scan(dir) {
  for (const entry of readdirSync(path.join(root, dir), { withFileTypes: true })) {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) scan(file);
    else if (file.endsWith('.cs')) {
      // `MovedFrom` markers are required compatibility data, and the preset-folder fallback in
      // WhimTexUserSettings.cs is the only other line allowed to spell the old name.
      let source = read(file).replace(/^\s*\[MovedFrom\(.*$/gm, '');
      if (LEGACY_FOLDER_FALLBACK.test(source)) source = source.replace(LEGACY_FOLDER_FALLBACK, '');
      assert.ok(!/SpriteEditor/.test(source), `Old type or identifier name: ${file}`);
      assert.ok(!/Sprite Editor/.test(source), `Old display name: ${file}`);
    }
  }
}
scan('src');
assert.ok(read('src/PsdWriter.cs').includes('w.Unicode("WhimTex"); w.Unicode("WhimTex");'));
assert.ok(read('Skills~/whimtex-live/SKILL.md').includes('name: whimtex-live'));
const logo = read('Documentation~/Images/whimtex-logo.svg');
assert.match(logo, /<svg\b/);
assert.match(logo, /<linearGradient\b/);
assert.ok(!/<image\b|data:image|<script\b/.test(logo), 'Logo remains editable vector artwork, not an embedded bitmap');
const duotone = read('Documentation~/Images/whimtex-logo-duotone.svg');
assert.deepEqual([...new Set(duotone.match(/#[0-9a-f]{6}\b/g))].sort(), ['#202124', '#ffffff']);
assert.ok(!/<linearGradient\b|<image\b|<script\b/.test(duotone), 'Two-tone artwork stays flat; only its background shadow uses a radial gradient');
assert.equal((duotone.match(/<radialGradient\b/g) || []).length, 1);
assert.match(duotone, /<ellipse id="background-shadow"[^>]*fill="url\(#center-shadow\)"/);
assert.ok(!/<path\b[^>]*fill="#202124"/.test(duotone), 'Dark color is reserved for contours and eyes, never filled body segments');
const logoPaths = svg => new Map([...svg.matchAll(/<path id="([^"]+)"[^>]* d="([^"]+)"/g)].map(m => [m[1], m[2]]));
for (const [id, geometry] of logoPaths(logo))
  assert.equal(logoPaths(duotone).get(id), geometry, `Two-tone variant preserves approved ${id} geometry`);
for (const file of ['README.md', 'README-RU.md'])
  assert.ok(read(file).includes('src="Documentation~/Images/whimtex-logo.svg"'));
const iconGuid = read('src/WhimTexIcon.png.meta').match(/^guid: (\w+)$/m)[1];
assert.ok(read('src/WhimTexBranding.cs').includes(`GUIDToAssetPath("${iconGuid}")`));
for (const file of ['src/TextureCompositorWindow.DocumentTitle.cs', 'src/WhimTexUserSettingsWindow.cs',
  'src/ModifierEditorWindow.cs', 'src/Utils.cs'])
  assert.ok(read(file).includes('WhimTexBranding.WindowTitle('), `${file}: branded title`);
for (const [file, size] of [
  ['src/WhimTexIcon.png', 64], ['src/WhimTexPreviewBackdrop.png', 1024], ['Documentation~/Images/favicon-32.png', 32],
  ['Documentation~/Images/apple-touch-icon.png', 180], ['Documentation~/Images/whimtex-logo.png', 512]
]) {
  const png = readFileSync(path.join(root, file));
  assert.equal(png.subarray(1, 4).toString(), 'PNG');
  assert.equal(png.readUInt32BE(16), size);
  assert.equal(png.readUInt32BE(20), size);
  assert.equal(png[25], 6, `${file}: preserve RGBA transparency`);
}
assert.ok(read('Documentation~/_includes/title.html').includes('site.logo | relative_url'));
assert.ok(read('Documentation~/_includes/favicon.html').includes('site.logo | relative_url'));
const backdropGuid = read('src/WhimTexPreviewBackdrop.png.meta').match(/^guid: (\w+)$/m)[1];
assert.ok(read('src/WhimTexBranding.cs').includes(`GUIDToAssetPath("${backdropGuid}")`));
const previewUI = read('src/TextureCompositorWindow.UI.cs').split('private sealed class SpritePreviewElement')[1];
assert.ok(previewUI.indexOf('Add(backdrop);') < previewUI.indexOf('Add(checker);'), 'Backdrop stays behind the canvas/checker, not in document pixels');
assert.match(previewUI, /backdrop = new Image\s*\{[^}]*pickingMode = PickingMode.Ignore,[^}]*focusable = false/s);
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-preview-backdrop\s*\{\s*position: absolute;\s*opacity: 0\.035;/);
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-preview-canvas\s*\{[^}]*overflow: hidden;/);
const backdropLayout = previewUI.split('private void UpdateBackdropLayout()')[1].split('public void SetToolCursor')[0];
assert.ok(!/viewport\.|ImageRect|documentWidth|documentHeight/.test(backdropLayout), 'Background placement is independent of document transforms and zoom');
console.log('WhimTex branding and legacy package/API/preference identity checks passed (Unity not executed).');

const presentation = read('src/TextureCompositorWindow.UI.cs').split('private void UpdateToolkitPreviewPresentation()')[1].split('private void OnPreviewPointerEnter')[0];
const toolSource = read('src/TextureCompositorWindow.Tools.cs');
assert.match(toolSource, /foreach \(Layer layer in compositor.layers\)\s*if \(layer != null\) return true;/);
assert.match(presentation, /bool hasLayers = HasPreviewLayers;/);
const toolbar = toolSource.split('private void RefreshPreviewToolToolbar()')[1].split('private sealed class PreviewToolIcon')[0];
assert.match(toolbar, /PreviewTool displayedTool = previewTool;/);
assert.match(toolbar, /Layer selected = hasLayers \? GetSelectedLayer\(\) : null;/);
assert.ok(!/previewTool\s*=(?!=)|SetPreviewTool\(|SetEnabled\(/.test(toolbar), 'Empty styling preserves tool choice and configuration');
assert.equal((toolbar.match(/--selected", displayedTool == PreviewTool\./g) || []).length, 9);
for (const button of ['previewRectangleSelectButton', 'previewPolygonSelectButton', 'previewZoomButton'])
  assert.ok(toolbar.includes(`${button}?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers)`));
assert.match(toolSource, /HandlePaintConversionPrompt\(PointerDownEvent evt\)\s*\{\s*if \(!HasPreviewLayers\)\s*\{\s*WhimTexUI.ConsumeEvent\(evt\);\s*return true;/);
const zoom = read('src/TextureCompositorWindow.Zoom.cs');
assert.match(zoom, /ChangePreviewZoom\(bool fit\)\s*\{\s*if \(!HasPreviewLayers\) return;/);
assert.match(zoom, /if \(!owner.HasPreviewLayers \|\| \(evt.button != 2/);
assert.match(zoom, /if \(!owner.HasPreviewLayers \|\| !target.contentRect.Contains\(point\)/);
assert.match(read('src/TextureCompositorWindow.AreaSelectionView.cs'), /!owner.IsAreaSelectionTool \|\| !owner.HasPreviewLayers/);
assert.match(read('src/TextureCompositorWindow.Eyedropper.cs'), /CanUsePreviewEyedropper => HasPreviewLayers &&/);
assert.ok(!/layer\.visible/.test(presentation), 'Hidden layers still count as document content');
assert.match(presentation, /SetCanvasVisible\(hasLayers\)/);
for (const element of ['checker', 'image', 'tiledImage'])
  assert.ok(previewUI.includes(`${element}.AddToClassList("whimtex-preview-surface")`));
assert.ok(!previewUI.includes('backdrop.AddToClassList("whimtex-preview-surface")'));
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-preview-canvas--empty > \.whimtex-preview-surface\s*\{\s*display: none;/);
assert.match(previewUI, /if \(canvasVisible == visible\) return;[\s\S]*?if \(visible\) viewport.Reset\(\);/);
for (const method of ['ZoomAt', 'Frame', 'Pan'])
  assert.match(previewUI, new RegExp(`public void ${method}\\([^)]*\\)\\s*\\{\\s*if \\(!canvasVisible\\) return;`));
