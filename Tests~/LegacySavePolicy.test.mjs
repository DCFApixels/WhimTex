import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = name => readFileSync(new URL(`../src/${name}`, import.meta.url), 'utf8');
const documentFile = read('TextureCompositorWindow.DocumentFile.cs');
const inspector = read('Editor/TextureCompositorEditor.cs');
const outputWindow = read('Editor/WhimTexOutputSettingsWindow.cs');
const styles = read('WhimTexSplitView.uss');

assert.match(documentFile, /if \(IsLegacyAssetPath\(path\)\)\s*return SaveDocumentAs\(compositor\);/,
  'Ctrl+S must route bound legacy files to TIFF Save As');
assert.match(documentFile, /internal static bool IsLegacyAssetPath\(string path\)/,
  'Legacy path detection must be explicit');
assert.match(documentFile, /Legacy WhimTex asset is read-only/,
  'Direct legacy SaveDocumentTo calls must be rejected');
assert.match(inspector, /SaveDocumentAsTiff\(document\)/,
  'Legacy inspector must offer TIFF Save As');
assert.match(outputWindow, /Save As TIFF…/,
  'Legacy output settings must offer TIFF Save As');
assert.ok(!inspector.includes('whimtex-output-compression'),
  'Output Settings must not render the obsolete compression panel');
assert.ok(!styles.includes('whimtex-output-compression'),
  'Obsolete compression panel styles must be removed');

console.log('Legacy save policy and compression UI checks passed.');
