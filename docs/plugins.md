# Plugins

How to write a Jason plugin: what a package is, every rule its manifest is held to, what the
JavaScript may do, and what the runtime does with the answer. A plugin is the only way a vendor tool
is ever called on your behalf, so this document is the whole contract.

Provider operations are **not routed yet**: nothing in the runtime invokes a plugin on its own, and a
`provider_op` work item still fails with `no_route` (see [docs/work-execution.md](work-execution.md)).
What exists today is everything around that — packages, validation, the registry, the invocation
mechanism — and it is worth writing against, because the package you write now is the package that
will be routed to.

## 1. What a plugin is

A plugin is a **stateless JavaScript package** that implements canonical operations against one
vendor tool. The runtime hands it an operation name, an input object and a context; it does the work
— usually by running the vendor's CLI or calling the vendor's HTTP API — and answers with a result or
a classified failure.

The runtime starts the shipped executable in plugin-host mode, which runs exactly **one invocation
and exits**: globals do not survive, there is no plugin-local state and no warm pool. The engine is
Jint, embedded in that host, so there is no Node, no npm install step and no native module — a
package is the JavaScript you ship in it. Official and community plugins use exactly this mechanism;
there is no private extension path. What is **not** here yet: routing a `provider_op` to a plugin;
per-operation schemas (an outcome is checked for shape and size only); `plugin install|update|remove`
(installing is copying a directory); signing and trust; invoking `kind: notification` plugins.

## 2. The package

A package is a directory under the plugins directory of the data area, named exactly as the plugin's
id:

```text
~/.jason/plugins/fake-provider/
├── plugin.yaml          # the manifest: identity, contracts, operations, capabilities, limits
├── main.js              # the entry module: export function invoke(operation, input, context)
├── modules/helper.js    # any local modules, imported by relative path
└── README.md            # anything else you ship; it is part of the package
```

The directory name **is** the id: a manifest whose `id` says something else is refused with
`id_mismatch`, which is also why two plugins can never share an id. A directory whose name starts
with `.` is not a candidate at all, and a directory without a `plugin.yaml` is skipped with
`manifest_missing` rather than treated as a broken plugin. `modules/` is a convention — any `.js` or
`.mjs` file under the package root can be imported by a relative specifier.

A package holds at most **1024 files** and **16 MiB** in total, hidden paths excluded; over either it
is refused with `package_too_large`, so hashing one stays cheap.

**The digest** is `sha256:` and 64 hex characters over a canonical stream: every file whose relative
path has no segment starting with `.`, sorted ordinally by its forward-slashed relative path, each
contributing its path, a zero byte, its decimal byte length, a zero byte, then its bytes. Line
endings are not normalised — the bytes that will run are the bytes that are hashed, so a digest is
the identity of *this* package on *this* machine. It is shown by `plugin list`, carried in every
invocation, and recomputed by the child before it runs a line of JavaScript.

## 3. The manifest

`plugin.yaml` is read as one YAML 1.2 document by the core schema: `yes` is the string "yes", `0644`
is the string "0644". Anchors and aliases, a duplicate key, a second document in the file and nesting
deeper than 32 levels are all refused as `yaml_invalid`. **Unknown keys are errors anywhere in the
file** (`unknown_field`): a typo is the common failure, and forward compatibility arrives with a new
`manifest_version`. Every rule is checked and every problem reported, so a file with three mistakes
is answered with three problems rather than one per reload.

```yaml
manifest_version: 1              # required; this runtime reads 1
id: fake-provider                # required; must equal the directory name
version: 1.0.0                   # required; SemVer core, optional prerelease, no build metadata
kind: provider                   # required; provider | notification
name: Fake provider              # optional
description: The test double every plugin-host test drives; copy its shape.
homepage: https://example.com    # optional; absolute https URL
contracts:
  protocol: [1]                  # required; plugin-host protocol versions this plugin speaks
  operations: [1]                # required for a provider; canonical-operation contract versions
operations: [echo.run, exec.run] # required non-empty for a provider; absent/empty for notification
entry:                           # optional; defaults to main.js / invoke
  module: main.js
  function: invoke
capabilities:                    # every section optional; absent means "not requested"
  exec:
    executables:                 # 1..16; the ONLY programs host.exec may ever start
      - name: dotnet             # bare file name, no path separators, no extension
        min_version: 10.0.0      # optional; needs a version_command
        version_command: ["--version"]
  http:
    hosts: ["api.example.com", "localhost:5555"]   # 1..32; exact match, no wildcards
  env:
    variables: [FAKE_TOKEN]      # 1..32; the variables host.env may read
limits:                          # optional; asks, never raises
  timeout_ms: 20000
  memory_mb: 64
```

| Field | Rule | Problem code |
|---|---|---|
| `manifest_version` | required; must be `1` | `field_required`, `manifest_version_unsupported` |
| `id` | required; `^[a-z][a-z0-9-]{1,63}$` (2–64 characters); must equal the directory name | `field_required`, `field_invalid`, `id_mismatch` |
| `version` | required; `major.minor.patch` with an optional `-prerelease`, no build metadata | `field_required`, `version_invalid` |
| `kind` | required; `provider` or `notification` | `field_required`, `kind_invalid` |
| `name` | optional text, at most 100 characters | `field_invalid` |
| `description` | optional text, at most 1000 characters | `field_invalid` |
| `homepage` | optional; absolute `https` URL | `field_invalid` |
| `contracts.protocol` | required non-empty list of whole numbers; must include `1` | `field_required`, `field_invalid`, `contract_unsupported` |
| `contracts.operations` | required for a provider, optional for a notification; must include `1` | `field_required`, `field_invalid`, `contract_unsupported` |
| `operations` | required non-empty for a provider; each `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$` — at least one dot — and unique | `field_required`, `field_invalid`, `operation_invalid`, `operation_duplicate` |
| `operations` on a `notification` | must be absent or empty | `operations_not_allowed` |
| `entry.module` | `.js` or `.mjs`, relative, no `..` segments, inside the package, must exist | `field_invalid`, `entry_module_outside_package`, `entry_module_missing` |
| `entry.function` | `^[A-Za-z_$][A-Za-z0-9_$]*$` | `entry_function_invalid` |
| `capabilities.exec.executables` | required inside `exec`; 1..16 entries | `field_required`, `field_invalid` |
| `…executables[i].name` | required; `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`, no separators, no duplicates | `field_required`, `field_invalid` |
| `…executables[i].min_version` | `major.minor.patch`; needs a `version_command` | `field_invalid` |
| `…executables[i].version_command` | exactly one of `["--version"]`, `["-v"]`, `["-V"]`, `["version"]` | `field_invalid` |
| `capabilities.http.hosts` | required inside `http`; 1..32 lowercase `host` or `host:port` (port 1–65535), exact, unique, no scheme, no path, no wildcards | `field_required`, `field_invalid`, `host_invalid` |
| `capabilities.env.variables` | required inside `env`; 1..32 names `^[A-Z][A-Z0-9_]{0,63}$`, unique | `field_required`, `field_invalid`, `variable_name_invalid` |
| a variable named `JASON_…` | never; the prefix is reserved for the runtime | `variable_reserved` |
| `limits.timeout_ms` | 1000 .. `Plugins:Limits:MaxTimeoutMs` (3 600 000 by default) | `field_invalid` |
| `limits.memory_mb` | 16 .. `Plugins:Limits:MaxMemoryMb` (512 by default) | `field_invalid` |

An invocation's effective timeout and memory are `min(what the manifest asks for, the installation's
ceiling)`, and a caller's own budget may lower the timeout further, never raise it. A manifest with
no `limits` gets `Plugins:Limits:TimeoutMs` (60 000 ms) and `Plugins:Limits:MemoryMb` (64 MiB).

`kind: notification` is accepted, validated, listed and granted like any other plugin, and refused by
the invoker with `plugin_kind_not_invocable`; its operation contract is not written yet.

**Why `version_command` is a fixed list.** A reload runs that command on the user's machine. If the
manifest could name the arguments, a version check would be a way to run anything.

## 4. Installing, reloading and listing

Installing a plugin is copying its directory into `~/.jason/plugins/` (override the data directory
with `JASON_DATA_DIR`). There are no install verbs in this version.

```sh
cp -r ./fake-provider ~/.jason/plugins/
jason plugin reload --reason "installed the fake provider"
jason plugin list --human
```

```text
ID             VERSION  KIND      STATUS  OPERATIONS            GRANTS                         DIGEST
-------------  -------  --------  ------  --------------------  -----------------------------  ------------
fake-provider  1.0.0    provider  valid   echo.run, exec.run …  exec 1/1 · http 0/2 · env 1/2  3f2a9c1b4d5e
```

`GRANTS` is granted-over-requested per capability, `-` where the manifest requested nothing; `DIGEST`
is the first 12 hex characters. Without `--human` the verb prints the API body, which carries all of
it in full:

```json
{
  "snapshot": { "id": "snp_01J4…", "loaded_at": "2026-09-14T12:00:00.000Z", "source": "reload", "plugin_count": 1 },
  "plugins": [
    { "id": "fake-provider", "version": "1.0.0", "kind": "provider", "name": "Fake provider",
      "description": "…", "homepage": null, "root": "<data dir>/plugins/fake-provider",
      "digest": "sha256:3f2a9c1b4d5e…", "contracts": { "protocol": [1], "operations": [1] },
      "operations": ["echo.run", "exec.run"], "entry": { "module": "main.js", "function": "invoke" },
      "capabilities": {
        "exec": { "requested": [ { "name": "dotnet", "path": "/usr/bin/dotnet", "version": "10.0.100", "min_version": null } ], "granted": ["dotnet"] },
        "http": { "requested": ["localhost:5555", "api.example.com"], "granted": [] },
        "env":  { "requested": ["FAKE_TOKEN", "OTHER_TOKEN"], "granted": ["FAKE_TOKEN"] } },
      "limits": { "timeout_ms": 20000, "memory_mb": 64 }, "status": "valid", "problems": [] } ],
  "last_reload": { "at": "2026-09-14T12:00:00.000Z", "source": "reload", "activated": true,
                   "candidates": [ { "directory": "fake-provider", "id": "fake-provider", "status": "valid", "problems": [] } ] },
  "activated": true
}
```

### Two kinds of problem

**Package problems** — anything about the files: YAML, fields, ids, versions, contracts, operations,
the entry module, capability declarations, size, readability. One of these in **any** candidate
rejects the whole reload: the previous snapshot stays active, `plugin reload` answers
`409 plugin_reload_rejected` (`retryable: false`) with one detail per problem and exits 1, and
`plugin list` keeps showing the old snapshot with `"activated": false` on the root and the same
problems under `last_reload`.

**Environmental problems** — `executable_missing`, `executable_not_runnable`,
`executable_incompatible`, `executable_version_check_failed` — are about the machine rather than the
package. They make that one plugin `status: unavailable` with its problems listed, and change nothing
else: the set still activates and the other plugins stay usable. An autostarted runtime sees a
different search path than a shell does, and one uninstalled vendor tool must not empty the registry.

A problem's `path` is the place inside the manifest — `kind`, `capabilities.http.hosts[1]`,
`capabilities.exec.executables[0].name` — or `(root)` when the document is not a mapping,
`plugin.yaml#<line>:<col>` for a YAML syntax error, `plugin.yaml` for `manifest_missing`, `(package)`
for a package-level problem, `grants.http[0]` for a grant warning. In the 409 details and in
`--human` output the file is named exactly once: `fake-provider/plugin.yaml#kind`.

### At startup, and the journal

The load at startup follows exactly the same path. If the candidate set has a package problem the
runtime **still starts**, with an empty active snapshot and the diagnostics on record — a broken
plugin must never stand between you and your campaigns, and an empty registry is honest rather than
half-working. `jason runtime status --human` says which of the two it is:

```text
Plugins:    1 active · snapshot snp_01J4… · loaded 2026-09-14 12:00:00 UTC
Plugins:    none active · last reload rejected
```

An **activated** load writes one global journal entry of the reserved kind `plugins_reloaded`
carrying the snapshot id, its source and `{id, version, digest, status}` per plugin — actor `system`
for the load at startup, the request's actor and reason for a reload. A **rejected** load writes
none; it is visible in the log and in `last_reload`. `journal.append` refuses the kind, so nothing
but a real activation can write one.

## 5. Writing `invoke`

`main.js` is an ES module. The entry is the named export the manifest asks for (`invoke` by default),
falling back to the module's `default` export; if neither is callable the invocation fails
`permanent` with `entry_function_missing`.

```js
import { helper } from "./modules/helper.js";

export function invoke(operation, input, context) {
  switch (operation) {
    case "echo.run":
      return { result: { echo: input, helped: helper(input.value) } };

    case "contact.create": {
      const answer = host.exec({ executable: "reply", args: ["contact", "create", "--json"], stdin: JSON.stringify(input) });
      if (answer.exit_code !== 0) {
        throw host.fail({ class: "transient", code: "cli_failed", message: answer.stderr.slice(0, 500) });
      }

      const created = JSON.parse(answer.stdout);
      return { result: { id: created.id }, external_ids: { contact: created.id } };
    }

    default:
      throw host.fail({ class: "validation", code: "unknown_operation", message: "unknown operation " + operation });
  }
}
```

**The return contract.** `invoke` returns `{ result, external_ids? }`, or a Promise of it. `result`
is any JSON value up to 1 MiB (`result_too_large` above that); `external_ids` is an object of at most
64 string values of at most 256 characters, keyed by the **provider's** own names, which travel
verbatim. Anything else — a bare value, `undefined`, an array, an object without `result` — fails
`permanent` with `bad_return`.

**Failing.** `throw host.fail({ class, code, message, details?, external_ids? })` is the only way to
report a business failure with its class. Anything else thrown fails `permanent` with
`plugin_exception`, keeping the message and the JavaScript stack in `details.stack`.

**Promises.** `async function invoke` works: the host drains the job queue and unwraps the promise
within the budget. The five `host.*` functions are synchronous — they block until the program or the
request finishes — so there is nothing to `await` on them.

**Modules.** Only relative specifiers ending in `.js` or `.mjs`, only inside the package: `./x.js`,
`../shared/y.mjs`. A bare specifier (`lodash`), an absolute path, a URL, a `.json` import or a path
resolving outside the package root is refused with `module_not_allowed`; at most 256 modules per
invocation, each at most 1 MiB. **There is no `console`, no `require`, no `fetch`, no filesystem and
no CLR**; `eval` and `new Function` do not compile. Everything a plugin can reach is on `host`.

**`context`** is the third argument:

```json
{ "invocation_id": "pin_01J4…", "correlation_id": "att_01J4…",
  "attempt_id": null, "attempt_number": null, "work_item_id": null, "campaign_id": null,
  "binding": null, "timeout_ms": 20000, "deadline": "2026-09-14T12:00:20.000Z",
  "plugin": { "id": "fake-provider", "version": "1.0.0" }, "runtime_version": "0.1.0" }
```

`binding` is the caller's opaque, non-secret note to the plugin (at most 64 KiB); `attempt_id`,
`attempt_number`, `work_item_id` and `campaign_id` are filled once provider operations are routed —
`attempt_number` is what tells a plugin it is being repeated. No secret is ever in `context`.

## 6. The Host SDK

`host` is a plain JavaScript object with exactly five read-only functions, closed to extension. It is
not a wrapped CLR instance, so there is no member surface beyond those five and no type to walk back
into the host program. Every function takes **one options object** and refuses an unknown key with a
`TypeError` you can catch: `timeoutMs` next to `timeout_ms` would otherwise make a capped call look
uncapped. A key whose value is `undefined` counts as absent.

Calling a capability the user did not grant is **not** a catchable error: the whole invocation fails
`permanent` with `capability_not_granted` and `details: { capability, requested }`, because a plugin
must not be able to probe its grants and quietly fall back to something else.

### `host.exec`

```js
const answer = host.exec({ executable: "reply", args: ["--version"], stdin: null, timeout_ms: 5000, env: { LANG: "C" } });
// → { exit_code: 0, stdout: "…", stderr: "", truncated: { stdout: false, stderr: false }, timed_out: false, duration_ms: 42 }
```

`executable` is a **name** from the granted list, started at the path the runtime resolved at reload.
There is no path lookup in the child, absolute paths are not accepted, and nothing is ever handed to
a shell: arguments go through an argument array, one at a time. A name that is not granted — or any
name containing `/`, `\` or `:` — fails with `executable_not_allowed`.

| Key | Rule |
|---|---|
| `executable` | required string, at most 256 characters, a granted name |
| `args` | at most 256 strings, at most 1 MiB together |
| `stdin` | string of at most 1 MiB, written and then closed |
| `timeout_ms` | 1 .. the invocation's `timeout_ms`, then clamped to what is left of the budget; default: what is left |
| `env` | at most 32 entries, names `^[A-Za-z_][A-Za-z0-9_]*$`, never `JASON_*`, values at most 4096 bytes, for this child only |

The child's working directory is the invocation directory, and it inherits the plugin host's own
environment — the base set below plus the granted variables — with `env` added on top. `stdout` and
`stderr` are captured up to `Plugins:Exec:OutputBytes` (4 MiB) each, the first bytes kept and the
crossing flagged in `truncated`. On a timeout the whole process tree is killed and the call
**returns** with `timed_out: true` rather than throwing — the plugin decides what that means, usually
`ambiguous`. A granted program that will not start at all is an answer too: `exit_code: -1` with the
reason in `stderr`, and an `exec_launch_failed` line on stderr. At most `Plugins:Exec:MaxCalls` (64)
calls per invocation; the next fails `exec_limit`. Every call is logged as one `exec` line:
executable, arguments, exit code, duration, timed out, truncated.

### `host.http`

```js
const response = host.http({ method: "POST", url: "https://api.example.com/v1/contacts",
                             headers: { "Content-Type": "application/json", Authorization: "Bearer " + host.env("FAKE_TOKEN") },
                             body: JSON.stringify(input), timeout_ms: 10000 });
// → { status: 201, headers: { "content-type": "application/json" }, body: "…", truncated: false, duration_ms: 118 }
```

| Rule | Code when broken |
|---|---|
| `method` is `GET` or `POST` | `http_method_not_allowed` |
| `url` is absolute, at most 4096 characters | a `TypeError` |
| the scheme is `https`, or `http` only to `localhost`, `127.0.0.1` or `::1` | `http_scheme_not_allowed` |
| the host — with its port when it is not the scheme's default — is in the **granted** hosts, compared case-insensitively, no wildcards | `http_host_not_allowed` |
| `headers`: at most 32 entries, values at most 8 KiB; `Host`, `Content-Length`, `Transfer-Encoding` and `Connection` belong to the connection | `http_header_not_allowed` |
| `body` is a string of at most `Plugins:Http:RequestBytes` (1 MiB) | `http_limit` |
| at most `Plugins:Http:MaxCalls` (64) requests per invocation | `http_limit` |

The URL is parsed **before** the grant check, so a malformed URL is a `TypeError` whether or not the
capability was granted. `timeout_ms` accepts 1 .. the invocation's `timeout_ms`, defaults to
`Plugins:Http:TimeoutMs` (30 000) and is clamped to the remaining budget. The response body is read
as UTF-8 text up to `Plugins:Http:ResponseBytes` (4 MiB) with `truncated` flagged; header names come
back lowercased.

**Redirects are never followed** — a 3xx is returned to the plugin, and a plugin that wants to follow
it issues a new request that passes the same allowlist, which makes "no cross-host redirects" true by
construction. No cookies. The only header the host adds is `User-Agent: jason-plugin-host/<version>`;
no credential is ever injected. A request that never completed comes back as
`{ status: 0, body: "", error: "<why>" }` rather than throwing, with an `http_failed` line on stderr.
Each request is logged as an `http` line with method, scheme, host, path, status and duration — never
the query string, never headers, never bodies.

### `host.env`

```js
const token = host.env("FAKE_TOKEN");   // a string, or undefined
```

The value of a variable that is **both** declared in the manifest and granted by the user, read from
the child's own environment. Anything else — an undeclared name, a declared but ungranted one, a
`JASON_*` name whatever a grant says — is `undefined`, and the attempt is written to stderr as an
`env_probe` line naming the variable and never a value, so a plugin probing for what it was not given
is visible. Values never travel in the envelope; they reach the child by name.

### `host.log`

```js
host.log("info", "enrolled the contact", { contact: created.id });
```

`level` is `debug`, `info`, `warn` or `error`; `message` is a string; `data` is any JSON. One JSON
line on stderr. Before anything is written, every granted variable's value of at least 8 characters
is replaced by `[redacted]` in both the message and the serialised data. The message keeps its first
4096 characters and `data` its first `Plugins:Invoker:LogLineBytes` (16 KiB), with `"truncated": true`
on the line; after `Plugins:Invoker:StderrBytes` (4 MiB) in one invocation, a final `log_truncated`
line is written and the log goes silent.

### `host.fail`

```js
throw host.fail({ class: "transient", code: "rate_limited", message: "429 from the provider",
                  details: { retry_after: 30 }, external_ids: { contact: "r_123" } });
```

`class` is one of `transient`, `permanent`, `validation`, `ambiguous`. `code` is your own vocabulary,
lower snake_case, at most 64 characters; `message` at most 2000 characters; `details` any JSON, which
`host.fail` refuses with a `TypeError` over 64 KiB and the host drops to `null` if it is over the cap
when the thrown failure is read back — losing the details is better than losing the failure.
`external_ids` follows the same rule as on a success. The function returns an ordinary JavaScript
`Error` for you to throw, so the failure travels the language's own way: out through `finally`
blocks, and out of a rejected promise. The message and the details are redacted on the way out, the
same way a log line is.

### The environment a child sees

The plugin-host process — and therefore every program `host.exec` starts — is given an environment
**built from nothing**, not inherited. Copied when present in the runtime's own environment:

- always: `PATH`, `DOTNET_ROOT`, `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`, `http_proxy`,
  `https_proxy`, `no_proxy`;
- on Windows: `PATHEXT`, `SystemRoot`, `SystemDrive`, `windir`, `ComSpec`, `TEMP`, `TMP`,
  `USERPROFILE`, `HOMEDRIVE`, `HOMEPATH`, `APPDATA`, `LOCALAPPDATA`, `ProgramData`, `ProgramFiles`,
  `ProgramFiles(x86)`, `ProgramW6432`, `NUMBER_OF_PROCESSORS`, `PROCESSOR_ARCHITECTURE`, `OS`,
  `USERNAME`;
- on everything else: `HOME`, `TMPDIR`, `LANG`, `LC_ALL`, `LC_CTYPE`, `USER`, `LOGNAME`, `SHELL`,
  `TERM`;
- plus the granted `env` variables, by name.

Never anything else, and **never a `JASON_*` variable** — not even `JASON_DATA_DIR`. A plugin that
dumps its own environment learns nothing about the runtime that started it, and cannot locate the
database.

## 7. Failure classes

Every failure carries one of four classes, and the class — not the code — is what the runtime reads.

| Class | Meaning | What the runtime does |
|---|---|---|
| `transient` | it may work next time: a 429, a connection reset, a busy provider | retried, within the work item's attempt budget |
| `permanent` | it would fail the same way again: a bad request, a missing resource, a bug | final |
| `validation` | the input was wrong | final |
| `ambiguous` | **it may already have happened** at the provider | final in this version |

`ambiguous` is deliberately not retried: a blind repeat could send the same message twice, and that
is the user's reputation rather than a retry budget. Such an item waits for a human or a manager role
until a recovery read exists that can check what really happened.

**Protocol failures** mean the runtime never got an answer at all. They are classified by how much
may already have run.

| Code | Class | When |
|---|---|---|
| `plugin_launch_failed` | transient | the child could not be started at all |
| `plugin_not_loaded` | permanent | no plugin of that id is in the active snapshot |
| `plugin_unavailable` | permanent | the plugin is loaded but its environment is not usable |
| `plugin_operation_unsupported` | permanent | the plugin does not implement the operation |
| `plugin_kind_not_invocable` | permanent | a `notification` plugin was asked for an operation |
| `plugin_invocation_rejected` | permanent | the child refused before any JavaScript ran (exit 2 or 3) |
| `plugin_input_too_large` | permanent | the input is over 1 MiB, so nothing was started |
| `plugin_binding_too_large` | permanent | the binding is over 64 KiB, so nothing was started |
| `plugin_timeout` | ambiguous | the child did not exit in time and its tree was killed |
| `plugin_killed` | ambiguous | the caller cancelled and the tree was killed |
| `plugin_no_outcome` | ambiguous | the child exited without writing an outcome, a host failure included |
| `plugin_malformed_outcome` | ambiguous | what it wrote is not a valid outcome for this invocation |
| `plugin_output_too_large` | ambiguous | it wrote more to stdout than the invoker will read |

An unknown protocol code is ambiguous: the safe assumption about an unknown failure is that it may
have done something.

**`plugin_timeout` has two shapes and one code.** When the engine's budget runs out inside
JavaScript, the **child** ends the invocation and writes a normal `failed` outcome with that code,
class `permanent` if no `host.exec` or `host.http` call had been started and `ambiguous` if one had —
the host knows, and the plugin never has to reason about it. When the child itself hangs past the
budget and the grace period, the **runtime** kills the tree and reports the protocol failure
`plugin_timeout`, which is always ambiguous.

The other codes the host itself writes into a failed outcome — as opposed to your own vocabulary —
are `plugin_exception`, `bad_return`, `result_too_large`, `entry_function_missing`,
`plugin_syntax_error`, `plugin_memory_exceeded`, `plugin_statement_limit`, `plugin_recursion_limit`,
`capability_not_granted`, `executable_not_allowed`, `exec_limit`, `http_host_not_allowed`,
`http_scheme_not_allowed`, `http_method_not_allowed`, `http_header_not_allowed`, `http_limit` and
`module_not_allowed`. All of them are `permanent`.

## 8. Capabilities and grants

**Declaring a capability is not being given it.** A manifest says what a plugin wants; the user says
what it gets, in `~/.jason/config/settings.json`:

```json
{
  "Plugins": {
    "Grants": {
      "fake-provider": { "Exec": ["*"], "Http": ["api.example.com"], "Env": ["FAKE_TOKEN"] }
    }
  }
}
```

Three lists per plugin id, each naming things **out of what that plugin's manifest requested**. `*`
grants everything requested for that capability. An absent list, an absent plugin, or a freshly
copied package grants nothing — the default is nothing, and a plugin with no grants still loads as
`valid`. An entry the manifest never requested grants nothing and is reported as a
`grant_unrequested` warning at `grants.<capability>[i]`; it does not make the plugin unavailable.
Grants are resolved per plugin at load time and frozen into the snapshot, so a change takes effect at
the next `jason plugin reload`, and `plugin list` always shows requested next to granted.

Two consequences worth knowing. A capability the manifest never requested is **absent** from the
invocation rather than empty, which is a different thing from one that is present and always says no.
And the version check of a declared executable runs only for an executable the user **granted** — a
reload must never start a program nobody consented to; an ungranted one is resolved for presence only
and lists `"version": null`.

## 9. The protocol

The runtime starts the shipped executable in plugin-host mode, and you can do the same by hand (§11):

```text
jason plugin-host --protocol 1 --plugin <id> --operation <op> --correlation <id>
```

All four are required, order does not matter, and nothing else is accepted. They are public values a
process listing may show, and they are repeated inside the envelope — the child refuses a
disagreement. Everything else, the input, the binding and the resolved grants included, is on stdin.

### The envelope: stdin, one JSON object, then end of file

```json
{
  "protocol_version": 1,
  "invocation_id": "pin_01J4…",
  "correlation_id": "att_01J4…",
  "plugin": { "id": "fake-provider", "version": "1.0.0", "kind": "provider",
              "root": "<data dir>/plugins/fake-provider", "digest": "sha256:3f2a…",
              "entry": { "module": "main.js", "function": "invoke" } },
  "operation": "echo.run",
  "operation_contract_version": 1,
  "input": { "…": "the operation's arguments, at most 1 MiB" },
  "context": { "binding": null, "attempt_id": null, "attempt_number": null,
               "work_item_id": null, "campaign_id": null, "runtime_version": "0.1.0" },
  "grants": { "exec": { "executables": [ { "name": "dotnet", "path": "/usr/bin/dotnet" } ] },
              "http": { "hosts": ["api.example.com"] },
              "env": { "variables": ["FAKE_TOKEN"] } },
  "limits": { "timeout_ms": 20000, "memory_bytes": 67108864, "max_statements": 10000000, "max_recursion": 64,
              "exec": { "output_bytes": 4194304, "max_calls": 64 },
              "http": { "response_bytes": 4194304, "request_bytes": 1048576, "max_calls": 64, "timeout_ms": 30000 },
              "log": { "line_bytes": 16384, "total_bytes": 4194304 } }
}
```

The grants here are the **resolved policy** — what the user granted out of what the manifest
requested — so the child enforces what the runtime decided and has no configuration of its own. No
secret value is in the envelope. A capability the manifest never requested is `null` here.

### The outcome: stdout, exactly one JSON object

```json
{ "protocol_version": 1, "invocation_id": "pin_01J4…", "status": "succeeded",
  "result": { "echo": { "a": 1 } }, "external_ids": { "contact": "r_123" }, "error": null,
  "diagnostics": { "duration_ms": 412, "exec_calls": 1, "http_calls": 0, "log_lines": 3 } }
```

```json
{ "protocol_version": 1, "invocation_id": "pin_01J4…", "status": "failed",
  "result": null, "external_ids": null,
  "error": { "class": "transient", "code": "rate_limited", "message": "429 from the provider",
             "details": { "retry_after": 30 } },
  "diagnostics": { "duration_ms": 118, "exec_calls": 0, "http_calls": 1, "log_lines": 0 } }
```

Stdout belongs to the host alone — there is no `console` — so nothing a plugin writes can corrupt the
channel. The runtime re-validates the outcome after the child exits: exactly one JSON document, this
invocation's id, `protocol_version` 1, a known status, an error exactly when the status is `failed`,
a result of at most 1 MiB, `details` of at most 64 KiB, a lower snake_case code of at most 64
characters, a message of at most 2000 characters, `external_ids` of at most 64 string values of at
most 256 characters. Anything else is `plugin_malformed_outcome`. Canonical result schemas do not
exist yet, so a well-formed outcome whose result is nonsense for the operation still passes.

### stderr: JSON Lines

```json
{"ts":"2026-09-14T12:00:00.123Z","level":"info","source":"host","plugin":"fake-provider","invocation_id":"pin_01J4…","message":"exec","data":{"executable":"dotnet","args":["--version"],"exit_code":0,"timed_out":false,"duration_ms":42,"truncated":false}}
```

`source` is `host` or `plugin`; `data` and `truncated` appear only when they apply. The host's own
messages are `invocation_rejected`, `digest_unverified`, `env_probe`, `exec`, `exec_launch_failed`,
`http`, `http_failed` and `log_truncated`. The runtime copies the whole stream, redacted, to
`stderr.log` in the invocation directory and keeps the last 4096 characters as the trace that travels
with a protocol failure.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | the protocol completed and an outcome was written — succeeded **or** failed |
| `2` | usage: the command line was not one this build accepts, or the protocol version is unsupported. One line on stderr, nothing on stdout |
| `3` | the invocation was refused before any JavaScript ran |
| `4` | the host itself broke. A plain-text stack on stderr, and never a business answer |

Everything that happens inside or because of the plugin's code — an engine limit, a thrown error, a
refused capability — is a **failed outcome with exit 0**, because the protocol did complete and the
business answer is "it failed, here is why".

Exit 3 carries one of eight rejection codes on stderr: `envelope_unreadable`, `envelope_invalid`,
`argv_mismatch`, `protocol_unsupported`, `package_root_missing`, `package_unreadable`,
`digest_mismatch` and `entry_module_missing`. The runtime reports exit 2 and exit 3 alike as
`plugin_invocation_rejected`, quoting the host's own last message; exit 4 becomes
`plugin_no_outcome`.

## 10. Isolation, honestly

Isolation here is **process isolation plus interpreter isolation**, and that is the whole claim.

What is prevented: the plugin cannot reach the runtime's memory, its database, its API or its
capability token, because it runs in a different process that was never told any of them; its engine
has no CLR access, no `require`, no filesystem, no `eval` and no `new Function`; its modules cannot
leave the package root; the programs it may start are a fixed list of names the runtime resolved, run
through an argument array and never a shell; its HTTP is two methods to an exact allowlist with no
redirects and no injected credentials; its environment is built from nothing and holds no `JASON_*`
name; and memory, statements, recursion and wall-clock time are all bounded, with the whole process
tree killed when the budget runs out.

What is **not** prevented: this is not a container, a virtual machine or a kernel-level isolation
boundary — it is not a sandbox, and the documentation will not call it one. The plugin host runs
under your own operating-system identity with your own permissions. A granted program can do whatever
that program can do, and a granted host can be sent whatever the plugin wants to send it. Plugin
packages are **executable local code that you installed**, and the grants are the real boundary —
which is why the default is that nothing is granted.

Signing, publisher identity, trust levels, install-time capability review and operating-system
resource control are deliberately not in this version. Read a package before you install it, and
grant it the narrowest set of executables, hosts and variables that lets it do its job.

## 11. Testing a plugin locally

**Through the runtime.** Copy the package, reload, read the diagnostics. A rejected reload prints
every problem with its location and code, and changes nothing.

```sh
cp -r ./my-plugin ~/.jason/plugins/
jason plugin reload --human        # exit 1 and the 409 body if any package is broken
jason plugin list --human          # status, granted-over-requested, digest
```

**By hand.** The plugin host is a mode of the same executable and the envelope is public, so you can
drive one invocation yourself without going near the runtime's state:

```sh
cat invocation.json | jason plugin-host --protocol 1 --plugin my-plugin --operation echo.run --correlation dev
```

Write `invocation.json` to the shape in §9, with `"root"` pointing at your package directory. A
hand-written envelope may set `"digest": null` — the host then skips the verification, runs the
package as it is on disk, and says so with a `digest_unverified` line on stderr. The runtime always
sends a digest, so this is strictly a development path. Export the granted variables in your shell
first, since `host.env` reads the process's own environment. The outcome comes back on stdout as one
JSON object, the diagnostics on stderr as JSON Lines, and the exit code says whether the protocol
completed.

**The shape to copy.** `runtime/tests/fixtures/plugins/fake-provider/` in this repository is a
complete, valid package — manifest, `main.js`, and a local module under `modules/` — that exercises
every SDK function and every kind of failure. It is a test fixture rather than a marketplace plugin,
and it is the fastest way to see a working one.

## 12. Known limitations of this version

- **Nothing routes to a plugin yet.** `provider_op` work items fail with `no_route`; routing,
  bindings and invocation pinning arrive with the next increment.
- **Windows: only `.exe` and `.com` can be declared.** A bare name that resolves only to a `.cmd`,
  `.bat` or `.ps1` is `executable_not_runnable`, because starting one means `cmd.exe` interprets the
  arguments — the shell the argument-array rule exists to exclude. A Node-based CLI installed through
  npm resolves to a `.cmd` shim and so needs a native executable today; resolving a shim into its
  interpreter and entry script is a possible future step inside the executable resolver.
- **The outcome is checked for shape and size only**, until per-operation schemas exist.
- **Proxies come from the base environment.** `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY` and their
  lowercase forms are passed through and honoured; there is no proxy configuration of Jason's own and
  no custom TLS.
- **`host.http` is deliberately narrow**: `GET` and `POST`, exact hosts with no wildcards, text
  bodies only — no binary, no streaming — no automatic redirects, no cookies, no injected
  credentials.
- **Invocation directories are kept.** `~/.jason/work/plugins/<invocation_id>/` holds `stderr.log`
  and nothing else, and nothing is cleaned up in this version.
- **One installed version per id**, because the id is the directory name. Two plugins may implement
  the same operation; which one is chosen is a routing question, and routing is not here yet.
- **`kind: notification` is reserved**: such a plugin loads, lists and is granted like any other, and
  the invoker refuses it with `plugin_kind_not_invocable`.
- **No install verbs, no signing, no trust model.** Installing is copying a directory.

The design behind all of this — the plugin model, the trust boundary, and what is deliberately left
open — is in [docs/architecture.md](architecture.md), §14 and §16.5. How work items reach an
executor, and where provider operations will plug in, is in
[docs/work-execution.md](work-execution.md).
