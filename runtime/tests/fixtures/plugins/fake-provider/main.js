// The plugin every test drives, and the shape a real plugin copies: one ES module exporting one function, which
// is handed the operation, its input and the context, and answers with { result } or throws host.fail({...}).
// There is no console, no require and no filesystem here — everything a plugin can reach is on `host`.
import { helper } from "./modules/helper.js";

// The SDK reads a request object strictly, so a key whose value is undefined is dropped rather than passed on.
function compact(request) {
  const cleaned = {};
  for (const key of Object.keys(request)) {
    if (request[key] !== undefined) {
      cleaned[key] = request[key];
    }
  }

  return cleaned;
}

function probe(attempt) {
  try {
    return { reached: attempt() };
  } catch (error) {
    return { error: error.name };
  }
}

export function invoke(operation, input, context) {
  if (operation.startsWith("fail.")) {
    throw host.fail(compact({
      class: operation.slice(5),
      code: input.code ?? "provoked",
      message: input.message ?? "provoked failure",
      details: input.details,
      external_ids: input.external_ids,
    }));
  }

  switch (operation) {
    case "echo.run":
      return { result: { echo: input } };

    case "context.echo":
      return { result: context };

    case "exec.run":
      return {
        result: host.exec(compact({
          executable: input.executable,
          args: input.args ?? [],
          stdin: input.stdin,
          timeout_ms: input.timeout_ms,
          env: input.env,
        })),
      };

    case "http.get":
      return {
        result: host.http(compact({
          method: input.method ?? "GET",
          url: input.url,
          headers: input.headers,
          body: input.body,
          timeout_ms: input.timeout_ms,
        })),
      };

    case "env.read":
      return { result: { value: host.env(input.name) ?? null } };

    case "log.emit":
      host.log(input.level ?? "info", input.message, input.data);
      return { result: { logged: true } };

    case "hang":
      for (;;) {
        // Never ends: the engine's own limits are what stop this.
      }

    case "spew":
      return { result: "x".repeat(input.bytes) };

    case "throw.plain":
      throw new Error("boom");

    case "return.bare":
      return 42;

    case "escape.clr":
      return {
        result: {
          system: probe(() => typeof System !== "undefined" && System.IO.File.Exists("x")),
          import_namespace: probe(() => typeof importNamespace("System") !== "undefined"),
          clr: probe(() => typeof clr !== "undefined"),
        },
      };

    case "escape.eval":
      return { result: probe(() => eval("1+1") === 2) };

    case "escape.function":
      return { result: probe(() => new Function("return 1")() === 1) };

    case "escape.constructor":
      return { result: probe(() => typeof ({}).constructor.constructor("return this")()) };

    case "module.import":
      return { result: { helper: helper(input.value) } };

    default:
      throw host.fail({
        class: "validation",
        code: "unknown_operation",
        message: "unknown operation " + operation,
      });
  }
}
