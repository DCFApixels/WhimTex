// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('CanvasTerminology: source/scalar contracts');
const assert = context.assert;

context.case('CanvasTerminology original assertions and branches', async () => {
    const read = path => readFileSync(new URL('../../../' + path, import.meta.url), 'utf8');
    const aliases = [
      ['Channels', 'previewChannels', 'canvasChannels'],
      ['Channels', 'previewDebug', 'canvasDebug'],
      ['GuideCommands', 'previewGuidesHidden', 'canvasGuidesHidden'],
      ['GuideCommands', 'previewGuidesLocked', 'canvasGuidesLocked'],
      ['GuideCommands', 'previewGuidesSnap', 'canvasGuidesSnap'],
      ['Guides', 'previewGuides', 'canvasGuides'],
      ['Tiling', 'tiledPreview', 'tiledCanvas'],
      ['Inspector', 'inspectorPreviewState', 'layerPreviewState'],
    ];
    for (const [part, oldName, newName] of aliases) {
      const source = read(`src/WhimTexWindow.${part}.cs`);
      assert.doesNotMatch(source, /FormerlySerializedAs/);
      assert.match(source, new RegExp(`\\[SerializeField\\] private [^;\\n]+\\b${newName}\\b`), newName);
    }
    for (const [part, oldName, newName] of [
      ['Guides', 'PreviewGuide', 'CanvasGuide'],
      ['GuideCommands', 'PreviewGuideSettingsWindow', 'CanvasGuideSettingsWindow'],
    ]) {
      const source = read(`src/WhimTexWindow.${part}.cs`);
      assert.doesNotMatch(source, /MovedFrom/);
      assert.match(source, new RegExp(`(?:class|struct) ${newName}\\b`));
    }
    const settings = read('src/WhimTexUserSettings.cs');
    for (const suffix of ['CheckerLight', 'CheckerDark', 'InvalidPixels', 'CheckerSize', 'ShowManta', 'PostFxBackground', 'PostFxBackgroundMode'])
      assert.ok(settings.includes(`"DCFApixels.WhimTex.CanvasView.${suffix}"`), suffix);
    assert.ok(read('src/WhimTexWindow.cs').includes('"DCFApixels.WhimTex.Canvas.PaintingScale"'));
    const tools = read('src/WhimTexWindow.Tools.cs');
    for (const key of ['Canvas.Tool', 'Canvas.TransformReturnTool'])
      assert.ok(tools.includes(`"DCFApixels.WhimTex.${key}"`));
    
    const color = read('src/Editor/WhimTexColorField.cs');
    assert.match(color, /public bool UseCanvasChannels \{ get; set; \}/);
    assert.doesNotMatch(color, /\bUsePreviewChannels\b/);
    const utils = read('src/Utils.cs');
    assert.ok(utils.includes('protected virtual string LayerPreviewTitle => "Layer Preview";'));
    assert.ok(utils.includes('protected virtual bool ImmediateLayerPreviewUpdates => false;'));
    assert.doesNotMatch(utils, /protected (?:virtual string PreviewTitle|virtual bool ImmediatePreviewUpdates|void RequestPreview)\b/);
    assert.ok(utils.includes('{ tooltip = LayerPreviewTitle }'));
    assert.ok(utils.includes('immediate || ImmediateLayerPreviewUpdates'));
    
    const css = read('src/WhimTexSplitView.uss');
    assert.doesNotMatch(css, /\.whimtex-(?:preview-|mini-preview-)/);
    for (const name of ['canvas-view', 'canvas', 'canvas-surface', 'layer-preview-channels', 'layer-preview-channel'])
      assert.ok(css.includes(`.whimtex-${name}`), name);
    assert.ok(read('src/Editor/LayerPreviewPanel.cs').includes('new Label("Layer Preview")'));
    assert.ok(read('src/CanvasViewport.cs.meta').includes('guid: 0a580ed6d33343cda32b9db8500ed359'));
    assert.ok(read('src/WhimTexDocument.LayerPreview.cs.meta').includes('guid: b1a6a95b73624f82923b9ef3c1eb82c5'));
    for (const [file, guid] of [
      ['src/WhimTexCanvasViewBackdrop.png', 'a693463e898e46f089f96a27a27c7451'],
      ['src/Editor/ChannelDragManipulator.cs', '7dc74ce0e40e423391e045e73c172f56'],
      ['src/Shaders/DisplayChannels.shader', '4dc8a962e0d34186818056fabc97e88a'],
    ]) assert.ok(read(file + '.meta').includes('guid: ' + guid), file);
    assert.match(read('src/Editor/ChannelDragManipulator.cs'), /internal sealed class ChannelDragManipulator\b/);
    assert.ok(utils.includes('public static Material DisplayChannels'));
    assert.ok(read('src/Shaders/DisplayChannels.shader').includes('Shader "Hidden/WhimTex/DisplayChannels"'));
    for (const file of ['src/WhimTexWindow.Channels.cs', 'src/Editor/LayerPreviewPanel.cs'])
      assert.ok(read(file).includes('new ChannelDragManipulator('), file);
});

await finish(context);

