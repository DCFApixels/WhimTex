import fs from 'node:fs';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import test from 'node:test';
const root = new URL('../../', import.meta.url);
const file = new URL('Tests~/Cases/UnityC/ReviewedOracle.cs', root);
const source = fs.readFileSync(file, 'utf8');
const imageStart = source.indexOf('        public static string Image(');
const image = source.slice(imageStart);
const sha = value => crypto.createHash('sha256').update(value).digest('hex');
// Static source guards only: these do not execute C# or prove native artifact delivery.
test('only Image changed; other oracle authentication/assertion paths and shared fixture frozen', () => {
    assert.equal(sha(source.slice(0, imageStart)), '83acb7c88d6e92beff7425123ae7c56f08f1afd7aad11e4b8bd67751311a4975');
    assert.equal(sha(fs.readFileSync(new URL('Tests~/Cases/UnityC/UnityCFixture.cs', root))), '11fed83b70eebdf4cba08e3f6bed0f97f360b0720a72291d4c479dbaf134be7d');
});
test('artifact JSON uses explicit public token objects, never interpreted DTO serialization', () => {
    for (const name of ['JObject', 'JArray', 'JValue']) assert.ok(image.includes('Newtonsoft.Json.Linq.' + name));
    assert.ok(image.includes('GetConstructor(new[] { typeof(string) })'));
    assert.ok(!/JsonUtility|SerializeObject|FromObject|BindingFlags|UnityEditor/.test(image));
});
test('capture is limited to current scope identity, GUID outputs and non-linked paths', () => {
    assert.ok(image.indexOf('var scope = FixtureContext.Scope;') < image.indexOf('string report = body(data);'));
    for (const guard of ['object.ReferenceEquals(scope, FixtureContext.Scope)', 'TryParseExact(leaf.Substring(7), "N"', 'input.StartsWith(prefix', 'FileAttributes.ReparsePoint', 'absolute.StartsWith(prefix']) assert.ok(image.includes(guard));
    assert.ok(!/File.Write|Directory.Create|AssetDatabase|baseline|golden/i.test(image));
});
test('output bytes captured before delegate returns and owned disposal; status JSON parsed afterward', () => {
    const callback = image.indexOf('FixtureContext.Diagnostic(label, () =>');
    const read = image.indexOf('System.IO.File.ReadAllBytes(file)');
    const returned = image.indexOf('return report;');
    const parsed = image.indexOf('object result = objectType.GetMethod("Parse"');
    assert.ok(callback < read && read < returned && returned < parsed);
    for (const key of ['name', 'encoding', 'content']) assert.ok(image.includes('Set(artifact, "' + key + '"'));
});
test('failed body/assertion/capture/cleanup responses pass through; only skipped gets artifacts', () => {
    const guard = image.indexOf('if (status == null || status.ToString() != "skipped") return diagnostic;');
    const attach = image.indexOf('Set(result, "artifacts", artifacts);');
    assert.ok(guard >= 0 && guard < attach);
    assert.ok(!/Set\(result, "(?:status|checks|failures)"/.test(image));
    assert.ok(image.includes('TestContext.Result("failed", 0, label, error.ToString())'));
});

