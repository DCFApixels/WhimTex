import assert from 'node:assert/strict';

export const resultMarker = 'WHIMTEX_TEST_RESULT ';

// Cases execute serially; assertions retain node:assert messages and expected/actual values.
export class TestContext {
    constructor(message) {
        this.message = message;
        this.checks = 0;
        this.cases = [];
        this.facts = {};
        this.assert = new Proxy(assert, { get: (target, name) => {
            const value = target[name];
            return typeof value !== 'function' ? value : (...args) => { this.checks++; return value(...args); };
        }, apply: (target, receiver, args) => { this.checks++; return target(...args); } });
    }
    case(name, body) { this.cases.push({ name, body }); }
    async run() {
        const failures = [];
        const cases = [];
        for (const { name, body } of this.cases) {
            const before = this.checks;
            try {
                await body();
                if (this.checks === before) throw Error('No assertions were executed in case: ' + name);
                cases.push({ name, status: 'passed' });
            }
            catch (error) {
                failures.push({ case: name, message: String(error), stack: error.stack });
                cases.push({ name, status: 'failed' });
            }
        }
        if (!cases.length) failures.push({ message: 'No cases were registered.' });
        return { status: failures.length ? 'failed' : 'passed', checks: this.checks,
            message: this.message, failures, cases, facts: this.facts,
            ...(this.recoveryRequired === true ? { recoveryRequired: true } : {}) };
    }
}

export async function finish(context) {
    const result = await context.run();
    console.log(resultMarker + JSON.stringify(result));
    process.exitCode = result.status === 'passed' ? 0 : 1;
    return result;
}
