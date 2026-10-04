// CI entry for this explicitly reviewed READ-ONLY Node profile; no Unity/effect opt-in.
import fs from 'node:fs';
import path from 'node:path';
import { main, root, validateCatalog, selectScenarios, reviewFingerprint } from './run-tests.mjs';
const catalog = validateCatalog(JSON.parse(fs.readFileSync(path.join(root, 'Tests~/scripts/test-catalog.json'), 'utf8')));
const scenarios = selectScenarios(catalog, { profile: 'ci-docs' });
if (scenarios.some(s => s.runner !== 'node' || s.requiresUnity || s.effects.length)) throw Error('CI docs profile must remain read-only Node.');
process.exitCode = await main(['--run', '--profile', 'ci-docs', '--reviewed', reviewFingerprint(scenarios)]);
