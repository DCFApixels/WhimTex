import { createReloadContext, projectArgument } from '../../Framework/Workflows/ReloadOrchestration.mjs';
import { finish } from '../../Framework/test-api.mjs';
await finish(createReloadContext('AgentReloadTests', projectArgument(process.argv.slice(2))));
