import path from 'node:path';
import { setTimeout as pause } from 'node:timers/promises';

const canonical = value => {
    const full = path.resolve(value);
    return process.platform === 'win32' ? full.toLowerCase() : full;
};

// Read-only preflight only. A timed-out test/mutation must never enter this retry loop.
export function classifyEditorReadiness(reply, projectPath) {
    const result = (reason, retry, state = null) => ({ ready: reason === 'ready', reason, retry, state });
    if (reply?.timedOut) return result('status-timeout', true);
    let envelope;
    try { envelope = JSON.parse(reply?.stdout); }
    catch { return result('invalid-status-response', true); }
    if (reply.code !== 0 || envelope?.success !== true || envelope.data?.success !== true) {
        const errors = JSON.stringify(envelope?.errors ?? []);
        return result(/401|unauthorized|authentication/i.test(errors) ? 'authentication-failed' : 'connection-failed', true);
    }
    const state = envelope.data.result;
    const target = envelope.data.target?.projectPath;
    if (typeof state?.projectPath !== 'string' || !state.projectPath.trim()) return result('invalid-editor-state', true);
    if (canonical(state.projectPath) !== canonical(projectPath) ||
        target != null && (typeof target !== 'string' || canonical(target) !== canonical(projectPath)))
        return result('wrong-project', false);
    if (state.status === 'blocked_by_dialog' || state.dialog) return result('blocked-by-dialog', false, state);
    if (state.playMode === 'playing' || state.playMode === 'paused') return result('play-mode', false, state);
    if (state.compiling === true) return result('compiling', true, state);
    if (state.domainReloadInProgress === true) return result('importing-or-reloading', true, state);
    if (state.status === 'settling') return result('settling', true, state);
    if (state.status !== 'ready' || state.compiling !== false || state.domainReloadInProgress !== false || state.playMode !== 'stopped')
        return result('invalid-editor-state', true, state);
    return result('ready', false, state);
}

export async function waitForEditorReady(projectPath, invoke, delay = pause) {
    const attempts = [];
    for (let index = 0; index < 4; index++) {
        if (index) await delay(1000 * 2 ** (index - 1));
        // Each CLI process rediscovers the target endpoint/token from its project path.
        // Bound both the HTTP request and our child process; never start another Editor.
        let reply;
        try {
            reply = await invoke('unity', ['command', 'editor_status', '--project-path', projectPath,
                '--timeout', '6', '--format', 'json'], { cwd: projectPath, timeoutMs: 8000 });
        } catch {
            attempts.push({ attempt: index + 1, reason: 'probe-execution-failed' });
            return { ready: false, reason: 'probe-execution-failed', state: null, attempts };
        }
        const verdict = classifyEditorReadiness(reply, projectPath);
        attempts.push({ attempt: index + 1, reason: verdict.reason, exitCode: reply?.code ?? null,
            timedOut: reply?.timedOut === true, status: verdict.state?.status ?? null });
        if (verdict.ready || !verdict.retry || index === 3)
            return { ready: verdict.ready, reason: verdict.reason, state: verdict.state, attempts };
    }
}
