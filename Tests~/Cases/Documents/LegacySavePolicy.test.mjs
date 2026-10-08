// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { readFileSync, existsSync } from 'node:fs';
const context = new TestContext("LegacySavePolicy source/reference tests");
const assert = context.assert;
context.case("LegacySavePolicy original assertion inputs and source contracts", async () => {
  const read = name => readFileSync(new URL(`../../../src/${name}`, import.meta.url), 'utf8');
  for (const removed of [
    'Editor/Legacy/WhimTexDocument.LegacyAssetWriter.cs', 'Editor/WhimTexLegacyMigration.cs',
    'Editor/WhimTexDocumentEditor.cs', 'Editor/WhimTexOutputSettingsWindow.cs',
    'WhimTexDocument.OutputProcessing.cs', 'WhimTexDocument.Sprites.cs',
    'WhimTexDocument.LinkedTexture.cs', 'LiveOutputSession.cs',
    'Automation/WhimTexApi.Migration.cs'
  ]) assert(!existsSync(new URL(`../../../src/${removed}`, import.meta.url)), removed);
  assert.doesNotMatch(read('WhimTexDocument.cs'), /\[CreateAssetMenu\(/);
  assert.doesNotMatch(read('WhimTexDocument.Assets.cs'), /SaveLegacyAssetForCompatibility|AssetDatabase\.CreateAsset\(/);
  assert.match(read('WhimTexDocumentService.cs'), /".asset"[\s\S]*?throw new WhimTexDocumentException/);
  assert.match(read('WhimTexDocumentFile.cs'), /IsTiffDocumentPath\(assetPath\)/);
  assert.doesNotMatch(read('Automation/Pipeline/WhimTexCommands.cs'), /whimtex_document_migrate/);
  assert.doesNotMatch(read('Automation/WhimTexApi.Assets.cs'), /extension, ".asset"/);

});
await finish(context);

