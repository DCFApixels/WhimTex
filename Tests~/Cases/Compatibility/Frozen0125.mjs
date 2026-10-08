import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { TestContext, finish } from '../../Framework/test-api.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const directory = path.join(root, 'Tests~/Fixtures/Compatibility0125');
const context = new TestContext('Frozen 0.12.5 file integrity');
const assert = context.assert;
context.case('Original file hashes, render coverage and unsupported asset boundary', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'manifest.json'), 'utf8'));
    assert.equal(manifest.packageVersion, '0.12.5');
    assert.equal(manifest.commit, 'a72cc9544d39f93555ca6e9f39c137340e3028f0');
    assert.equal(manifest.documents.length, 8);
    assert.equal(manifest.documents.filter(entry => path.extname(entry.file) !== '.asset').length, 7);
    assert.equal(manifest.sha256.length, 30);
    const expectedFiles = new Set(manifest.sha256.map(entry => entry.file));
    assert.equal(expectedFiles.size, manifest.sha256.length);
    for (const entry of manifest.sha256) {
        assert.equal(path.basename(entry.file), entry.file, 'Fixture paths must stay in their directory');
        const bytes = fs.readFileSync(path.join(directory, entry.file));
        assert.equal(createHash('sha256').update(bytes).digest('hex'), entry.sha256, entry.file);
    }
    const renders = new Set();
    for (const entry of manifest.documents) {
        assert.ok(expectedFiles.has(entry.file) && expectedFiles.has(entry.render));
        assert.ok(entry.layers.every(layer => layer.id && layer.type));
        assert.equal(entry.layers.at(-1).name, 'Background', `${entry.file}: background must not cover test content`);
        const bytes = fs.readFileSync(path.join(directory, entry.render));
        assert.equal(bytes.length, entry.width * entry.height * 16);
        let nonuniform = false;
        for (let i = 0; i < bytes.length; i += 16) {
            for (let c = 0; c < 4; c++) assert.ok(Number.isFinite(bytes.readFloatLE(i + c * 4)));
            for (let c = 0; c < 3; c++) if (Math.abs(bytes.readFloatLE(i + c * 4) - bytes.readFloatLE(c * 4)) > .01) nonuniform = true;
        }
        assert.ok(nonuniform, `${entry.file}: uniform renders do not exercise the composition`);
        renders.add(createHash('sha256').update(bytes).digest('hex'));
    }
    assert.equal(renders.size, 3, 'Procedural, SDR and HDR fixtures need distinct rendered images');
    const activeDocument = fs.readFileSync(path.join(root, 'src/WhimTexDocument.cs'), 'utf8');
    assert.doesNotMatch(activeDocument, /\[CreateAssetMenu\(/);
    assert.ok(!fs.existsSync(path.join(root, 'src/Editor/WhimTexDocumentProjectPreview.cs')));
    context.facts = { fileHashes: 30, historicalDocuments: 7, archivedAssetDocuments: 1, distinctRenders: 3 };
});
await finish(context);
