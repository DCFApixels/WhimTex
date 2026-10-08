// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('DocumentTitle: source/scalar contracts');
const assert = context.assert;

context.case('DocumentTitle original assertions and branches', async () => {
    const read = name => readFileSync(new URL(`../../../src/${name}`, import.meta.url), 'utf8');
    const source = read('WhimTexWindow.DocumentTitle.cs');
    const window = read('WhimTexWindow.cs');
    assert.match(source, /OnProjectChange\(\) => RefreshDocumentTitle\(true\)/);
    assert.match(source, /titleDocument == activeDocument && titleDocumentName == documentName/);
    assert.match(source, /Path.GetFileNameWithoutExtension\(path\)/);
    assert.match(source, /if \(string.IsNullOrWhiteSpace\(title\)\) title = "Untitled"/);
    assert.match(source, /content.tooltip = .*"WhimTex — "/);
    assert.match(source, /WhimTexBranding.WindowTitle\(title\)/);
    assert.match(window, /result.name = "Untitled"/);
    assert.match(window, /UpdateUnsavedChangesState\(\)\s*\{\s*RefreshDocumentTitle\(\)/);
    assert.ok(!window.includes('titleContent = WhimTexBranding.WindowTitle("WhimTex")'));
    assert.ok(!source.includes('hasUnsavedChanges ='), 'Unity retains ownership of the unsaved asterisk');
});

await finish(context);

