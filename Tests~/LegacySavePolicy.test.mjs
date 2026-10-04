import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
const read = name => readFileSync(new URL(`../src/${name}`, import.meta.url), 'utf8');
for (const removed of [
  'Editor/Legacy/TextureCompositor.LegacyAssetWriter.cs', 'Editor/WhimTexLegacyMigration.cs',
  'Editor/TextureCompositorEditor.cs', 'Editor/WhimTexOutputSettingsWindow.cs',
  'TextureCompositor.OutputProcessing.cs', 'TextureCompositor.Sprites.cs',
  'TextureCompositor.LinkedTexture.cs', 'LiveOutputSession.cs',
  'Automation/WhimTexApi.Migration.cs'
]) assert(!existsSync(new URL(`../src/${removed}`, import.meta.url)), removed);
assert.doesNotMatch(read('TextureCompositor.cs'), /\[CreateAssetMenu\(/);
assert.doesNotMatch(read('TextureCompositor.Assets.cs'), /SaveLegacyAssetForCompatibility|AssetDatabase\.CreateAsset\(/);
assert.match(read('WhimTexDocumentService.cs'), /".asset"[\s\S]*?throw new WhimTexDocumentException/);
assert.match(read('WhimTexDocumentFile.cs'), /IsTiffDocumentPath\(assetPath\)/);
assert.doesNotMatch(read('Automation/Pipeline/WhimTexCommands.cs'), /whimtex_document_migrate/);
assert.doesNotMatch(read('Automation/WhimTexApi.Assets.cs'), /extension, ".asset"/);
console.log('PASS: retired document backend absent; TIFF/JSON boundaries enforced.');
