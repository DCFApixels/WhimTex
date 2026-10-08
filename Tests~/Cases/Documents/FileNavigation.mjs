// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('FileNavigation: source/scalar contracts');
const assert = context.assert;

context.case('FileNavigation original assertions and branches', async () => {
    const read = name => readFileSync(new URL(`../../../src/${name}`, import.meta.url), 'utf8');
    const source = read('WhimTexWindow.FileNavigation.cs');
    assert.match(read('WhimTexWindow.UI.cs'), /if \(TryOpenFileLayerDocument\(row, layer, evt\)\) return;/);
    assert.match(source, /evt.button != 0 \|\| evt.clickCount != 2/);
    assert.match(source, /evt.altKey \|\| evt.ctrlKey \|\| evt.commandKey \|\| evt.shiftKey/);
    assert.match(source, /layer\?\.Behaviour is FileLayerBehaviour file/);
    assert.match(source, /FindLayerDragControl\(row, evt.target as VisualElement\) != null/);
    assert.match(source, /!IsLayerDragArea\(row, evt.target as VisualElement\)/);
    assert.match(source, /WhimTexDocumentService.IsDocumentAsset\(source\)/);
    assert.doesNotMatch(source, /WhimTexDocument.FindDocument/);
    assert.match(source, /OpenWhimTexDocumentPath\(UnityEditor.AssetDatabase.GetAssetPath\(source\)\)/);
    assert.match(source, /activeLayerDrag\?\.Cancel\(\)/);
    assert.match(source, /EditorApplication.delayCall/);
    assert.match(source, /if \(this != null && source != null/);
    assert.match(source, /candidate.activeDocument == document/);
    assert.match(source, /existing.Show\(\);\s*existing.Focus\(\);\s*return existing;/);
    assert.match(source, /CreateWindow<WhimTexWindow>\("WhimTex", typeof\(WhimTexWindow\)\)/);
    assert.match(source, /window.SetDocument\(document\)/);
    assert.ok(!source.includes('GetWindow<'), 'Never replace a different window document');
    assert.ok(!/SaveAssets|SaveWithOutput|ExecuteModelChange/.test(source), 'Navigation does not save or edit assets');
    
    const newDocument = source.slice(source.indexOf('private WhimTexWindow OpenNewDocument()'), source.indexOf('private bool TryOpenFileLayerDocument'));
    assert.match(read('WhimTexWindow.UI.cs'), /CreateToolbarButton\("New", \(\) => OpenNewDocument\(\), 46f\)/);
    assert.match(newDocument, /CreateWindow<WhimTexWindow>/);
    assert.match(newDocument, /FinishPaintingStroke\(\)/);
    assert.match(newDocument, /window.RefreshDocumentTitle\(true\)/);
    assert.ok(!/ResolveUnsavedTemporaryDocument|SetDocument|GetWindow</.test(newDocument), 'New never replaces or resolves the current document');
});

await finish(context);

