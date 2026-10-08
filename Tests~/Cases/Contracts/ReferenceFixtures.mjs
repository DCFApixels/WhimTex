// Authenticate committed reference inputs. No rendering, capture or fixture generation.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { TestContext, finish } from '../../Framework/test-api.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const directory = path.join(root, 'Tests~/Fixtures/Oracles');
const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'manifest.json'), 'utf8'));
const context = new TestContext('Frozen reference fixture bytes and oracle safety');
const assert = context.assert;
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
function authenticated(record, read = fs.readFileSync) {
    if (!record || !/^[a-f0-9]{64}$/.test(record.sha256) || !record.provenance?.trim() ||
        typeof record.path !== 'string' || path.isAbsolute(record.path)) throw Error('Invalid reference registration');
    const file = path.resolve(directory, record.path);
    if (!file.startsWith(directory + path.sep)) throw Error('Reference path escaped fixtures');
    if (!fs.lstatSync(file).isFile() || fs.lstatSync(file).isSymbolicLink() ||
        !fs.realpathSync(file).startsWith(fs.realpathSync(directory) + path.sep)) throw Error('Linked/non-file reference');
    const bytes = read(file);
    if (sha(bytes) !== record.sha256) throw Error('Reference hash mismatch: ' + record.path);
    return bytes;
}
context.case('All 255 references have distinct local paths and authenticated bytes', () => {
    assert.equal(manifest.version, 1);
    const records = [manifest.live4, ...manifest.seamlessOptimization, ...manifest.images];
    assert.equal(records.length, 255);
    assert.equal(new Set(records.map(r => r.path)).size, 255);
    for (const record of records) assert.ok(authenticated(record).length > 0, record.path);
    const provenance = JSON.parse(fs.readFileSync(path.join(directory, 'provenance.json'), 'utf8'));
    assert.equal(provenance.version, 1);
    assert.ok(provenance.limitations.some(value => value.includes('not cryptographic continuity')));
});
context.case('Float references retain dimensions, layout and complete 252-case matrix', () => {
    const live = authenticated(manifest.live4);
    assert.equal(live.readInt32LE(0), 512); assert.equal(live.readInt32LE(4), 512);
    assert.equal(live.length, 8 + 512 * 512 * 16);
    const records = new Map(manifest.seamlessOptimization.map(r => [r.name, r]));
    assert.equal(records.size, 252);
    for (const [w, h] of [[1, 7], [17, 13], [64, 48], [65, 63]])
        for (let fixture = 0; fixture < 3; fixture++) for (let mode = 0; mode < 4; mode++)
            for (let edge = 0; edge < 3; edge++) for (const strength of [0, 1]) {
                if (mode === 2 && strength === 0) continue;
                const name = `${w}-${h}-${fixture}-${mode}-${edge}-${strength}.bin`;
                assert.ok(records.has(name), name);
                const bytes = authenticated(records.get(name));
                assert.equal(bytes.length, w * h * 16, name);
                assert.ok(Array.from({ length: bytes.length / 4 }, (_, i) => bytes.readFloatLE(i * 4)).every(Number.isFinite), name + ': finite floats');
            }
});
context.case('Both original PNG inputs retain exact format and dimensions', () => {
    assert.deepEqual(manifest.images.map(r => r.name), ['copy-blend-source', 'screened-poisson-original']);
    for (const record of manifest.images) {
        const bytes = authenticated(record);
        assert.equal(bytes.length, 112799);
        assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
        assert.equal(bytes.readUInt32BE(16), 600); assert.equal(bytes.readUInt32BE(20), 600);
    }
});
context.case('Missing, escaping and altered references fail without generation', () => {
    const original = manifest.images[0];
    assert.throws(() => authenticated(null), /registration/);
    assert.throws(() => authenticated({ ...original, path: '../outside.png' }), /escaped/);
    assert.throws(() => authenticated({ ...original, path: path.resolve(directory, original.path) }), /registration/);
    assert.throws(() => authenticated({ ...original, path: 'missing.png' }), /ENOENT/);
    assert.throws(() => authenticated(original, () => Buffer.from('changed bytes')), /hash mismatch/);
});
const source = fs.readFileSync(path.join(root, 'Tests~/Framework/Unity/ReviewedOracle.cs'), 'utf8');
const image = source.slice(source.indexOf('        public static string Image('));
context.case('Unity authenticates package-relative input bytes before use', () => {
    assert.ok(source.includes('Tests~/Fixtures/Oracles/manifest.json'));
    for (const guard of ['Path.IsPathRooted(file.path)', 'input.StartsWith(prefix', 'sha.ComputeHash(data)', 'Oracle byte length mismatch'])
        assert.ok(source.includes(guard), guard);
    assert.ok(source.includes('ReadAllBytes(InputPath(file))'));
    assert.ok(image.includes('string input = InputPath(match);'));
});
context.case('Artifact JSON uses public tokens, not interpreted DTO serialization', () => {
    for (const name of ['JObject', 'JArray', 'JValue']) assert.ok(image.includes('Newtonsoft.Json.Linq.' + name));
    assert.ok(image.includes('GetConstructor(new[] { typeof(string) })'));
    assert.doesNotMatch(image, /JsonUtility|SerializeObject|FromObject|BindingFlags|UnityEditor/);
});
context.case('Artifact capture stays in the current owned GUID scope without writes', () => {
    assert.ok(image.indexOf('var scope = FixtureContext.Scope;') < image.indexOf('string report = body(data);'));
    for (const guard of ['object.ReferenceEquals(scope, FixtureContext.Scope)', 'TryParseExact(leaf.Substring(7), "N"', 'input.StartsWith(prefix', 'FileAttributes.ReparsePoint', 'absolute.StartsWith(prefix'])
        assert.ok(image.includes(guard));
    assert.doesNotMatch(image, /File.Write|Directory.Create|AssetDatabase|baseline|golden/i);
});
context.case('Artifacts are captured before owned disposal and preserve failed verdicts', () => {
    const callback = image.indexOf('FixtureContext.Diagnostic(label, () =>');
    const read = image.indexOf('System.IO.File.ReadAllBytes(file)');
    const returned = image.indexOf('return report;');
    const parsed = image.indexOf('object result = objectType.GetMethod("Parse"');
    assert.ok(callback < read && read < returned && returned < parsed);
    for (const key of ['name', 'encoding', 'content']) assert.ok(image.includes('Set(artifact, "' + key + '"'));
    const guard = image.indexOf('if (status == null || status.ToString() != "skipped") return diagnostic;');
    assert.ok(guard >= 0 && guard < image.indexOf('Set(result, "artifacts", artifacts);'));
    assert.doesNotMatch(image, /Set\(result, "(?:status|checks|failures)"/);
    assert.ok(image.includes('TestContext.Result("failed", 0, label, error.ToString())'));
});
await finish(context);
