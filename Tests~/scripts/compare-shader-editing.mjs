import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const paths = process.argv.slice(2);
if (paths.length !== 2) throw Error('Usage: compare-shader-editing.mjs before/measurements.json after/measurements.json');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const [before, after] = paths.map(read);
assert.deepEqual(read(path.join(path.dirname(paths[0]), 'plan.json')),
    read(path.join(path.dirname(paths[1]), 'plan.json')), 'Plans differ');
for (const key of ['fixtureHash', 'unity', 'graphics', 'device', 'cachePolicy', 'timing'])
    assert.equal(before[key], after[key], 'Different ' + key);
assert.equal(before.status, 'complete', 'Incomplete baseline');
assert.equal(after.status, 'complete', 'Incomplete candidate');
const samples = report => report.samples.filter(s => s.index >= 0);
const oldSamples = samples(before), newSamples = samples(after);
assert.equal(oldSamples.length, newSamples.length, 'Different sample count');
const rows = newSamples.map((sample, i) => {
    const old = oldSamples[i];
    for (const key of ['pass', 'index', 'action', 'visiblePreviews'])
        assert.equal(old[key], sample[key], 'Unmatched sample ' + i + ': ' + key);
    assert.equal(old.status, 'complete'); assert.equal(sample.status, 'complete');
    return { pass: sample.pass, edit: sample.index, action: sample.action,
        beforeMs: Math.round(old.finalCompileReadyMs), afterMs: Math.round(sample.finalCompileReadyMs),
        deltaMs: Math.round(sample.finalCompileReadyMs - old.finalCompileReadyMs),
        nativeBefore: old.nativeStartsTotal, nativeAfter: sample.nativeStartsTotal,
        allBeforeMs: Math.round(old.allVisibleCompileReadyMs), allAfterMs: Math.round(sample.allVisibleCompileReadyMs) };
});
console.table(rows);
const percentile = (values, p) => [...values].sort((a,b) => a-b)[Math.ceil(values.length * p) - 1];
console.table([before, after].flatMap((report, i) => [...new Set(samples(report).map(s => s.pass))].map(pass => {
    const items = samples(report).filter(s => s.pass === pass), times = items.map(s => s.finalCompileReadyMs);
    return { run: i ? 'after' : 'before', pass, p50Ms: Math.round(percentile(times, .5)),
        p95Ms: Math.round(percentile(times, .95)), maxMs: Math.round(Math.max(...times)),
        over3s: times.filter(t => t > 3000).length, nativeStarts: items.reduce((n,s) => n+s.nativeStartsTotal, 0) };
})));
console.log(before.cachePolicy);
console.log('Native-start counts include node previews; all-times exclude rendering/paint.');
