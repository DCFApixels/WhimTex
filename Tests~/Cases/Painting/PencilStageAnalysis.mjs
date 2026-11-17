// Read-only analysis of two completed matrices; writes only the requested Temp report.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';

const [project, plainId, profileId] = process.argv.slice(2);
assert(project && plainId && profileId, 'Usage: node PencilStageAnalysis.mjs <project> <plain-run-id> <profile-run-id>');
for (const id of [plainId, profileId]) assert(/^[\w-]+$/.test(id), 'Invalid run ID');
const base = path.resolve(project, 'Temp/WhimTex');
const read = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const manifests = [plainId, profileId].map(id => read(path.join(base, 'pencil-stages', `${id}.json`)));
assert.equal(manifests[0].profile, false);
assert.equal(manifests[1].profile, true);
assert.equal(manifests[0].fixtureHash, manifests[1].fixtureHash, 'Cycle fixture changed between runs');
for (const m of manifests) { assert.equal(m.status, 'passed'); assert.equal(m.runs.length, 10); }
const stats = values => {
    assert(values.length && values.every(Number.isFinite), 'Empty or nonfinite samples');
    const sorted = [...values].sort((a, b) => a - b), n = sorted.length;
    return { n, min: sorted[0], median: (sorted[(n - 1) >> 1] + sorted[n >> 1]) / 2,
        mean: sorted.reduce((a, b) => a + b, 0) / n, p95: sorted[Math.ceil(n * .95) - 1], max: sorted[n - 1] };
};
const measured = f => f.phase === 'input' && f.point > 0 && f.stroke >= 2;
const phases = ['build_stamps', 'mesh_build', 'raster_prepare', 'index_build', 'mesh_upload', 'index_upload', 'mesh_submit'];
const rows = manifests[0].runs.map((id, i) => {
    const files = [id, manifests[1].runs[i]].map(run => path.join(base, 'raster-cycle-audit', run));
    const [plain, profiled] = files.map(file => read(`${file}.json`));
    for (const r of [plain, profiled]) {
        assert.equal(r.status, 'passed'); assert.equal(r.asyncErrors, 0);
        assert.equal(r.lastUndoSkippedReason, null); assert.equal(r.canvasPlacementVerified, true);
    }
    assert.deepEqual({ ...plain.config, profile: null }, { ...profiled.config, profile: null });
    assert.equal(hash(`${files[0]}.png`), hash(`${files[1]}.png`), 'Preview changed under instrumentation');
    const capture = read(`${files[1]}.profile.json`);
    assert.equal(capture.dropped, 0); assert.equal(capture.count, capture.events.length);
    const inputs = profiled.frames.filter(f => f.phase === 'input');
    const plainInputs = plain.frames.filter(f => f.phase === 'input');
    const roots = capture.events.filter(e => e.phase === 'brush.segment').sort((a, b) => a.start - b.start);
    assert.equal(roots.length, inputs.length); assert.equal(plainInputs.length, inputs.length);
    const eventsById = new Map(capture.events.map(e => [e.id, e]));
    const samples = inputs.map((frame, j) => {
        const root = roots[j];
        const events = capture.events.filter(e => e.operationId === root.operationId && e.kind === 'span');
        const duration = event => (event.end - event.start) * 1000 / capture.frequency;
        for (const event of events) {
            assert(event.completed && !event.interrupted && event.end >= event.start);
            let ancestor = event;
            while (ancestor.id !== root.id && ancestor.parentId) ancestor = eventsById.get(ancestor.parentId);
            assert.equal(ancestor.id, root.id, 'Span belongs to another operation');
        }
        const s = { stroke: frame.stroke, point: frame.point, phase: frame.phase, paintMs: frame.paintMs,
            segmentMs: duration(root) };
        for (const p of phases) {
            const spans = events.filter(e => e.phase === `brush.${p}`);
            assert.equal(spans.length, 1, `Missing/ambiguous ${p}; compile the temporary instrumentation first`);
            s[p] = spans.reduce((sum, event) => sum + duration(event), 0);
        }
        for (const key of ['stroke', 'point', 'stamps', 'vertices', 'indices', 'vertexPayloadBytes', 'indexPayloadBytes']) {
            assert.equal(frame[key], plainInputs[j][key], `${key} differs under instrumentation`);
            s[key] = frame[key];
        }
        s.geometryMs = s.mesh_build + s.raster_prepare + s.index_build;
        s.uploadApiMs = s.mesh_upload + s.index_upload;
        s.rasterOtherMs = s.mesh_submit - s.index_build - s.index_upload;
        s.otherMs = s.segmentMs - s.build_stamps - s.geometryMs - s.uploadApiMs - s.rasterOtherMs;
        assert(s.rasterOtherMs >= 0 && s.otherMs >= 0, 'Overlapping stage accounting');
        return s;
    }).filter(measured);
    const numericKeys = Object.keys(samples[0]).filter(key => !['stroke', 'point', 'phase'].includes(key));
    return { config: plain.config, unity: plain.unity, device: plain.device, backend: plain.backend,
        plainPaintMs: stats(plain.frames.filter(measured).map(f => f.paintMs)),
        profile: Object.fromEntries(numericKeys.map(key => [key, stats(samples.map(s => s[key]))])),
        checks: [plain.checks, profiled.checks], identicalPreview: true, inputCount: inputs.length,
        capacityWaits: [plain.capacityWaits, profiled.capacityWaits], samples };
});
for (let i = 0; i < rows.length; i += 2) {
    for (const key of ['stamps', 'vertices', 'indices', 'vertexPayloadBytes', 'indexPayloadBytes'])
        assert.deepEqual(rows[i].samples.map(s => s[key]), rows[i + 1].samples.map(s => s[key]), `${key}: inside/outside mismatch`);
}
const result = { plainId, profileId, warmupStrokes: 2, timingUnit: 'ms',
    note: 'CPU wall spans; mesh upload API calls include Unity/driver work, not device DMA timing. Separate runs are not paired overhead estimates. Medians do not add.', rows };
const output = path.join(base, 'pencil-stages', `${plainId}-analysis.json`);
fs.writeFileSync(output, JSON.stringify(result, null, 2) + '\n');
console.table(rows.map(r => ({ scenario: r.config.name, n: r.plainPaintMs.n,
    plain: r.plainPaintMs.median.toFixed(3), generation: r.profile.build_stamps.median.toFixed(3),
    geometry: r.profile.geometryMs.median.toFixed(3), uploadApi: r.profile.uploadApiMs.median.toFixed(3),
    rasterOther: r.profile.rasterOtherMs.median.toFixed(3), profile: r.profile.paintMs.median.toFixed(3),
    profileMax: r.profile.paintMs.max.toFixed(3) })));
console.log(`Verified ${rows.length} cases; ${output}`);
