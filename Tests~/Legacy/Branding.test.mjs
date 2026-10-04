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
assert.match(window, /void OnEnable\(\)\s*\{\s*RestoreSourceImage\(\);\s*RefreshDocumentTitle\(true\)/,
  'Restored windows recover their source image and persisted title without resetting their document');
assert.match(read('src/TextureCompositorWindow.DocumentTitle.cs'), /WhimTexBranding.WindowTitle\(title\)/);
const commands = read('src/Automation/Pipeline/WhimTexCommands.cs');
const canonicalCommands = [
  'whimtex_assistant_begin', 'whimtex_assistant_lock', 'whimtex_assistant_sessions', 'whimtex_assistant_live',
  'whimtex_describe', 'whimtex_document_inspect', 'whimtex_batch_execute', 'whimtex_image_import',
  'whimtex_document_render', 'whimtex_storage_inspect',
  'whimtex_document_validate',
  'whimtex_fx_compile', 'whimtex_document_status', 'whimtex_document_compare',
  'whimtex_document_recover', 'whimtex_document_export', 'whimtex_headless_live'
];
for (const id of canonicalCommands)
  assert.ok(commands.includes(`CliCommand("${id}"`), `Canonical CLI command: ${id}`);
for (const id of [
  'whimtex_begin', 'whimtex_sessions', 'whimtex_live', 'whimtex_lock', 'whimtex_execute',
  'whimtex_render', 'whimtex_inspect', 'whimtex_import_image', 'whimtex_migrate',
  'whimtex_inspect_storage', 'whimtex_validate', 'whimtex_status', 'whimtex_compare',
  'whimtex_recover', 'whimtex_export', 'whimtex_tiff_live'
]) assert.ok(!commands.includes(`CliCommand("${id}"`), `Legacy CLI command removed: ${id}`);
for (const [file, key] of [
  ['src/WhimTexColorInputs.cs', 'DCFApixels.WhimTex.HdrColorInputs'],
  ['src/WhimTexUserSettings.cs', 'DCFApixels.WhimTex.PresetsFolder'],
  ['src/TextureCompositorWindow.Tools.cs', 'DCFApixels.WhimTex.Canvas.PaintToolSettings']
]) assert.ok(read(file).includes(`"${key}"`), `Current preference: ${key}`);
function scan(dir) {
  for (const entry of readdirSync(path.join(root, dir), { withFileTypes: true })) {
    const file = path.join(dir, entry.name);
    if (entry.isDirectory()) scan(file);
    else if (file.endsWith('.cs')) {
      const source = read(file);
      {
        assert.ok(!/SpriteEditor/.test(source), `Old type or identifier name: ${file}`);
        assert.ok(!/Sprite Editor/.test(source), `Old display name: ${file}`);
      }
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
  ['src/WhimTexIcon.png', 64], ['src/WhimTexCanvasViewBackdrop.png', 1024], ['Documentation~/Images/favicon-32.png', 32],
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
const backdropGuid = read('src/WhimTexCanvasViewBackdrop.png.meta').match(/^guid: (\w+)$/m)[1];
assert.ok(read('src/WhimTexBranding.cs').includes(`GUIDToAssetPath("${backdropGuid}")`));
const previewUI = read('src/TextureCompositorWindow.UI.cs').split('private sealed class CanvasElement')[1];
assert.ok(previewUI.indexOf('Add(backdrop);') < previewUI.indexOf('Add(checker);'), 'Backdrop stays behind the canvas/checker, not in document pixels');
assert.match(previewUI, /backdrop = new Image\s*\{[^}]*pickingMode = PickingMode.Ignore,[^}]*focusable = false/s);
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-canvas-view-backdrop\s*\{\s*position: absolute;\s*opacity: 0\.035;/);
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-canvas\s*\{[^}]*overflow: hidden;/);
const backdropLayout = previewUI.split('private void UpdateBackdropLayout()')[1].split('public void SetToolCursor')[0];
assert.ok(!/viewport\.|ImageRect|documentWidth|documentHeight/.test(backdropLayout), 'Background placement is independent of document transforms and zoom');
console.log('WhimTex branding and package/API/current preference identity checks passed (Unity not executed).');

const presentation = read('src/TextureCompositorWindow.UI.cs').split('private void UpdateToolkitCanvasPresentation()')[1].split('private void OnCanvasPointerEnter')[0];
const toolSource = read('src/TextureCompositorWindow.Tools.cs');
assert.match(toolSource, /foreach \(Layer layer in compositor.layers\)\s*if \(layer != null\) return true;/);
assert.match(presentation, /bool hasLayers = HasCanvasLayers;/);
const toolbar = toolSource.split('private void RefreshCanvasToolToolbar()')[1].split('private sealed class CanvasToolIcon')[0];
assert.match(toolbar, /CanvasTool displayedTool = canvasTool;/);
assert.match(toolbar, /Layer selected = hasLayers \? GetSelectedLayer\(\) : null;/);
assert.ok(!/canvasTool\s*=(?!=)|SetCanvasTool\(|SetEnabled\(/.test(toolbar), 'Empty styling preserves tool choice and configuration');
assert.equal((toolbar.match(/--selected", displayedTool == CanvasTool\./g) || []).length, 11);
for (const button of ['canvasRectangleSelectButton', 'canvasPolygonSelectButton', 'canvasZoomButton'])
  assert.ok(toolbar.includes(`${button}?.EnableInClassList("whimtex-tool-button--unavailable", !hasLayers)`));
assert.match(toolSource, /HandlePaintConversionPrompt\(PointerDownEvent evt\)\s*\{\s*if \(!HasCanvasLayers\)\s*\{\s*WhimTexUI.ConsumeEvent\(evt\);\s*return true;/);
const zoom = read('src/TextureCompositorWindow.Zoom.cs');
assert.match(zoom, /ChangeCanvasZoom\(bool fit\)\s*\{\s*if \(!HasCanvasLayers\) return;/);
assert.match(zoom, /if \(!owner.HasCanvasLayers \|\| \(evt.button != 2/);
assert.match(zoom, /if \(!owner.HasCanvasLayers \|\| !target.contentRect.Contains\(point\)/);
assert.match(read('src/TextureCompositorWindow.AreaSelectionView.cs'), /!owner.IsAreaSelectionTool \|\| !owner.HasCanvasLayers/);
assert.match(read('src/TextureCompositorWindow.Eyedropper.cs'), /CanUseCanvasEyedropper => HasCanvasLayers &&/);
assert.ok(!/layer\.visible/.test(presentation), 'Hidden layers still count as document content');
assert.match(presentation, /SetCanvasVisible\(hasLayers\)/);
for (const element of ['checker', 'image', 'tiledImage'])
  assert.ok(previewUI.includes(`${element}.AddToClassList("whimtex-canvas-surface")`));
assert.ok(!previewUI.includes('backdrop.AddToClassList("whimtex-canvas-surface")'));
assert.match(read('src/WhimTexSplitView.uss'), /\.whimtex-canvas--empty > \.whimtex-canvas-surface\s*\{\s*display: none;/);
assert.match(previewUI, /if \(canvasVisible == visible\) return;[\s\S]*?if \(visible\) viewport.Reset\(\);/);
for (const method of ['ZoomAt', 'Frame', 'Pan'])
  assert.match(previewUI, new RegExp(`public void ${method}\\([^)]*\\)\\s*\\{\\s*if \\(!canvasVisible\\) return;`));
