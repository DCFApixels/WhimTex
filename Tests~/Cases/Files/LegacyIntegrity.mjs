import { TestContext, finish } from '../../Framework/test-api.mjs';
import { verifyLegacy } from '../../scripts/legacy.mjs';
const context = new TestContext('Immutable Legacy archive');
context.case('Inventory and all archived file bytes', () => {
    const archive = verifyLegacy();
    context.assert.equal(archive.files, 466);
    context.assert.equal(archive.baselineCommit, '1269df51866959744a4b0763453eb667aefe6d8e');
    context.facts = archive;
});
await finish(context);
