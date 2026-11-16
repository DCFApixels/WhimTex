import path from 'node:path';
import { TestContext, finish } from '../../Framework/test-api.mjs';
import { classifyEditorReadiness, waitForEditorReady } from '../../Framework/EditorReadiness.mjs';

const t = new TestContext('Editor preflight protocol: mocked transport; no Unity or mutation commands');
const project = path.resolve('test-project');
const ready = { projectPath: project, status: 'ready', compiling: false, domainReloadInProgress: false, playMode: 'stopped' };
const reply = (state = ready, target = project) => ({ code: 0, stdout: JSON.stringify({ success: true,
    data: { success: true, result: state, target: { projectPath: target } } }) });
const failed = message => ({ code: 6, stdout: JSON.stringify({ success: false, errors: [{ message }] }) });
async function sequence(responses) {
    const calls = [], waits = [];
    const result = await waitForEditorReady(project, async (binary, argv, options) => {
        calls.push({ binary, argv, options });
        return responses[Math.min(calls.length - 1, responses.length - 1)];
    }, async ms => { waits.push(ms); });
    for (const { binary, argv, options } of calls) {
        t.assert.equal(binary, 'unity');
        t.assert.deepEqual(argv.slice(0, 2), ['command', 'editor_status']);
        t.assert.equal(argv[argv.indexOf('--project-path') + 1], project);
        t.assert.equal(options.timeoutMs, 8000);
    }
    return { ...result, calls, waits };
}
t.case('healthy target uses one direct request', async () => {
    const result = await sequence([reply()]);
    t.assert.equal(result.ready, true); t.assert.equal(result.calls.length, 1); t.assert.deepEqual(result.waits, []);
});
t.case('reload and connection gap recover without user action', async () => {
    const result = await sequence([reply({ ...ready, status: 'compiling', compiling: true }), failed('Network error'), reply()]);
    t.assert.equal(result.ready, true);
    t.assert.deepEqual(result.attempts.map(x => x.reason), ['compiling', 'connection-failed', 'ready']);
    t.assert.deepEqual(result.waits, [1000, 2000]);
});
t.case('read-only timeout and rotated-token rejection retry', async () => {
    const result = await sequence([{ code: null, timedOut: true, stdout: '' }, failed('401 Unauthorized'), reply()]);
    t.assert.equal(result.ready, true);
    t.assert.deepEqual(result.attempts.map(x => x.reason), ['status-timeout', 'authentication-failed', 'ready']);
});
t.case('persistent failure has bounded attempts and a precise reason', async () => {
    const result = await sequence([failed('Network error')]);
    t.assert.equal(result.ready, false); t.assert.equal(result.reason, 'connection-failed');
    t.assert.equal(result.calls.length, 4); t.assert.deepEqual(result.waits, [1000, 2000, 4000]);
});
t.case('wrong project, modal dialog and play mode stop immediately', async () => {
    for (const [response, reason] of [
        [reply({ ...ready, projectPath: project + '-other' }), 'wrong-project'],
        [reply(ready, project + '-other'), 'wrong-project'],
        [reply({ ...ready, status: 'blocked_by_dialog' }), 'blocked-by-dialog'],
        [reply({ ...ready, status: 'playing', playMode: 'playing' }), 'play-mode']]) {
        const result = await sequence([response, reply()]);
        t.assert.equal(result.ready, false); t.assert.equal(result.reason, reason); t.assert.equal(result.calls.length, 1);
    }
});
t.case('unknown, malformed and partial states never establish readiness', () => {
    for (const response of [{ code: 0, stdout: '{}' }, { code: 0, stdout: '<html>' }, reply({ projectPath: project }),
        reply({ ...ready, status: 'unknown' }), reply({ ...ready, compiling: undefined }), reply({ ...ready, projectPath: '' })])
        t.assert.equal(classifyEditorReadiness(response, project).ready, false);
    t.assert.equal(classifyEditorReadiness(reply({ ...ready, domainReloadInProgress: true }), project).reason, 'importing-or-reloading');
    t.assert.equal(classifyEditorReadiness(reply({ ...ready, status: 'settling' }), project).reason, 'settling');
});
t.case('CLI launch failure is distinct from Editor unavailability', async () => {
    const result = await waitForEditorReady(project, async () => { throw Error('ENOENT'); });
    t.assert.equal(result.ready, false); t.assert.equal(result.reason, 'probe-execution-failed');
    t.assert.equal(result.attempts.length, 1);
});
await finish(t);
