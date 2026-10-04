import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

const read = path => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');

test('FX stack copies blocks and pastes new Shader FX instances after the selected row', () => {
  const view = read('src/Editor/LayerShaderFXView.cs');
  const clipboard = read('src/Editor/ShaderFXClipboard.cs');
  const shaderFx = read('src/ShaderFX.cs');
  const undo = read('src/TextureCompositor.Api.cs');

  assert.match(view, /GUIContent\("Copy FX"\)/);
  assert.match(view, /GUIContent\("Paste FX As New"\)/);
  assert.match(view, /toolbar\.AddManipulator\(new ContextualMenuManipulator\(evt =>/);
  assert.match(view, /menu\.AppendAction\("Copy FX"/);
  assert.match(view, /menu\.AppendAction\("Paste FX As New"/);
  assert.match(view, /actions\.AddToClassList\("whimtex-layer-menu-button"\)/);
  assert.match(view, /dot\.AddToClassList\("whimtex-layer-menu-dot"\)/);
  assert.doesNotMatch(view, /text = "⋮"/);
  assert.doesNotMatch(view, /text = "Paste FX"/);
  assert.doesNotMatch(view, /ShaderFXClipboard\.Changed \+= RefreshClipboardActionState/);
  assert.match(view, /ShaderFXClipboard\.Copy\(owner, modifier\)/);
  assert.match(view, /layer\.modifiers\.Insert\(destinationIndex, pasted\)/);
  assert.match(view, /owner\.AdoptAgentShaderFX\(effect, "Paste FX"\)/);
  assert.match(view, /Undo\.SetCurrentGroupName\("Paste FX"\)/);
  assert.match(view, /Undo\.CollapseUndoOperations\(undoGroup\)/);
  assert.match(view, /Undo\.FlushUndoRecordObjects\(\)/);

  assert.match(clipboard, /effect\.CloneForClipboard\(\)/);
  assert.match(clipboard, /source\.CloneForDocument\(destination\)/);
  assert.match(clipboard, /copy\.RemapTextureLayers\(new Dictionary<string, string>\(\), clearExternal: true\)/);
  assert.match(clipboard, /GUIUtility\.systemCopyBuffer != marker/);
  assert.match(clipboard, /ImageClipboard\.Revision/);
  assert.match(clipboard, /AssemblyReloadEvents\.beforeAssemblyReload \+= Clear/);
  assert.match(clipboard, /EditorApplication\.quitting \+= Clear/);

  assert.match(shaderFx, /internal ShaderFX CloneForClipboard\(\)/);
  assert.match(shaderFx, /copy\.documentIncludeBasePath = includeBasePath/);
  assert.match(shaderFx, /copy\.DetachCatalog\(\)/);

  assert.match(undo, /Undo\.undoRedoPerformed \+= RefreshModelsAfterUndo/);
  assert.match(undo, /Changed\?\.Invoke\(document\)/);
});

test('FX section removes only its horizontal inspector padding', () => {
  const ui = read('src/WhimTexUI.cs');
  const styles = read('src/WhimTexSplitView.uss');

  assert.match(ui, /fx\.contentContainer\.AddToClassList\("whimtex-fx-section-content"\)/);
  assert.match(styles, /\.whimtex-fx-section-content\s*\{\s*padding-left:\s*0;\s*padding-right:\s*0;\s*\}/);
});

test('FX blocks can be dropped onto another layer with lock checks and drop feedback', () => {
  const view = read('src/Editor/LayerShaderFXView.cs');
  const styles = read('src/WhimTexSplitView.uss');

  assert.match(view, /Drag the header to reorder this FX or move it to another layer/);
  assert.match(view, /toolbar\.AddManipulator\(new ReorderManipulator\(/);
  assert.match(view, /RegisterCallback<PointerDownEvent>\(OnPointerDown, TrickleDown\.TrickleDown\)/);
  assert.match(view, /target\.CapturePointer\(pointerId\)/);
  assert.match(view, /if \(shouldMove\)[\s\S]*?evt\.StopPropagation\(\);/);
  assert.match(view, /panel\?\.Pick\(panelPosition\)/);
  assert.match(view, /ClassListContains\("whimtex-layer-row"\)/);
  assert.match(view, /!WhimTexApi\.IsLayerContentLocked\(owner, layer\)[\s\S]*!WhimTexApi\.IsLayerContentLocked\(owner, destination\)/);
  assert.match(view, /layer\.modifiers\.RemoveAt\(sourceIndex\);\s*destination\.modifiers \?\?= new List<UnityEngine\.Object>\(\);\s*destination\.modifiers\.Add\(modifier\);/);
  assert.match(view, /moveToLayer\(sourceIndex, destinationLayerAtRelease\)/);
  assert.match(styles, /\.whimtex-layer-row--drop-fx\s*>\s*\.whimtex-layer-drop-marker/);
});
