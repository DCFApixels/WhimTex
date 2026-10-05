import { finish } from '../../Framework/test-api.mjs';
import { createReloadContext, projectArgument } from '../../Framework/Workflows/ReloadOrchestration.mjs';
await finish(createReloadContext('DocumentReloadTests', projectArgument(process.argv.slice(2))));
