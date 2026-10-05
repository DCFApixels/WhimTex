// Independent read-only source/scalar replacement. Does not execute Unity, C#, or GPU code.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const context = new TestContext('AgentCommandInventory: source/scalar contracts');
const assert = context.assert;

context.case('AgentCommandInventory original assertions and branches', async () => {
    // Source contract check for the Pipeline command inventory. Unity registration is verified by
    // the connected-Editor smoke command; this test catches accidental renames or duplicate IDs early.
    
    const root = fileURLToPath(new URL('../../../', import.meta.url));
    const source = readFileSync(path.join(root, 'src/Automation/Pipeline/WhimTexCommands.cs'), 'utf8');
    const registered = [...source.matchAll(/CliCommand\("([^"]+)"/g)].map(match => match[1]);
    const expected = [
      'whimtex_document_json',
      'whimtex_assistant_begin', 'whimtex_assistant_lock', 'whimtex_assistant_sessions', 'whimtex_assistant_live',
      'whimtex_describe', 'whimtex_assistant_execute', 'whimtex_fx_catalog', 'whimtex_render_probe',
      'whimtex_document_inspect', 'whimtex_batch_execute', 'whimtex_image_import',
      'whimtex_document_render', 'whimtex_storage_inspect',
      'whimtex_document_validate',
      'whimtex_fx_compile', 'whimtex_document_status', 'whimtex_document_compare',
      'whimtex_document_recover', 'whimtex_document_export', 'whimtex_headless_live'
    ];
    
    assert.deepEqual(registered, expected, 'Pipeline command inventory changed unexpectedly');
    assert.equal(new Set(registered).size, registered.length, 'Pipeline command IDs must be unique');
    assert.ok(!registered.some(id => /whimtex_(tiff_live|live|begin|lock|sessions|execute|render|import_image|migrate|inspect_storage|validate|status|compare|recover|export|inspect)$/.test(id)),
      'Legacy ambiguous command ID remains registered');
});

await finish(context);

