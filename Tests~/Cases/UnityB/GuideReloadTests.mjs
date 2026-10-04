import { finish } from '../../Framework/test-api.mjs';
import { createReloadContext, projectArgument } from './ReloadOrchestration.mjs';
await finish(createReloadContext('GuideReloadTests', projectArgument(process.argv.slice(2))));
