// Independent Node assertions; no Unity commands or Legacy runtime dependencies.
import { TestContext, finish } from '../../Framework/test-api.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const context = new TestContext("RenameMarkerCleanup source/reference tests");
const assert = context.assert;
context.case("RenameMarkerCleanup original assertion inputs and source contracts", async () => {

  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
  const read = name => fs.readFileSync(path.join(root, name), 'utf8');
  function scan(directory) {
    for (const file of fs.readdirSync(path.join(root, directory), { withFileTypes: true })) {
      const name = path.join(directory, file.name);
      if (file.isDirectory()) scan(name);
      else if (name.endsWith('.cs'))
        assert.doesNotMatch(read(name), /\[(?:UnityEngine\.(?:Serialization|Scripting\.APIUpdating)\.)?(?:MovedFrom|FormerlySerializedAs)\(/, name);
    }
  }
  scan('src');
  const serializer = read('src/WhimTexDocumentSerializer.cs');
  assert.doesNotMatch(serializer, /FindMovedType|MovedTypeNames|MatchesMovedName|AttributesNamed|StringMember/);
  assert.match(serializer, /RecordSkippedField/);
  assert.match(serializer, /_missingTypeNames\.Add\(name\)/);
  const directory = 'Tests~/Fixtures/ShaderFX0125';
  const manifest = JSON.parse(read(directory + '/manifest.json'));
  assert.equal(manifest.packageVersion, '0.12.5');
  assert.equal(manifest.commit, 'a72cc9544d39f93555ca6e9f39c137340e3028f0');
  assert.equal(manifest.files.length, 5);
  for (const entry of manifest.files) {
    assert.equal(path.basename(entry.file), entry.file);
    const bytes = fs.readFileSync(path.join(root, directory, entry.file));
    const gitBlob = createHash('sha1').update(Buffer.from('blob ' + bytes.length + '\0')).update(bytes).digest('hex');
    assert.equal(gitBlob, entry.gitBlob, entry.file + ': immutable tag source');
    const current = read('src/FXPresets/' + entry.file).replace(/\r\n/g, '\n');
    assert.doesNotMatch(current, /@formerlyserializedas/);
    assert.equal(current, bytes.toString('utf8').replace(/^\/\/ @formerlyserializedas\([^\n]*\)\n/gm, ''),
      entry.file + ': only the obsolete alias changed');
    assert.match(current, /@param hidden float _Opacity/);
  }
  // User-authored files may contain the supported rename directive; it is not a Unity migration attribute.
  assert.match(read('src/ShaderFXMetadata.cs'), /internal static bool HasFormerName/);
  assert.match(read('src/ShaderFXPresetWriter.cs'), /@formerlyserializedas/);
  assert.match(read('src/BrushTipProgram.cs'), /@formerlyserializedas/);

});
await finish(context);

