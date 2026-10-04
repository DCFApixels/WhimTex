// Optional, explicitly selected independent decoder. Never installs a dependency.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { randomUUID, createHash } from 'node:crypto';
import { TestContext, finish, resultMarker } from '../../Framework/test-api.mjs';
import { root, runProcess, runScenario, commandArgs, bundleSources, classifyReply } from '../../scripts/run-tests.mjs';
const moduleName = process.env.WHIMTEX_PSD_READER;
const require = createRequire(new URL('./PsdReader.mjs', import.meta.url));
let modulePath;
try { modulePath = moduleName && require.resolve(moduleName); } catch { /* Explicit prerequisite below. */ }
if (!modulePath) {
    console.log(resultMarker + JSON.stringify({ status:'skipped', checks:0, failures:[],
        message:'Provide an already installed independent reader via WHIMTEX_PSD_READER (module ID or absolute path). No decoder coverage was executed.' }));
} else {
    const context = new TestContext('Encode current PSD and decode it with the independently installed reader');
    context.case('Original hierarchy/descriptor/RGBA/matte reader assertions', async () => {
        const project = path.resolve(process.argv[2] ?? '');
        context.assert.equal(project, path.resolve(root, '../..'), 'Explicit matching project');
        const directory = path.join(project, 'Temp/WhimTex/test-runs');
        context.assert.equal(JSON.parse(fs.readFileSync(path.join(directory, 'runner.lock'))).pid, process.ppid, 'Only the locked outer runner owns this workflow');
        const id = randomUUID(), source = bundleSources(['Tests~/Cases/Export/PsdWriter.cs', 'Tests~/Framework/TestApi.cs', 'src/PsdWriter.cs']);
        const file = path.join(directory, 'psd-reader-input-' + id + '.cs');
        fs.writeFileSync(file, source, { flag:'wx' });
        context.facts.input = { file, sha256:createHash('sha256').update(source).digest('hex') };
        context.facts.decoder = { modulePath, sha256:createHash('sha256').update(fs.readFileSync(modulePath)).digest('hex') };
        const producer = { id:'reader-owned-fixture', runner:'run_script', file:path.relative(root,file), entry:'PsdWriterTests.RunFixture',
            args:[id], timeoutMs:20000, result:{kind:'structured'} };
        const encoded = await runScenario(producer, (s,entry,args,budget) => runProcess('unity', commandArgs(s,entry,args,budget,project), { cwd:project,timeoutMs:budget }));
        context.facts.producer = encoded;
        if (encoded.uncertain) context.recoveryRequired = true;
        context.assert.equal(encoded.uncertain, false, 'Encoder operation is complete before decoding');
        context.assert.equal(encoded.status, 'passed', encoded.detail);
        const fixture = path.join(directory, 'psd-writer-' + id + '.psd');
        const reply = await runProcess(process.execPath, [path.join(root,'Tests~/Cases/Export/PsdReader.mjs'), modulePath, fixture], { timeoutMs:20000 });
        const decoded = classifyReply(reply, { runner:'node', result:{kind:'structured'} });
        context.facts.decoderResult = decoded; context.facts.fixture = fixture;
        context.assert.equal(decoded.status, 'passed', decoded.detail);
        context.assert.ok(decoded.testResult.checks > 0, 'Original external-decoder assertions actually ran');
        context.checks += decoded.testResult.checks;
    });
    await finish(context);
}
