// Synthetic mock-invoker metadata only. These entries are never registered or
// dispatched to Unity; historical protocol tests must not read archived sources.
const common = {
    file: 'Tests~/Framework/Fixtures/HistoricalProtocol.mjs',
    args: [], category: 'runner', timeoutMs: 50000, effects: [],
    prerequisites: 'In-memory mock invoker; no Unity or archive dependency.',
    setup: 'Synthetic protocol responses only.', teardown: 'No persistent state.',
    groups: ['framework']
};

export const textCase = {
    ...common, id: 'fixture-text', runner: 'run_script', entry: 'Fixture.Pass',
    result: { kind: 'text', pass: '^PASS: runner protocol fixture$' }
};
export const asyncTextCase = {
    ...common, id: 'fixture-async-text', runner: 'run_script', entry: 'Fixture.Start',
    result: { kind: 'text', pass: '^Passed: runner async fixture$', pending: '^Running$' },
    async: { entry: 'Fixture.Result', args: [], started: '^Started; call Result\\.$', pollMs: 500 },
    cleanup: { entry: 'Fixture.Cleanup', args: [], pass: '^PASS: runner fixture cleanup$' }
};
export const exitCodeCase = {
    ...common, id: 'fixture-node-exit', runner: 'node', timeoutMs: 30000,
    result: { kind: 'exit-code' }
};

// Validate the historical protocol itself, separately from the structured-only
// active catalog. A dispatch rejection must not hide a broken regex contract.
export function validateHistoricalProtocol(scenario) {
    const validEntry = value => typeof value === 'string' && /^[\w.]+$/.test(value);
    const anchored = (pattern, label) => {
        if (typeof pattern !== 'string' || !pattern.startsWith('^')) throw Error('Missing anchored ' + label);
        new RegExp(pattern);
    };
    if (!['node', 'run_script', 'eval_file'].includes(scenario.runner)
        || !Array.isArray(scenario.args)) throw Error('Invalid invocation');
    if (scenario.runner === 'run_script' && !validEntry(scenario.entry)
        || scenario.runner === 'eval_file' && (scenario.entry || scenario.args.length)) throw Error('Invalid entry');
    if (scenario.runner === 'node' && scenario.result?.kind !== 'exit-code'
        || scenario.runner !== 'node' && !['text', 'json-success'].includes(scenario.result?.kind)) throw Error('Invalid result protocol');
    if (scenario.result.kind === 'text') anchored(scenario.result.pass, 'pass');
    if (scenario.async) {
        if (scenario.runner !== 'run_script' || !validEntry(scenario.async.entry)
            || !Array.isArray(scenario.async.args) || !Number.isSafeInteger(scenario.async.pollMs)
            || scenario.async.pollMs < 100 || scenario.async.pollMs > 5000) throw Error('Invalid async protocol');
        anchored(scenario.async.started, 'async start');
        anchored(scenario.result.pending, 'async pending');
    }
    if (scenario.cleanup) {
        if (scenario.runner !== 'run_script' || !validEntry(scenario.cleanup.entry)
            || !Array.isArray(scenario.cleanup.args)) throw Error('Invalid cleanup');
        anchored(scenario.cleanup.pass, 'cleanup');
    }
    return scenario;
}
