// Test-owned repository mirror under project Temp; never moves/deletes the frozen working archive.
import fs from 'node:fs';
import path from 'node:path';
import { randomUUID, createHash } from 'node:crypto';
import { TestContext, finish } from './test-api.mjs';
import { archiveMetadata, openLegacyArchive, legacyIO, decodeGitBlobs } from '../scripts/legacy.mjs';
import { root, runProcess } from '../scripts/run-tests.mjs';
const context = new TestContext('Pinned archive and new-only runner without a physical Legacy directory');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const project = path.resolve(root, '../..');

context.case('Authenticated pinned bytes and copy ownership, no working-copy fallback', () => {
    const archive = openLegacyArchive(root), metadata = archiveMetadata(root);
    context.assert.equal(archive.summary.files, 466);
    context.assert.equal(archive.summary.source, 'pinned-git');
    for (const item of metadata.manifest.files) {
        const bytes = archive.read(item.file);
        context.assert.equal(bytes.length, item.bytes);
        context.assert.equal(hash(bytes), item.sha256);
    }
    const first = metadata.manifest.files[0], borrowed = archive.read(first.file);
    borrowed.fill(0);
    context.assert.equal(hash(archive.read(first.file)), first.sha256);
    context.assert.throws(() => archive.read('../outside'));
    const guarded = { ...fs };
    for (const name of ['readFileSync', 'existsSync', 'statSync', 'lstatSync', 'readdirSync']) guarded[name] = (file, ...args) => {
        const rel = path.relative(path.join(root, 'Tests~/Legacy'), path.resolve(String(file)));
        if (rel === '' || rel !== '..' && !rel.startsWith('..' + path.sep) && !path.isAbsolute(rel)) throw Error('Physical Legacy read attempted.');
        return fs[name](file, ...args);
    };
    const io = legacyIO(root, guarded), file = path.join(root, 'Tests~/Legacy', first.file);
    context.assert.equal(hash(io.readFileSync(file)), first.sha256);
    context.assert.equal(io.statSync(file).size, first.bytes);
    context.assert.equal(io.lstatSync(file).isSymbolicLink(), false);
    context.assert.equal(io.existsSync(file), true);
    context.assert.ok(io.readdirSync(path.join(root, 'Tests~/Legacy'), { withFileTypes: true }).length > 0);
    context.assert.equal(io.existsSync(path.join(root, 'Tests~/Legacy/not-in-manifest')), false);
    context.assert.throws(() => io.readFileSync(path.join(root, 'Tests~/Legacy/not-in-manifest')));
});

context.case('Strict binary Git protocol rejects type/identity/length/truncation/trailing data', () => {
    const id = 'a'.repeat(40), body = Buffer.from([0, 13, 10, 255]);
    const good = Buffer.concat([Buffer.from(id + ' blob 4\n'), body, Buffer.from('\n')]);
    context.assert.deepEqual(decodeGitBlobs(good, [id]), [body]);
    for (const bad of [Buffer.from('missing\n'), Buffer.from(id + ' tree 0\n\n'),
        Buffer.from('b'.repeat(40) + ' blob 0\n\n'), Buffer.from(id + ' blob -1\n\n'),
        Buffer.from(id + ' blob 01\n\n'), good.subarray(0, good.length - 1),
        Buffer.concat([good, Buffer.from('extra')]), Buffer.from(id + ' blob 9007199254740992\n\n')])
        context.assert.throws(() => decodeGitBlobs(bad, [id]));
});

context.case('Real new-only quick runner and audit in a GUID Git mirror with no Legacy directory', async () => {
    const scope = path.join(project, 'Temp/WhimTex/archive-retirement-' + randomUUID());
    const mirror = path.join(scope, 'package');
    fs.mkdirSync(scope, { recursive: false });
    const clone = await runProcess('git', ['clone', '--shared', '--no-checkout', '--quiet', root, mirror], { timeoutMs: 30000 });
    context.assert.equal(clone.code, 0, clone.stderr || clone.error);
    context.assert.equal(clone.timedOut, false);
    const listing = await runProcess('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: root, timeoutMs: 10000 });
    context.assert.equal(listing.code, 0, listing.stderr || listing.error);
    context.assert.equal(listing.timedOut, false);
    for (const relative of [...new Set(listing.stdout.split('\0').filter(Boolean))]) {
        if (relative.startsWith('Tests~/Legacy/')) continue;
        const source = path.resolve(root, relative), target = path.resolve(mirror, relative);
        if (!target.startsWith(mirror + path.sep) || !source.startsWith(root + path.sep)) throw Error('Mirror path escape.');
        if (!fs.existsSync(source)) continue;
        if (!fs.lstatSync(source).isFile()) throw Error('Mirror only accepts regular source files.');
        fs.mkdirSync(path.dirname(target), { recursive: true });
        fs.copyFileSync(source, target, fs.constants.COPYFILE_EXCL);
    }
    context.assert.equal(fs.existsSync(path.join(mirror, 'Tests~/Legacy')), false);
    const script = "import fs from'node:fs';import{main,reviewFingerprint,selectScenarios}from'./Tests~/scripts/run-tests.mjs';const c=JSON.parse(fs.readFileSync('Tests~/scripts/test-catalog.json'));if(c.scenarios.some(s=>s.legacy||s.file.startsWith('Tests~/Legacy/')))throw Error('Retired scenario remained active');const s=selectScenarios(c,{profile:'quick'});process.exitCode=await main(['--run','--profile','quick','--reviewed',reviewFingerprint(s)]);";
    const run = await runProcess(process.execPath, ['--input-type=module', '-e', script], { cwd: mirror, timeoutMs: 30000 });
    context.assert.equal(run.timedOut, false);
    context.assert.equal(run.code, 0, run.stdout + run.stderr);
    const reports = path.join(mirror, 'Temp/WhimTex/test-runs');
    const reportFile = fs.readdirSync(reports).find(f => f.endsWith('.json'));
    context.assert.ok(reportFile);
    const raw = fs.readFileSync(path.join(reports, reportFile)), report = JSON.parse(raw);
    context.assert.equal(report.success, true);
    context.assert.equal(report.recoveryRequired, false);
    context.assert.deepEqual(report.selected, ['framework', 'archive-integrity', 'frozen-files-v2']);
    context.assert.ok(report.results.every(r => r.status === 'passed'));
    context.assert.equal(fs.existsSync(path.join(mirror, 'Tests~/Legacy')), false);
    const auditScript = "import{buildInventory}from'./Tests~/CoverageAudit/coverage-gate.mjs';const v=buildInventory({projectPath:" + JSON.stringify(project) + "});console.log(JSON.stringify({archive:v.archive,summary:v.summary}));";
    const audit = await runProcess(process.execPath, ['--input-type=module', '-e', auditScript], { cwd: mirror, timeoutMs: 30000 });
    context.assert.equal(audit.timedOut, false);
    context.assert.equal(audit.code, 0, audit.stdout + audit.stderr);
    const evidence = JSON.parse(audit.stdout);
    context.assert.equal(evidence.archive.verified, true);
    context.assert.equal(evidence.archive.files, 466);
    context.assert.equal(evidence.summary.expectedSourceFiles, 360);
    context.assert.equal(evidence.summary.currentReviewedSourceClaims, 360);
    context.assert.equal(evidence.summary.staleReview, 0);
    context.assert.equal(evidence.summary.sourceGapFiles, 0);
    context.assert.equal(evidence.summary.externalGapFiles, 0);
    context.assert.equal(fs.existsSync(path.join(mirror, 'Tests~/Legacy')), false);
    const generators = ['Tests~/CoverageAudit/node-audit.mjs', 'Tests~/CoverageAudit/unity-cd-audit.mjs',
        'Tests~/CoverageAudit/audit-unity-b-public.mjs', 'Tests~/CoverageAudit/framework-audit.mjs',
        'Tests~/CoverageAudit/final-inventory.mjs', 'Tests~/scripts/migration-inventory.mjs'];
    const generatorScript = "import{spawnSync}from'node:child_process';import{createHash}from'node:crypto';const evidence=[];for(const file of " + JSON.stringify(generators) + "){const args=file.endsWith('/node-audit.mjs')?['--schema']:file.endsWith('/unity-cd-audit.mjs')?['--json']:[];const r=spawnSync(process.execPath,[file,...args],{cwd:process.cwd(),timeout:15000,maxBuffer:64*1024*1024,windowsHide:true});if(r.error||r.status!==0)throw Error(file+': '+(r.error||r.stderr));const value=JSON.parse(r.stdout);evidence.push({file,args,exitCode:r.status,outputSha256:createHash('sha256').update(r.stdout).digest('hex'),rows:value.files?.length??value.summary?.originalSourceFiles??value.originalSourceFiles??null});}console.log(JSON.stringify(evidence));";
    const generated = await runProcess(process.execPath, ['--input-type=module', '-e', generatorScript], { cwd: mirror, timeoutMs: 30000 });
    context.assert.equal(generated.timedOut, false);
    context.assert.equal(generated.code, 0, generated.stdout + generated.stderr);
    const generatorEvidence = JSON.parse(generated.stdout);
    context.assert.deepEqual(generatorEvidence.map(r => r.file), generators);
    context.assert.ok(generatorEvidence.every(r => r.exitCode === 0));
    context.assert.equal(fs.existsSync(path.join(mirror, 'Tests~/Legacy')), false);
    context.facts.mirror = { directory: mirror, physicalLegacyExists: false,
        report: path.join(reports, reportFile), reportSha256: hash(raw),
        selected: report.selected, results: report.results.map(r => ({ id: r.id, status: r.status })),
        auditArchiveVerified: true, auditSummary: evidence.summary, generators: generatorEvidence, retained: true };
    // Missing descriptor/repository must fail even with a local fake archive directory available.
    const missing = path.join(scope, 'without-git');
    fs.mkdirSync(path.join(missing, 'Tests~/Legacy'), { recursive: true });
    for (const file of ['legacy-manifest.json', 'archive-descriptor.json']) fs.copyFileSync(path.join(root, 'Tests~', file), path.join(missing, 'Tests~', file));
    context.assert.throws(() => openLegacyArchive(missing), /Git read failed|requires this package Git/);
    const emptyRepository = await runProcess('git', ['init', '--quiet', missing], { timeoutMs: 10000 });
    context.assert.equal(emptyRepository.code, 0, emptyRepository.stderr || emptyRepository.error);
    context.assert.equal(emptyRepository.timedOut, false);
    context.assert.throws(() => openLegacyArchive(missing), /Git read failed/);
    const descriptor = path.join(missing, 'Tests~/archive-descriptor.json');
    fs.writeFileSync(descriptor, JSON.stringify({ ...JSON.parse(fs.readFileSync(descriptor)), commit: '0'.repeat(40) }));
    context.assert.throws(() => archiveMetadata(missing), /descriptor changed/);
    fs.writeFileSync(path.join(missing, 'Tests~/legacy-manifest.json'), '{}');
    context.assert.throws(() => archiveMetadata(missing), /manifest changed/);
});
await finish(context);
