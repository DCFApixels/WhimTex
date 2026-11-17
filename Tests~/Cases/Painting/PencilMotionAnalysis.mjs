import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';

const [project, beforeTiming, afterTiming, beforeExact, afterExact] = process.argv.slice(2);
assert(project && beforeTiming && afterTiming && beforeExact && afterExact,
    'Usage: node PencilMotionAnalysis.mjs <project> <before-timing> <after-timing> <before-exact> <after-exact>');
const base = path.resolve(project, 'Temp/WhimTex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const manifest = id => {
    assert(/^[\w-]+$/.test(id));
    const m = read(path.join(base, 'pencil-stages', `${id}.json`));
    assert.equal(m.status, 'passed'); assert.equal(m.profile, false);
    return m;
};
const loadCases = m => m.runs.map(id => {
    const r = read(path.join(base, 'raster-cycle-audit', `${id}.json`));
    assert.equal(r.status, 'passed'); assert.equal(r.config.side, 8192);
    assert.equal(r.lastUndoSkippedReason, null); assert.equal(r.asyncErrors, 0);
    assert.equal(r.canvasPlacementVerified, true);
    return r;
});
const stats = values => {
    const v = [...values].sort((a, b) => a - b), n = v.length;
    assert(n && v.every(Number.isFinite));
    return { n, min: v[0], median: (v[(n - 1) >> 1] + v[n >> 1]) / 2,
        p95: v[Math.ceil(n * .95) - 1], max: v[n - 1], mean: v.reduce((a, b) => a + b) / n };
};
const manifests = [beforeTiming, afterTiming, beforeExact, afterExact].map(manifest);
for (const m of manifests) assert.equal(m.fixtureHash, manifests[0].fixtureHash, 'Cycle fixture changed');
const [bt, at, be, ae] = manifests.map(loadCases);
assert.equal(bt.length, 12); assert.equal(at.length, 12); assert.equal(be.length, 8); assert.equal(ae.length, 8);
const input = r => r.frames.filter(f => f.phase === 'input');
const warm = r => input(r).filter(f => f.point > 0 && f.stroke >= 2);
const timings = bt.map((b, i) => {
    const a = at[i]; assert.deepEqual(b.config, a.config);
    const bf = input(b), af = input(a); assert.equal(bf.length, af.length);
    for (let j = 0; j < bf.length; j++)
        for (const k of ['stroke', 'point', 'stamps', 'vertices', 'indices', 'vertexPayloadBytes', 'indexPayloadBytes'])
            assert.equal(bf[j][k], af[j][k], `${b.config.name}: ${k}`);
    const before = stats(warm(b).map(f => f.paintMs)), after = stats(warm(a).map(f => f.paintMs));
    assert.equal(before.n, 10); assert.equal(after.n, 10);
    return { name: b.config.name, before, after, gainPercent: (1 - after.median / before.median) * 100,
        stamps: warm(a)[0].stamps, vertices: warm(a)[0].vertices,
        poolBytes: [b.peakPoolBytes, a.peakPoolBytes], stagingBytes: [b.peakStagingBytes, a.peakStagingBytes],
        capacityWaits: [b.capacityWaits, a.capacityWaits] };
});
const exact = be.map((b, i) => {
    const a = ae[i]; assert.deepEqual(b.config, a.config);
    const digests = ['pendingHash', 'committedHash', 'undoHash', 'redoHash'];
    for (const key of digests) {
        assert.match(b[key], /^[A-F0-9]{64}$/); assert.equal(b[key], a[key], `${b.config.name}: ${key} changed`);
    }
    for (const r of [b, a]) {
        assert.equal(r.pendingHash, r.committedHash, 'Pending/commit differ');
        assert.equal(r.committedHash, r.redoHash, 'Redo differs');
        assert.notEqual(r.undoHash, r.redoHash, 'Undo must actually change the pixels');
    }
    return { name: b.config.name, hashes: Object.fromEntries(digests.map(k => [k, a[k]])), checks: [b.checks, a.checks] };
});
const output = path.join(base, 'pencil-motion', `${afterTiming}-comparison.json`);
fs.mkdirSync(path.dirname(output), { recursive: true });
const report = { beforeTiming, afterTiming, beforeExact, afterExact, timings, exact,
    provenance: manifests.map(m => ({ id: m.runId, sourceHash: m.sourceHash, adapterHash: m.adapterHash, fixtureHash: m.fixtureHash })),
    analyzerHash: createHash('sha256').update(fs.readFileSync(new URL(import.meta.url))).digest('hex') };
fs.writeFileSync(output, JSON.stringify(report, null, 2) + '\n');
console.table(timings.map(r => ({ name: r.name, before: r.before.median.toFixed(3), after: r.after.median.toFixed(3),
    gain: `${r.gainPercent.toFixed(1)}%`, maxBefore: r.before.max.toFixed(3), maxAfter: r.after.max.toFixed(3) })));
console.log(`PASS: 12 timing cases, 8 exact strips / 32 before-after hashes. ${output}`);
