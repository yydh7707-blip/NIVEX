import { readConfig, type WorkerEnv } from "./config.js";
import { createD1Db } from "./d1.js";
import { handleRequest, handleScheduled } from "./app.js";
import { createTurnstileVerifier } from "./turnstile.js";

function deps(env: WorkerEnv) {
  const config = readConfig(env);
  return {
    db: createD1Db(env.DB),
    config,
    turnstile: createTurnstileVerifier(config),
    now: () => new Date(),
    assets: env.ASSETS ? (request: Request) => env.ASSETS!.fetch(request) : undefined,
  };
}

export default {
  fetch(request: Request, env: WorkerEnv): Promise<Response> {
    return handleRequest(request, deps(env));
  },
  scheduled(_controller: unknown, env: WorkerEnv): Promise<void> {
    return handleScheduled(deps(env));
  },
};
