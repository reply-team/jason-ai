# Plugins

How to write a Jason plugin: what a package is, every rule its manifest is held to, what the
JavaScript may do, and what the runtime does with the answer. A plugin is the only way a vendor tool
is ever called on your behalf, so this document is the whole contract.

A `provider_op` work item reaches a plugin because a **route** sends it there: which plugin performs an
operation for a campaign, and which account of that plugin it works in, is configured rather than
built in, and never guessed. That side of it belongs to whoever operates an installation, with
`jason route set`; this document is the author's guide.

## 1. What a plugin is

A plugin is a **stateless JavaScript package** that implements canonical operations against one
vendor tool. The runtime hands it an operation name, an input object and a context; it does the work
— usually by running the vendor's CLI or calling the vendor's HTTP API — and answers with a result or
a classified failure.

The runtime starts the shipped executable in plugin-host mode, which runs exactly **one invocation
and exits**: globals do not survive, there is no plugin-local state and no warm pool. The engine is
Jint, embedded in that host, so there is no Node, no npm install step and no native module — a
package is the JavaScript you ship in it. Official and community plugins use exactly this mechanism;
there is no private extension path. What is **not** here yet: `plugin install|update|remove`
(installing is copying a directory); signing and trust.

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

`plugin.yaml` is read as one YAML 1.2 document by the core schema: `yes` is the string "yes", and a
leading zero means nothing — `0644` is the integer 644, while `0o644` is octal and `0x1a4` is
hexadecimal. Anchors and aliases, a duplicate key, a second document in the file and nesting
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
  events: [1]                    # optional, a provider only: the events contract versions it reads accounts by
operations: [echo.run, exec.run] # required non-empty for a provider; absent/empty for notification
entry:                           # optional; defaults to main.js / invoke
  module: main.js
  function: invoke
capabilities:                    # every section optional; absent means "not requested"
  exec:
    executables:                 # 1..16; the ONLY programs host.exec may ever start
      - name: provider-cli       # the vendor program itself, by its own name: a bare file name, no
                                 # path separators, no extension. Never a language runtime — granting
                                 # one of those reaches every program it can be handed.
        min_version: 2.4.0       # optional; needs a version_command
        version_command: ["--version"]
  http:
    hosts: ["api.example.com", "localhost:5555"]   # 1..32; exact match, no wildcards
  env:
    variables: [FAKE_TOKEN]      # 1..32; the variables host.env may read
limits:                          # optional; asks, never raises
  timeout_ms: 20000
  memory_mb: 64
binding:                         # optional; a schema, in the published dialect, that a route must satisfy
  type: object
  additionalProperties: false
  required: [workspace]
  properties:
    workspace: { type: string }
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
| `contracts.events` | optional, and only on a provider; when present, must include `1` | `field_invalid`, `contract_unsupported`, `events_not_allowed` |
| `operations` | required non-empty for a provider; each `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$` — at least one dot — and unique | `field_required`, `field_invalid`, `operation_invalid`, `operation_duplicate` |
| `operations` on a `notification` | must be absent or empty | `operations_not_allowed` |
| `entry.module` | `.js` or `.mjs`, relative, no `..` segments, inside the package, must exist | `field_invalid`, `entry_module_outside_package`, `entry_module_missing` |
| `entry.function` | `^[A-Za-z_$][A-Za-z0-9_$]*$` | `entry_function_invalid` |
| `capabilities.exec.executables` | required inside `exec`; 1..16 entries | `field_required`, `field_invalid` |
| `…executables[i].name` | required; `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`, no separators, no duplicates. Resolved on the search path at every reload: an executable file, or on Windows the cmd shim npm writes, which resolves to `node.exe` plus the entry script the shim names (§13) | `field_required`, `field_invalid`, `executable_missing`, `executable_not_runnable` |
| `…executables[i].min_version` | `major.minor.patch`; needs a `version_command` | `field_invalid` |
| `…executables[i].version_command` | exactly one of `["--version"]`, `["-v"]`, `["-V"]`, `["version"]` | `field_invalid` |
| `capabilities.http.hosts` | required inside `http`; 1..32 lowercase `host` or `host:port` (port 1–65535), exact, unique, no scheme, no path, no wildcards | `field_required`, `field_invalid`, `host_invalid` |
| `capabilities.env.variables` | required inside `env`; 1..32 names `^[A-Z][A-Z0-9_]{0,63}$`, unique | `field_required`, `field_invalid`, `variable_name_invalid` |
| a variable named `JASON_…` | never; the prefix is reserved for the runtime | `variable_reserved` |
| a variable that decides what a program loads | never; the same names `host.exec` refuses — `LD_*`, `DYLD_*`, `COMPlus_*`, `CORECLR_*`, `DOTNET_STARTUP_HOOKS`, `NODE_OPTIONS`, `PYTHONPATH`, `JAVA_TOOL_OPTIONS`, `PATH` and the rest listed under `host.exec` below. A granted variable is copied into every child the plugin starts, so being handed one and setting one end in the same place | `field_invalid` |
| `limits.timeout_ms` | 1000 .. `Plugins:Limits:MaxTimeoutMs` (3 600 000 by default) | `field_invalid` |
| `limits.memory_mb` | 16 .. `Plugins:Limits:MaxMemoryMb` (512 by default) | `field_invalid` |
| `binding` | optional; a schema of `type: object` in the dialect of `docs/contracts/`, at most 64 KiB. A problem inside it is located as `binding#<json pointer>` — including a pattern that cannot finish matching the short probe or the 64 KiB one within the dialect's 100 ms on a warm engine, twice | `field_invalid` |
| a field a `binding` declares, at any depth | never named like a credential — any of `token`, `secret`, `password`, `passwd`, `api_key`, `apikey`, `credential`, `private_key`, `authorization`, `bearer`, `cookie`, matched case-insensitively anywhere in the name and with every separator ignored, so `x-api-key`, `API.KEY` and `api key` are all that same name | `binding_secret_like` |

`binding` says what an installation has to tell this plugin before it can act — which account, which
workspace, which mailbox. It never carries the credential: the plugin reaches that through the
environment variables the user granted it, and a binding is written and read by the people who
operate the installation, so a field named like a secret is refused rather than filled in. Every route
to this plugin is held to that schema — at activation, and again at the claim against the package in
front of it — so a route that does not satisfy it never reaches your code. A route with no binding is
measured against the schema as the empty object and reaches your code as `null`, so a plugin requires a binding
exactly when its schema refuses `{}`. It is still a route of its own: it has no identity where `{}` has one,
so trading one for the other is a change of account in provenance, in event sources and in reads by the
account a person was learned in. The key is optional and additive, so `manifest_version` stays `1`. §7 is the
same key from the plugin's side: what a binding is for, and what reaches the code as `context.binding`.

An invocation's effective timeout and memory are `min(what the manifest asks for, the installation's
ceiling)`, and a caller's own budget may lower the timeout further, never raise it. A manifest with
no `limits` gets `Plugins:Limits:TimeoutMs` (300 000 ms — the budget of the slowest operation this
build publishes, so the defaults can run everything the runtime publishes) and
`Plugins:Limits:MemoryMb` (64 MiB).

**The memory budget counts what an invocation allocates, not what it is still holding.** Every answer
a script reads is charged against it — a `host.exec` answer, a `host.http` body, the JSON it parses
out of them — whether or not the script keeps any of it, because the engine measures allocation
rather than the live heap. So the budget is what bounds how many provider calls one invocation can
make: four answers of a megabyte each cost four megabytes even though the script holds one at a time,
and a package that reads ten large answers in one invocation will exceed the 64 MiB default and end
with `plugin_memory_exceeded`. A package that needs more asks for it in its own manifest
(`limits.memory_mb`, up to `Plugins:Limits:MaxMemoryMb` — 512 MiB by default); a package that can
read less should, because a smaller answer is cheaper than a larger ceiling.

`kind: notification` plugins are invoked for the delivery contract, never for a canonical operation: §14.

**Why `version_command` is a fixed list.** A reload runs that command on the user's machine. If the
manifest could name the arguments, a version check would be a way to run anything.

## 4. Installing, reloading and listing

Installing a plugin is copying its directory into `~/.jason/plugins/` (override the data directory
with `JASON_DATA_DIR`). There are no install verbs in this version. The package this repository ships,
the Reply plugin, is also published with every release from 0.11.0 on as `jason-plugin-reply.zip`, its
files at the archive's root: checked against that release's `checksums.txt` and unpacked into
`~/.jason/plugins/reply`, it is installed. Step 3 of [the wiring check](golden-path.md) prints the lines.

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
  "routing_snapshot_id": "rts_01J4…",
  "plugins": [
    { "id": "fake-provider", "version": "1.0.0", "kind": "provider", "name": "Fake provider",
      "description": "…", "homepage": null, "root": "<data dir>/plugins/fake-provider",
      "digest": "sha256:3f2a9c1b4d5e…", "contracts": { "protocol": [1], "operations": [1] },
      "operations": ["echo.run", "exec.run"], "entry": { "module": "main.js", "function": "invoke" },
      "capabilities": {
        "exec": { "requested": [ { "name": "provider-cli", "path": "/usr/local/bin/provider-cli", "version": "2.4.1", "min_version": null, "launch": null } ], "granted": ["provider-cli"] },
        "http": { "requested": ["localhost:5555", "api.example.com"], "granted": [] },
        "env":  { "requested": ["FAKE_TOKEN", "OTHER_TOKEN"], "granted": ["FAKE_TOKEN"] } },
      "limits": { "timeout_ms": 20000, "memory_mb": 64 }, "binding_schema": null, "status": "valid", "problems": [] } ],
  "last_reload": { "at": "2026-09-14T12:00:00.000Z", "source": "reload", "activated": true,
                   "candidates": [ { "directory": "fake-provider", "id": "fake-provider", "status": "valid", "problems": [] } ],
                   "routes": [] },
  "activated": true
}
```

`last_reload.routes` lists the routes that load refused, each named the way it is written — `Routes:Default`,
`Routes:Operations:<operation>`, `campaign:<cmp_…>/routes/<operation>` or `…/routes/default` — with its code and
message, so it is empty when the load activated. The routes a load froze are not listed there: `routing_snapshot_id`
names the snapshot they are in, and the activation journals them in `plugins_reloaded` (below).

### Two kinds of problem

**Package problems** — anything about the files: YAML, fields, ids, versions, contracts, operations,
the entry module, capability declarations, size, readability. One of these in **any** candidate
rejects the whole reload: the previous snapshot stays active, `plugin reload` answers
`409 plugin_reload_rejected` (`retryable: false`) with one detail per problem and exits 1, and
`plugin list` keeps showing the old snapshot with `"activated": false` on the root and the same
problems under `last_reload`. A `settings.json` the reload cannot read rejects it as well, with a
`settings_file_unreadable` detail in the 409 (§9), and so does a `Plugins` section of the settings file that fails
its validator (`plugins_settings_invalid`).

At the first load there is no previous snapshot to keep, so the same rule empties the registry: one
bad package leaves every other package unloaded until it is fixed, and the startup report names it
with its problems rather than passing over it in silence. A set that loads is a set that was read
whole — the alternative is a runtime that quietly runs with one plugin missing.

**Environmental problems** — `executable_missing`, `executable_not_runnable`,
`executable_incompatible`, `executable_version_check_failed`, and at a start only `probe_timed_out`
— are about the machine rather than the package. They make that one plugin `status: unavailable`
with its problems listed, and change nothing else: the set still activates and the other plugins
stay usable. An autostarted runtime sees a different search path than a shell does, and one
uninstalled vendor tool must not empty the registry. `probe_timed_out` is a binding pattern that did
not finish its load-time probe on either of a start's two loads (below); a live reload refuses such
a package as `field_invalid` instead.

A problem's `path` is the place inside the manifest — `kind`, `capabilities.http.hosts[1]`,
`capabilities.exec.executables[0].name` — or `(root)` when the document is not a mapping,
`plugin.yaml#<line>:<col>` for a YAML syntax error, `plugin.yaml` for `manifest_missing`, `(package)`
for a package-level problem, `grants.http[0]` for a grant warning. In the 409 details and in
`--human` output the file is named exactly once: `fake-provider/plugin.yaml#kind`.

### At startup, and the journal

The load at startup follows exactly the same path. If the candidate set has a package problem the
runtime **still starts**, with an empty active snapshot and the diagnostics on record — a broken
plugin must never stand between you and your campaigns, and an empty registry is honest rather than
half-working. One case is loaded once more first: when the only thing wrong is a binding pattern
whose load-time probe timed out twice (reported as `field_invalid`, naming `pattern_timed_out`), or
a route set held back only by bindings no pattern could finish measuring, the
start builds the whole load again on the now warm engine, because a cold engine on a busy machine is
not yet a fault in the package. A package whose probe still cannot finish on that second load loads
`unavailable` with `probe_timed_out`, and the rest of the load stands: its routes are kept with
their bindings unmeasured, and work on them ends `plugin_unavailable` until a reload measures them.
A start and a reload differ here on purpose: at a start nobody is there to send the load again, and
a busy machine is not the package's fault; at a reload the engine is warm and an operator is there,
so the package is refused as `field_invalid` and the previous snapshot is kept. A live `jason plugin
reload` is not repeated. `jason runtime status --human` says which of the two it is:

```text
Plugins:    1 active · snapshot snp_01J4… · loaded 2026-09-14 12:00:00 UTC
Plugins:    none active · last reload rejected
```

An **activated** load writes one global journal entry of the reserved kind `plugins_reloaded`
carrying the snapshot id, its source, `{id, version, digest, status}` per plugin, and the routes it
froze as `{snapshot_id, global_default, override_count, campaign_route_count}` — actor `system`
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

    case "list_membership.add": {
      const contact = input.contacts[0];
      const answer = host.exec({ executable: "reply", args: ["list", "add", "--json"], stdin: JSON.stringify(input.args) });
      if (answer.exit_code !== 0) {
        // Not every non-zero exit is the same not-knowing. A program that never started (`-1`) or that
        // refused the call before doing anything (2 here — your provider's own convention) cannot have
        // acted, so that is `permanent`. A timeout, or an exit once the call was under way, may have
        // acted: that is what `ambiguous` means, and the contract's recovery read is how the next attempt
        // finds out (§7).
        const acted = answer.timed_out || (answer.exit_code !== -1 && answer.exit_code !== 2);
        throw host.fail({
          class: acted ? "ambiguous" : "permanent",
          code: acted ? "provider_answer_lost" : "provider_call_failed",
          message: answer.stderr.slice(0, 500),
        });
      }

      const added = JSON.parse(answer.stdout);
      return {
        result: { items: [{ contact_id: contact.id, status: added.status, external_ids: { contact: added.id } }] },
        external_ids: { contact: added.id },
      };
    }

    default:
      throw host.fail({ class: "validation", code: "unknown_operation", message: "unknown operation " + operation });
  }
}
```

**The return contract.** `invoke` returns `{ result, external_ids? }`, or a Promise of it. `result`
is any JSON value up to 1 MiB (`result_too_large` above that); `external_ids` is an object of at most
64 string values of at most 256 characters, keyed by the **provider's** own names, which travel
verbatim. Because an identifier is later printed to a person exactly as you returned it, a value
carrying a control character — a newline, an escape sequence — is refused the way an undeclared kind
is, and is never written down. Anything else — a bare value, `undefined`, an array, an object without
`result` — fails `permanent` with `bad_return`.

**Failing.** `throw host.fail({ class, code, message, details?, external_ids? })` is the only way to
report a business failure with its class. Anything else thrown fails `permanent` with
`plugin_exception`, keeping the message and the JavaScript stack in `details.stack`. **Know where
`details` ends up**: for a provider operation it is flattened into the attempt's free-text `trace`,
and it is stripped from the work item's `last_error` entirely — so put what a person needs to act on
in `message`, and treat `details` as something a developer reads from the attempt rather than as
structure anything will parse. The class is a
judgement only you can make, and `ambiguous` is the expensive one — it ends the work item and waits
for a person (§8), so spend it only where the effect really may have landed. In the snippet above
`provider_answer_lost` is a code every published contract declares; `provider_call_failed` is
that plugin's own word for a call that never became work, and a contract expecting it would list it
among its `failure_codes` (§7).

**Promises.** `async function invoke` works: the host drains the job queue and unwraps the promise
within the budget. The six `host.*` functions are synchronous — they block until the program or the
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

`binding` is the caller's non-secret note to the plugin, in the shape the manifest's `binding` schema
declares (§3 and §7), or `null` when the route carries none, at most 64 KiB; `attempt_id`,
`attempt_number`, `work_item_id` and `campaign_id` are filled once provider operations are routed —
`attempt_number` is what tells a plugin it is being repeated. No secret is ever in `context`.

## 6. The Host SDK

`host` is a plain JavaScript object with exactly six read-only functions, closed to extension. It is
not a wrapped CLR instance, so there is no member surface beyond those six and no type to walk back
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

A name may resolve to a program **plus leading arguments** — on Windows, where npm installed the CLI
as a cmd shim, that is `node.exe` and the entry script the shim named (§13). Those arguments are the
runtime's: they go ahead of everything in `args`, a plugin can neither see nor set them, and they are
not counted against the `args` cap, because a limit should not depend on how a vendor packaged its
CLI. They travel in the envelope as the grant's `launch` and are recorded on the `exec` line, so what
was actually started is on the record rather than inferred from the name.

| Key | Rule |
|---|---|
| `executable` | required string, at most 256 characters, a granted name |
| `args` | at most 256 strings, at most 1 MiB together |
| `stdin` | string of at most 1 MiB, written as UTF-8 without a byte-order mark, then closed |
| `timeout_ms` | 1 .. the invocation's `timeout_ms`, then clamped to what is left of the budget; default: what is left, less the kill grace |
| `env` | at most 32 entries, names `^[A-Za-z_][A-Za-z0-9_]*$`, never `JASON_*` and never a loader or interpreter hook, values at most 4096 bytes, for this child only |

The child's working directory is the invocation directory, and it inherits the plugin host's own
environment — the base set below plus the granted variables — with `env` added on top. A name that
decides what a program loads is refused with a `TypeError` at the call, whose message names the rule
as `exec_env_not_allowed`: `LD_*`, `DYLD_*`, `COMPlus_*`,
`CORECLR_*`, `DOTNET_STARTUP_HOOKS`, `DOTNET_ROOT`, `NODE_OPTIONS`, `NODE_PATH`, `PYTHONPATH`,
`PYTHONSTARTUP`, `RUBYOPT`, `PERL5OPT`, `JAVA_TOOL_OPTIONS`, `CLASSPATH`, `PATH`, `PATHEXT`,
`COMSPEC` and `SHELL` among them. Ordinary variables of the same runtimes — `NODE_ENV`,
`DOTNET_NOLOGO`, `PYTHONUNBUFFERED` — are yours to set. `stdout` and
`stderr` are read as UTF-8 and captured up to `Plugins:Exec:OutputBytes` (4 MiB) each, the first bytes
kept and the crossing flagged in `truncated`. UTF-8 is the rule on every platform, whatever the console's
code page — what a node program reads and writes on a pipe — so a name or a message outside ASCII reaches
the program as it was written. A call's **kill grace** is the smaller of 5 s and half of what is
left of the budget when the call is made — with 20 s left, a call without `timeout_ms` runs for 15 s
and keeps a grace of 5 s; with 4 s left, 2 s and 2 s. Kept back from a call that asked for no timeout,
it means the plugin is told its program timed out while it still has time to answer for itself. On a
timeout the whole process tree is killed and the call **returns** with `timed_out: true` rather than
throwing — the plugin decides what that means, usually `ambiguous`. The grace bounds everything the
host waits for once the program has ended — the killed program going, and the reading of the output
however the program ended: a program may leave a helper running that inherited its pipes, an exit of
its own accord says nothing about what it left behind, and the call comes back with whatever was
captured rather than waiting for a handle the program it ran no longer holds. A killed program that is
not seen to end within the grace is said once, as an `exec_kill_unconfirmed` line with its process id
and the grace in milliseconds, and the call still returns `timed_out: true`; a read that ran out after
the program was seen to end is not a kill that failed, and says nothing. A kill that is itself slow
does not hold the call past its grace, and the host does not exit before a kill it started has ended:
one still running at the invocation's deadline is said once, as an `exec_kill_unfinished` line with
its process id and the deadline, and the host goes on waiting; should the runtime's own kill come
first, it ends the host with the program, and the invocation ends `plugin_timeout` rather than with
your answer. A granted program that will not start at all is an answer too: `exit_code: -1` with the
reason in `stderr`, and an `exec_launch_failed` line on stderr. At most `Plugins:Exec:MaxCalls` (64)
calls per invocation; the next fails `exec_limit`. Every call is logged as one `exec` line:
executable, what the runtime resolved that name to (`launch`), arguments, exit code, duration, timed
out, truncated.

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

### `host.channelKey`

```js
const key = host.channelKey("linkedin", held);   // "https://www.linkedin.com/in/marta-example", or null
```

The value the runtime's contacts, its do-not-contact register and its claim compare for a value on a channel: the
runtime's own key, by the very function the runtime calls, or `null` where the runtime would store none. Compare a
provider's spelling of an address with the one the runtime stored through it, and you compare as the register does,
never by a copy of the rule that could come to differ. The channel's name is read as the runtime reads one, so
`LinkedIn` and `linkedin` key alike; a channel without a rule of its own keys a value as its text trimmed. The input
bound is the stored value's, 500 characters after trimming: anything longer is `null`. Both arguments must be strings,
or it is a `TypeError`. It is a pure function — no grant, no budget, nothing written to stderr — and a runtime older
than 0.13.0 has no such function.

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
- on everything else: `HOME`, `XDG_CONFIG_HOME`, `TMPDIR`, `LANG`, `LC_ALL`, `LC_CTYPE`, `USER`,
  `LOGNAME`, `SHELL`, `TERM`;
- plus the granted `env` variables, by name.

Never anything else, and **never a `JASON_*` variable** — not even `JASON_DATA_DIR`. A plugin that
dumps its own environment learns nothing about the runtime that started it, and cannot locate the
database.

## 7. Implementing a canonical operation

A **canonical operation** is one unit of SDR work named in the practitioner's language, with no vendor
in the name: `campaign.get`, `list_membership.add`, `campaign.enroll`. Each one is published as a
single machine-readable document under [`docs/contracts/operations/`](contracts/operations), and that
same file is embedded into the runtime as a resource — the contract you read is the contract the
runtime applies, so there is no second copy to drift from it. The `.md` page beside each document
explains it for a person, and [`docs/contracts/README.md`](contracts/README.md) covers the schema
dialect, what every field means and how versions move.

All of it runs. A `provider_op` work item naming an operation this build publishes no contract for is
refused when it is created, and the arguments it carries are measured against that operation's schema
there and then; at the claim the item is routed to a plugin, the whole input below is composed and
validated, and your answer is measured against the operation's output schema before it becomes the
item's result. Which plugin a campaign's work goes to, and which account of it, is an operator's
business rather than yours: a route names it, and work with no route fails at the claim with `no_route`
before your package is started.

Listing an operation nobody publishes a contract for is not a manifest error — the rule there is only
that the name is well formed. It is simply an operation no work item can ever name.

### Reading a document

The first group of fields is the vendor-neutral contract: what the operation reaches, whether it can
be undone, what approval it needs, what it costs, and what must be observed before it is run again.
Those decide whether an operation may run at all, and they are not a plugin's to reinterpret.

The second group is about executing it, and it is the half you implement:

| Field | What it asks of you |
|---|---|
| `preflight`, `contact_projection` | what you are handed: whether a contact comes with the call, and exactly which of its fields and channels; and, under `preflight.provider_contact`, how you find the provider's own contact for that person — the contact proxy rule below |
| `precondition` | what must be true at the provider before you may act, beyond how its contact is found |
| `input_schema` | the whole input you receive, already valid by the time it reaches you |
| `output_schema` | what a succeeded `result` must look like |
| `external_ids` | the kinds of provider identifier you may return, and what each one identifies |
| `recovery_read`, `repeat_after_ambiguous` | what to read, and when, before repeating something that may already have happened |
| `failure_codes` | the code to report for each situation the operation knows about, and the class it carries |
| `timeout_ms` | the budget of one invocation of this operation |
| `conformance` | the behaviours a conforming plugin demonstrates — the list to test yourself against |

### The budget your invocation gets, and the ceiling you set yourself

One invocation is given the operation's own `timeout_ms` — never what is left of the work item's
lease, so the same operation behaves the same however busy the runtime was when it was claimed. Your
plugin's **effective limit is a ceiling on that budget**: the two are combined by taking the lower, so
a plugin can lower what an operation asks for and can never raise it. That effective limit is
`limits.timeout_ms` from your manifest when you declare one, and `Plugins:Limits:TimeoutMs` — **300 000
ms**, the budget of the slowest operation this build publishes — when you do not; either is itself
capped by `Plugins:Limits:MaxTimeoutMs`. That setting is held to that floor when the runtime starts,
naming the operation that forces it, so an installation's defaults can always run the operations the
runtime publishes.

Your own limit is held to nothing of the kind, and that is the trap worth stating plainly. The
reference package declares `timeout_ms: 20000` while `list_membership.add` declares `120000`, so an
invocation of that operation is killed after 20 s — long enough for a stand-in provider, and far too
short for a real one. Nothing in the manifest vocabulary refuses that: what a package declares is
checked for range and never against the operations it lists. A plugin implementing a slow operation
must therefore declare a limit **at least as large as that operation's contract**, or every attempt
that takes longer than its own ceiling is killed and reported `plugin_timeout`, which is
**ambiguous**: the provider may already have acted, and the next attempt has to find out. Declaring no
`limits` at all is the safe choice here rather than the careless one — the installation default holds
every published operation.

### The input you are handed

The runtime composes it from the work item; a plugin never sees a work item's context.

```json
{
  "args": { "list": { "external_id": "L-1129" }, "channel": "email" },
  "contacts": [ { "id": "cnt_…", "first_name": "Marta", "company": "Puerto Analytics",
                  "channels": [ { "channel": "email", "value": "marta@example.com", "primary": true } ],
                  "external_ids": { "contact": "p_88421" } } ],
  "campaign": { "id": "cmp_…", "name": "Q3 LatAm founders", "status": "active",
                "external_ids": { "campaign": "c_7714" } },
  "idempotency_key": "wi_…"
}
```

- **`args`** is what the caller wrote under the work item's `context.input`, and nothing else from
  that context. It is always present — `{}` when the operation takes no arguments.
- **`contacts`** appears when the operation's pre-flight says a contact is `required`, or when it says
  `optional` and the work item names one. It carries exactly one person, and is cut to the **declared
  projection**: the fields the document names, and only the one channel the operation consumes, or none.
  The rest of a person's reachability is not yours to see, and widening that is a change to the
  document. An operation whose contact is `optional` runs with a person or without one, so look for
  `contacts` before reading it.
- **`campaign`** is Jason's own record of the campaign the work item belongs to. A read a person asks
  for — a person's threads, one thread's messages — runs outside any work item and carries no
  `campaign`, so an operation whose document lets the campaign go unnamed must not expect one.
- **`idempotency_key`** is the *work item's* own identifier, present when the operation requires one.
  It is stable across every attempt, which is what makes "what did the prior run under this key do"
  a question a provider's ledger can answer.
- Every `external_ids` object is filtered to **your own** pins — identifiers your plugin returned
  before. Another plugin's identifier for the same person is never visible to you.

### The contact proxy rule

A provider's list or campaign holds the *provider's* contacts, not Jason's, so an operation about a person has you find
the provider's own contact for them. How is data, not prose: each document says it under `preflight.provider_contact`,
in one of four words, and the rule below is the same for every one of them.

| `preflight.provider_contact` | What you do for a person carrying no pin of yours | Published today |
|---|---|---|
| `find_or_create` | Look the person up by the value of the channel `args.channel` names, and create the contact from that value and the person's own fields only when the provider holds nobody under it. | `campaign.enroll`, `list_membership.add`, `optout.record`, `contact.update`, `social_invitation.send`, `message.send` |
| `find_only` | Find the person — by that value, or through what the operation names — and create nobody. | `enrollment.stop`; `conversation.reply`, through its thread |
| `pinned_only` | Work by the pin alone: a person with none fails `contact_not_linked` before anything is called. | `conversation.list`, `contact.delete` |
| `not_used` | The operation names nobody, so there is nobody to find. | every other operation |

The loader holds each word to what it implies, so a document cannot say one thing and declare another: a way that
finds the person declares `external_ids.contact`, `pinned_only` declares `contact_not_linked`, and `not_used` is said
of exactly the documents whose `preflight.contact` is `not_used`. A document's `precondition` says only what its word
does not.

The rule has four parts.

1. **The plugin owns the binding.** With no pin, find the provider's contact by the consumed channel's value, compared
   the way the runtime keys that value — `host.channelKey` answers that key: an email address without regard to case,
   and a LinkedIn profile link by the name after `/in/`, decoded once, composed and lowercased, whatever host of
   linkedin.com's, slashes, query or fragment it carries — and read the lookup to its end. The value you are handed is
   that key already, so compare it as it stands. A provider's search may match by resemblance, so the comparison is
   yours; and
   a lookup you cannot finish says nothing about a second match, so it is refused, never taken for nobody or for one.
   More than one match is `contact_not_unique`, and nothing is done. Create only where the word is `find_or_create`,
   and return the provider's identifier under `external_ids.contact` — on `error.external_ids` too, when the call
   fails after you learned it. The runtime never guesses one.
2. **A pin is worked by, and never overwritten.** Once `contacts[0].external_ids.contact` is present it is the
   provider's identifier for this person, whatever the operation, and nobody matches the person by address again — which
   is what stops two spellings of one address becoming two people. If the provider answers with another identifier,
   return the one it answered: the runtime records it beside the pin as a divergence, keeps the pin, and the attempt
   still succeeds.
3. **Deleting a Jason contact never deletes the provider's.** A provider's contact is deleted only by an operation whose
   whole purpose that is — `contact.delete`, approved by a person, by the pin — never as a side effect of another, and
   never because the person went away in Jason. Nor does deleting the provider's contact change Jason's: the person,
   their pins and the do-not-contact register stay as they were.
4. **Fields and lists travel with the operation that needs them.** You send what the operation's `contact_projection`
   and its arguments hand you — the fields `contact.update` sets, for one — when the operation runs. Nothing is copied to the provider in the background, and the runtime keeps no
   copy of the provider's contact.

LinkedIn values written under an earlier key are rewritten to the key when the runtime starts, before anything is
claimed, so the value you are handed is a key. Two rewrites have run. The first lowercased the path on linkedin.com,
collapsed two spellings of one person on the register into that person's one stop, the earliest, with its reason, and
kept a contact's two channels that became one value as one, the primary where one of them was. The second keys a link
by the profile's name and deletes nothing: a value the rule refuses is left as it stands, and of several of one
contact's values, or several of the register's entries, that come to share a key, one holds it and the others are left
as they stood — so a value you are handed can, rarely, be one the runtime keys as no profile, which matches nothing a
provider holds. Where the value the claim reads for a contact is left beside the one holding the key, the primary moves
to the holder, so the claim asks the register about the key. A contact that still holds a value left so cannot be
updated with its channels as read: the value the rule refuses answers `invalid`, and the one left beside the key answers
`duplicate`, because it keys to the key the contact already holds. No stop is lost by either rewrite, the journal keeps
every line it had, and neither adds one.

### Repeating after a lost answer

`context.attempt_number` is what tells you that you are being repeated. The runtime does not reason
about what the last attempt achieved, and neither should you.

When a document says `repeat_after_ambiguous: "after_recovery_read"`, the `recovery_read` it declares
is **a call you have to make** on every attempt after the first — not a state check you can reason
your way out of. Read the ledger under the idempotency key, and the membership or participation the
operation writes, *before* writing anything, and answer from that reading when the effect already
happened. A plugin that skips the reading looks perfectly correct against a provider that happens to
be idempotent, and bills twice against one that is not; performing it is what makes one crash cost one
effect. `campaign.get` says `safe` instead, because a read may simply be asked again.

### What to return

```json
{
  "result": { "items": [ { "contact_id": "cnt_…", "status": "added", "external_ids": { "contact": "p_88421" } } ] },
  "external_ids": { "contact": "p_88421" }
}
```

The provider's identifiers come back in **two places, and both are wanted**. The `external_ids` beside
`result` is the invocation's own answer: the pins the runtime will record, and what a later call
receives back where that entity sits in its input — `contacts[0].external_ids` for a person,
`campaign.external_ids` for a campaign. The `external_ids` inside each item
of the result is the record of *who got what* — which person this identifier belongs to — and it stays
right when a call addresses more than one person. They are not copies of each other; return both.

**A failure's identifiers go on `error.external_ids`.** That is where `host.fail` puts them and the
only place the runtime reads them on a failed answer: a top-level `external_ids` beside a failure is
accepted by the validator and then silently dropped, which is exactly the answer-lost case where a pin
is the only trace that anything happened. Return what you learned before the call went wrong — the
provider's identifier for a contact you created — inside the failure, and the runtime writes it down
even though the attempt failed, so the recovery read on the next attempt has something to read by.

**Translating the provider's words is your job.** A document's vocabulary is neutral and a provider's
is not: `campaign.get` answers with `draft | live | paused | archived | other`, and only your plugin
knows which of those a given provider state is. Where nothing fits, answer with the contract's
catch-all — `other` — rather than the closest-looking word, and let the provider's own word travel
untranslated in the free-form `vendor` object beside the neutral result. Nothing is lost that way, and
nobody downstream reasons over a word you guessed.

### Pages

A read that answers a page — `enrollment.list`, `conversation.list`, `conversation.get`, `sender.list` — is handed
`args.limit` and `args.offset` and answers at most `limit` items from `offset`, with `has_more`. Ask the
provider for that page, map what comes back, and **stop adding items before the result would pass
768 KiB**, counted the way the runtime counts a result: six bytes for DEL, for every character outside
ASCII and for each of `<`, `>`, `&`, `'`, `"`, `+` and the backtick, twelve for one outside the Basic
Multilingual Plane. Then say `has_more: true`. The runtime refuses a result above 1 MiB however few items
it holds, and a page of long text in a non-Latin script passes that well below `limit`. The caller asks
for the next page at `offset` plus the number of items you returned. Always return at least one item when
the provider returned any: the documents bound every output string so that one fits.
`plugins/reply/modules/page.js` is a count that only ever errs short.

**An answer you could not read whole.** `host.exec` reads a program's stdout up to
`Plugins:Exec:OutputBytes` and says so in `truncated.stdout`. On a read, report
`provider_answer_too_large`, permanent, which every read declares: asking again would be cut at the same
place, and the remedy is a smaller page or a larger setting. On a write, a cut answer is a lost answer —
the write may have happened — and its class is the write's own.

### The `binding`

`binding:` in the manifest (§3) is a schema — in the same dialect as the contracts — for what an
installation must tell your plugin before it can act: which account, which workspace, which mailbox.
The value satisfying it reaches you as `context.binding`. A route with no binding hands you `null`, also when your
schema declares fields (all of them optional); read it as `{}`.

**A binding selects an identity; it never carries a credential.** A declared property named anything
like one is refused at load with `binding_secret_like`, because a binding is written and read by the
people who operate an installation, and a field named `api_token` is an invitation to put a secret in
a file that was never meant to hold one. Punctuation is no way around it: the name is read with its
separators dropped, so `x-api-key` is refused exactly as `api_key` is. A credential reaches you the way every secret does: through a
variable your manifest declares under `capabilities.env`, granted by the user and read with `host.env`
(§6), never through the envelope. The worked example below is exactly this shape — its binding names
the account to act in, and the program it acts with comes from a granted variable.

### Checking yourself against the fixtures

Every operation ships argument and answer vectors under `docs/contracts/fixtures/<operation>/`, and
each of them is executed by a test in this repository, which is what keeps the documents and the
runtime from drifting apart. An **input fixture**'s `input` is a whole plugin input, so it is also the
body of a hand-written envelope: take one, put it under `"input"` in the envelope of §10 with
`"digest": null`, and drive the invocation yourself (§12):

```sh
cat invocation.json | jason plugin-host --protocol 1 --plugin my-plugin --operation list_membership.add --correlation dev
```

The **outcome fixtures** beside them are the other half: what a conforming answer looks like, with the
status, class and retriability each one implies. Compare what came back on stdout with those, and read
the operation's `conformance` list for the behaviours your own tests should cover.

### What the runtime holds you to

Each operation's own `conformance` list is the behaviour of that operation. These four are the rules
every operation's plugin is held to, and the runtime enforces each of them:

1. **On any attempt after the first, perform the declared recovery read before writing.**
   `context.attempt_number` is what tells you which attempt this is; when the document says
   `repeat_after_ambiguous: "after_recovery_read"`, the `recovery_read` it declares is a call you make,
   not a state you reason about. It is the only reason the runtime is willing to repeat an ambiguous
   end at all, and a plugin that skips it turns one crash into two effects.
2. **Return the provider's identifiers in the right place** — on `external_ids` beside a result, on
   `error.external_ids` inside a failure — and only kinds the operation declares. An undeclared kind
   makes a *success* `result_invalid`; on a failure it is refused and named beside the failure the
   plugin reported, which is kept. **A top-level `external_ids` on a failed outcome is dropped without
   a word**: the host writes null there for a failure, so the identifier you meant to record is lost
   exactly where it matters most — after a lost answer, where the pin is the only trace that anything
   happened.
3. **Answer in the operation's own vocabulary**, so that a result satisfies its output schema. An
   answer that does not is `result_invalid`, is never repeated, and is kept on the attempt.
4. **Find the person as the contact proxy rule says**: work by a pin when you are given one, and otherwise find or
   create exactly as the document's `preflight.provider_contact` says.
5. **Carry the provider's own refusal back, in the runtime's vocabulary.** A provider may know something
   the runtime cannot: a person suppressed at the provider between the moment the work was claimed and
   the moment you act on it is the ordinary case. Your job is not to re-check anything the runtime
   already checked — it is to recognise the provider's refusal and report it as `suppressed` with class
   `permanent`, so the item ends in a person's inbox rather than being retried into the same wall — unless
   the document declares `preflight.suppression: not_applicable`: an operation that only takes a person out
   is for exactly the people a suppression names, and declares no `suppressed` to report. The
   reference plugin does exactly this and nothing more: the re-check happens at the provider, and the
   plugin maps the answer.

And one thing the runtime does for you, which it is easy to write a plugin that does not expect: every
`external_ids` object in your input is filtered to **your own** pins. What another plugin calls the
same person or campaign never reaches you, and after a disagreement you keep receiving the value Jason
recorded rather than the one it disputes.

### The worked example

`plugins/reply/` is the one to read: a marketplace package that implements all twenty-one published
operations against a real provider by driving that provider's own command-line program. Its README
says what each operation maps onto, what the provider will not answer, and which of its claims were
checked against a live account; its `modules/errors.js` is the whole error table written as data, so
the mapping can be read rather than traced through code.

There is no shorter published example of a canonical operation: the stand-in package the runtime's own tests
drive against a stand-in vendor program is a test fixture, and it is not published. For the shape of a package
alone — a manifest, a grant, one call and every outcome its contract defines — `plugins/ntfy/` is the smallest
complete one, though it implements the delivery contract (§14) rather than an operation.

## 8. Failure classes

Every failure carries one of four classes, and the class — not the code — is what the runtime reads.

| Class | Meaning | What the runtime does |
|---|---|---|
| `transient` | it may work next time: a 429, a connection reset, a busy provider | retried, within the work item's attempt budget |
| `permanent` | it would fail the same way again: a bad request, a missing resource, a bug | final |
| `validation` | the input was wrong | final |
| `ambiguous` | **it may already have happened** at the provider | the operation decides |

`ambiguous` is never repeated blindly: a blind repeat could send the same message twice, and that is
the user's reputation rather than a retry budget.

What may be done about an ambiguous end is a property of the operation, and every published contract
states it as `repeat_after_ambiguous`: `safe` for a read, which may simply be asked again, and for a
reversible control that costs nothing, whose second performance changes nothing — pausing a paused campaign,
closing a closed thread; `after_recovery_read` for a write that declares the reading which settles the question;
`never` where nothing can settle it — `conversation.reply`, a send no read tells apart from another — and an item
that ends that way waits for a human or a manager role. The
runtime reads that field and nothing else when it decides; your half is to make the permission true:
when you are handed an attempt number above 1, perform the declared recovery read before you write
anything (§7). One exception belongs to neither of you: an answer that arrived and does not satisfy
the operation's output schema is `result_invalid`, which is ambiguous and never repeated whatever the
document allows, because the next attempt would run the same code over the same answer.

A contract also names the **codes** an operation may report and the class each carries, so that two
plugins for two providers answer the same situation with the same word. Nothing checks a code against
that list — the class is what the runtime reads — but a code outside it is a code no planner was
written for.

**Protocol failures** mean the runtime never got an answer at all. They are classified by how much
may already have run.

| Code | Class | When |
|---|---|---|
| `plugin_launch_failed` | transient | the child could not be started at all |
| `plugin_not_loaded` | permanent | no plugin of that id is in the active snapshot |
| `plugin_unavailable` | permanent | the plugin is loaded but its environment is not usable |
| `plugin_operation_unsupported` | permanent | the plugin does not implement the operation |
| `plugin_kind_not_invocable` | permanent | a `notification` plugin was asked for a canonical operation, or a provider for a delivery |
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
`plugin_timeout`, which is always ambiguous. Reading what that child wrote is bounded by the same
grace however the child ended, exactly as `host.exec` bounds its own: a child may leave a helper
running that inherited its pipes, and exiting cleanly says nothing about what it left behind — so an
invocation is classified from what was captured by the end of the grace rather than waiting on a
handle that nobody is coming back for.

The other codes the host itself writes into a failed outcome — as opposed to your own vocabulary —
are `plugin_exception`, `bad_return`, `result_too_large`, `entry_function_missing`,
`plugin_syntax_error`, `plugin_memory_exceeded`, `plugin_statement_limit`, `plugin_recursion_limit`,
`capability_not_granted`, `executable_not_allowed`, `exec_limit`, `http_host_not_allowed`,
`http_scheme_not_allowed`, `http_method_not_allowed`, `http_header_not_allowed`, `http_limit` and
`module_not_allowed`. All of them are `permanent`.

## 9. Capabilities and grants

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
the next `jason plugin reload`, and `plugin list` always shows requested next to granted. The reload
reads `settings.json` itself before it resolves anything, so a reload sent straight after an edit
holds the edit. The one exception is two edits that land within the settings watch's own delay of each other: a
reload can meet the watch still applying the earlier edit and hold that one, and the next reload holds the later
edit, so after a quick second save, reload once more. A file that is there and cannot be read — not valid JSON, or held open by an
editor — rejects the reload as a package problem does: `409 plugin_reload_rejected` with one
`settings_file_unreadable` detail at `settings.json`, naming the failure by its type and, for JSON,
by its line and byte, never by what the file holds; the previous snapshot stays active, nothing is
journaled, and `plugin reload` exits 1. A file that is not there rejects nothing: the reload resolves
the settings in force, and the runtime's log says by the failure's type that the file could not be
read. Where no setting has come from the file, as on an installation that has never had one, those
are the defaults and nothing is said.

Two consequences worth knowing. A capability the manifest never requested is **absent** from the
invocation rather than empty, which is a different thing from one that is present and always says no.
And the version check of a declared executable runs only for an executable the user **granted** — a
reload must never start a program nobody consented to; an ungranted one is resolved for presence only
and lists `"version": null`.

## 10. The protocol

The runtime starts the shipped executable in plugin-host mode, and you can do the same by hand (§12):

```text
jason plugin-host --protocol 1 --plugin <id> --operation <op> --correlation <id>
```

All four are required, order does not matter, and nothing else is accepted. They are public values a
process listing may show, and they are repeated inside the envelope — the child refuses a
disagreement. Everything else, the input, the binding and the resolved grants included, is on stdin.

The wire is **ASCII both ways**. Each stream is UTF-8 without a byte-order mark, and the envelope and
the outcome are JSON with each character outside ASCII written as an escape, so not one byte above 127
crosses in either. That is a second defence beside the encoding: a child started without a window of
its own is given a fresh console on the system's legacy code page, and a byte above 127 would be read
through that page by a program that did not choose an encoding, whatever encoding it was written in. An
escape reads the same in any page.

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
  "grants": { "exec": { "executables": [ { "name": "provider-cli", "path": "/usr/local/bin/provider-cli", "launch": null } ] },
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
secret value is in the envelope. A capability the manifest never requested is `null` here. An
executable's `launch` is `null` for an ordinary program and carries the arguments that go before the
plugin's own where the name resolved to an interpreter and a script (§13).

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
             "details": { "retry_after": 30 }, "external_ids": { "contact": "r_123" } },
  "diagnostics": { "duration_ms": 118, "exec_calls": 0, "http_calls": 1, "log_lines": 0 } }
```

A failure's identifiers belong **inside the error**, as above. The top-level `external_ids` is read
only on a succeeded outcome: beside a failure it is accepted by the validator and then dropped, and
what is dropped there is usually the one trace that anything happened at all (§7).

Stdout belongs to the host alone — there is no `console` — so nothing a plugin writes can corrupt the
channel. The runtime re-validates the outcome after the child exits: exactly one JSON document, this
invocation's id, `protocol_version` 1, a known status, an error exactly when the status is `failed`,
a result of at most 1 MiB, `details` of at most 64 KiB, a lower snake_case code of at most 64
characters, a message of at most 2000 characters, `external_ids` of at most 64 string values of at
most 256 characters. Anything else is `plugin_malformed_outcome`. The operation's own `output_schema`
is **not** applied here — this check is about the envelope. The runtime applies it a moment later, when
it turns the answer into the work item's outcome: a result that does not satisfy the schema, or an
identifier of a kind the operation never declared or carrying a control character, ends the item with
`result_invalid` and the failing pointers, rather than with a malformed outcome (§7).

### stderr: JSON Lines

```json
{"ts":"2026-09-14T12:00:00.123Z","level":"info","source":"host","plugin":"fake-provider","invocation_id":"pin_01J4…","message":"exec","data":{"executable":"provider-cli","args":["--version"],"exit_code":0,"timed_out":false,"duration_ms":42,"truncated":false}}
```

`source` is `host` or `plugin`; `data` and `truncated` appear only when they apply. The host's own
messages are `invocation_rejected`, `digest_unverified`, `env_probe`, `exec`, `exec_launch_failed`,
`exec_kill_unconfirmed`, `exec_kill_unfinished`, `http`, `http_failed` and `log_truncated`. The runtime copies the stream, redacted, to
`stderr.log` in the invocation directory and keeps the last 4096 characters as the trace that travels
with a protocol failure. It is read in fixed chunks and copied line by line, up to
`Plugins:Invoker:StderrBytes` (4 MiB); past that the file ends with a single `[stderr truncated]`
notice. A line is kept or dropped whole, never cut — half a JSON line is not a line, and half a
redacted value is not redacted — so a line longer than 128 Ki characters, which is far more than any
line this host writes, is replaced by an `[stderr line dropped: …]` notice instead. Write your
diagnostics as lines and the runtime keeps every one of them; write megabytes without a line ending
and it keeps none of them.

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

## 11. Isolation, honestly

Isolation here is **process isolation plus interpreter isolation**, and that is the whole claim.

What is prevented: the plugin cannot reach the runtime's memory, its database, its API or its
capability token, because it runs in a different process that was never told any of them; its engine
has no CLR access, no `require`, no filesystem, no `eval` and no `new Function`; its modules cannot
leave the package root; the programs it may start are a fixed list of names the runtime resolved, run
through an argument array and never a shell; its HTTP is two methods to an exact allowlist with no
redirects and no injected credentials; its environment is built from nothing and holds no `JASON_*`
name; and memory, statements, recursion and wall-clock time are all bounded, with the whole process
tree killed when the budget runs out.

`env` on `host.exec` is a **trust decision** rather than a detail of the call: whatever it sets reaches
a program you granted, so the loader and interpreter hooks — the variables that make an operating
system, a runtime or an interpreter load somebody's code before the program's own first line — are
refused. Without that, granting one program would quietly grant everything the plugin brought with it.

What is **not** prevented: this is not a container, a virtual machine or a kernel-level isolation
boundary — it is not a sandbox, and the documentation will not call it one. The plugin host runs
under your own operating-system identity with your own permissions. A granted program can do whatever
that program can do, and a granted host can be sent whatever the plugin wants to send it. Plugin
packages are **executable local code that you installed**, and the grants are the real boundary —
which is why the default is that nothing is granted.

Signing, publisher identity, trust levels, install-time capability review and operating-system
resource control are deliberately not in this version. Read a package before you install it, and
grant it the narrowest set of executables, hosts and variables that lets it do its job.

## 12. Testing a plugin locally

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

Write `invocation.json` to the shape in §10, with `"root"` pointing at your package directory. A
hand-written envelope may set `"digest": null` — the host then skips the verification, runs the
package as it is on disk, and says so with a `digest_unverified` line on stderr. The runtime always
sends a digest, so this is strictly a development path. Export the granted variables in your shell
first, since `host.env` reads the process's own environment. The outcome comes back on stdout as one
JSON object, the diagnostics on stderr as JSON Lines, and the exit code says whether the protocol
completed.

**The shape to copy.** `plugins/ntfy/` in this repository is the smallest complete, valid package —
manifest, `main.js`, a grant and every outcome its contract defines; `plugins/reply/` adds local modules
under `modules/` and implements every published operation. Read either before you write your own.

**Against the published vectors.** The argument and answer fixtures under `docs/contracts/fixtures/`
are the operations' own examples; §7 says how to drive one through the host by hand.

## 13. Known limitations of this version

- **Windows: only `.exe`, `.com` and npm's own cmd shim.** A bare name that resolves only to a `.bat`
  or a `.ps1` — or to a `.cmd` that is anything other than the shim `npm install -g` writes today — is
  `executable_not_runnable`, because starting one means `cmd.exe` interprets the arguments, the shell
  the argument-array rule exists to exclude. The one exception is that shim, which every Node-based
  vendor CLI installed by npm resolves to: the runtime reads it, takes the entry script it names, and
  starts `node.exe <entry script> <your arguments>` through the argument array, so no shell is
  involved at any point. It is the **last** resort, never the first: the search path is walked to its
  end, any `.exe` or `.com` in any directory wins outright, and only then is the first `.cmd` that was
  found read. Every one of these has to hold, and a file that breaks one is refused with that rule
  named:
  - the file is at most 8 KiB and matches npm's current `:find_dp0` template exactly, so an older
    template is refused;
  - the package is one directory under `node_modules`, and that directory reads as a package name
    before any path is built from it — so a **scoped package** (`node_modules\@scope\name\…`) is
    refused and says so, and so is a segment that is not a name at all, such as `..` or a drive letter;
  - that package's own `package.json` maps **this declared name** to **this same entry**, either as a
    `bin` object keyed by the name or as a bare `bin` string on a package whose own `name` is the
    declared name — no other shape is read;
  - the entry lies inside the package directory as the disk really holds it, followed link by link
    from that directory down — so a `..` in the entry path is refused, and so is a junction or a
    symbolic link that leaves the package, at `node_modules`, at the package itself, at any directory
    inside it, or at the entry file;
  - `node.exe` is found beside the shim or on the same search path, and recorded as an absolute path.

  What the name resolved to is visible in `plugin list` and in the envelope as `launch`, and every
  `host.exec` line records it.

  Resolving a shim means the runtime now reads a file to decide what to start, which it did not do
  before. It does not widen who can substitute the program — anyone who can write npm's directory can
  already replace an `.exe` on the search path — but it is a new kind of trust, so it is narrow on
  purpose: npm's exact template, the package's own `bin` entry as a cross-check, the entry script held
  inside the package directory, an absolute interpreter, and the whole thing re-read at every reload,
  never remembered from the last one. A file that deviates in any of those is refused with the rule it
  broke, exactly as a `.cmd` is refused today.
- **Proxies come from the base environment.** `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY` and their
  lowercase forms are passed through and honoured; there is no proxy configuration of Jason's own and
  no custom TLS.
- **`host.http` is deliberately narrow**: `GET` and `POST`, exact hosts with no wildcards, text
  bodies only — no binary, no streaming — no automatic redirects, no cookies, no injected
  credentials.
- **Invocation directories are kept.** `~/.jason/work/plugins/<invocation_id>/` holds `stderr.log`
  and nothing else, and nothing is cleaned up in this version.
- **One installed version per id**, because the id is the directory name. Two plugins may implement
  the same operation; which one a campaign uses is a routing question: a route names the plugin, per
  campaign or for the whole installation.
- **Notification plugins are not routed**: a route to one is refused at activation with
  `route_plugin_kind_not_invocable`; they are reached through the `Notifications` settings instead (§14).
- **No install verbs, no signing, no trust model.** Installing is copying a directory.

The operations themselves, with their schemas, fixtures and the dialect they are written in, are in
[docs/contracts/](contracts/README.md). Nothing else is part of the contract: Jason's own design notes are
not published, and this guide and those documents are the whole of what a plugin is held to.

## 14. Writing a notification plugin

A notification plugin carries Jason's pointer — how many approvals and questions are waiting, whether protective
actions failed, and where to look — to a service a person reads. It is invoked for one thing, the delivery contract
[`notification.send`](contracts/notifications/notification.send.md), by the runtime's own notifier, and only when an
operator has named it under `Notifications:Plugins:<id>`. No work item
names it and no route reaches it. [`plugins/ntfy`](../plugins/ntfy/README.md) is the reference.

**The manifest.** `kind: notification`; `contracts.operations` lists the versions of the delivery contract it speaks,
which this build reads as `[1]`; and no `operations` list, because it implements no canonical operation:

```yaml
manifest_version: 1
id: my-notifier
version: 1.0.0
kind: notification
contracts:
  protocol: [1]
  operations: [1]          # the delivery contract notification.send, version 1
capabilities:
  http:
    hosts: [notify.example.com]
  env:
    variables: [MY_NOTIFIER_TOKEN]
binding:
  type: object
  properties:
    channel: { type: string, minLength: 1, maxLength: 64 }
  required: [channel]
  additionalProperties: false
```

A notification plugin that declares no version of the delivery contract is recorded as `not_attempted`,
`not_declared` — listed, never invoked, and never left out silently.

**What it is handed.** `operation` is `notification.send`; `input` is `{ title, message, counts: { approvals,
decisions } }`, where `message` is one line ready to send; `context.binding` is the operator's binding; the context
names no campaign, work item or attempt. **What it answers:** `{ result: { receipt } }` with the service's own id for
the message, or `{ result: {} }`; a result outside the contract is recorded as an ambiguous failure, because the
pointer may have arrived. A failure is one of three codes — `notification_unreachable` (transient: the runtime tries
once more, thirty seconds later), `notification_refused` and `notification_unauthorized` (permanent) — thrown with
`host.fail`.

**The binding names an account, never a credential.** A field named like a credential at any depth is refused before
anything starts, as `binding_invalid`, and so is a binding the manifest's own `binding` schema refuses. A secret is
read with `host.env` from a variable the manifest declares under `capabilities.env` and the operator grants; it is sent
in a header, and never logged or returned.

**A self-hosted service.** `capabilities.http.hosts` names the hosts the package may reach, and a grant can only
narrow it. A plugin for a service people also run themselves names the public host; an operator who runs their own
edits `hosts` in their copy, grants the same host, and names the server in the binding. The edit changes the
package's `sha256:` digest, so their copy is a package of its own.

**The outcomes a person sees** are the `notification_sent` line's: `delivered` with the receipt, `failed` with the
code and class, or `not_attempted` with `unavailable`, `not_declared` or `binding_invalid` — in `jason runtime status
--human`, and in `jason status`'s `notifications` check.

**Testing it.** A loopback server stands in for the service: grant `127.0.0.1:<port>`, put that host in a copy of the
manifest, and invoke the package through the plugin host the way §12 describes, with an input like the one above. The
ntfy package's own tests are this, end to end.

## 15. Writing an events plugin

An events plugin is a provider plugin that also reads what happened in its accounts: a thread that moved, a reply the
provider detected, an invitation accepted, a message that bounced. It is invoked for the events contract
[`events.read`](contracts/events/events.read.md) by the runtime itself, once for each page of a read, with the binding
of the account it reads; no work item names it and no route carries it. A read belongs to no campaign, and the
runtime finds the campaigns an event concerns by the identifiers your operations pinned.

**The manifest.** `kind: provider`, its `operations` as ever, and the events contract version it speaks:

```yaml
contracts:
  protocol: [1]
  operations: [1]
  events: [1]              # the events contract events.read, version 1
```

**What it is handed.** `operation` is `events.read`; `input` is `{ cursor, since, until, limit }`, every field present:
a cursor your plugin returned with an earlier page, or a moment to read from by date, with `until` the end of a range
or `null`; `limit` is at most 100. `context.binding` is the route's binding, and the context names no campaign, work
item or attempt.

**What it answers.** `{ result: { events, cursor, has_more } }`: the events of the page in the provider's order, a
cursor to read on from — always, even when the page is empty — and whether more follow now. Every event is one of four
closed kinds of identifiers, moments and the provider's own flags; a subject, a preview or a name is refused with the
whole page. The cursor is yours: 1 to 2048 printable ASCII characters with no space, which the runtime stores and hands
back unchanged and never reads. Say who moved a thread with `moved_by` — `other_side`, `account`, `provider`, or
`null` only where the provider cannot say — because only a move by the other side reads as a reply, and Jason's own
replies, categories and read flags move threads too. Name a thread that moved as `conversation.list` names it — its
`thread` the thread's `external_id` there, its `occurred_at` the moment the thread last moved, which `conversation.list`
reports as `last_activity_at`, to the millisecond — so that a move both reads find is said once. Name under `campaign`
the provider campaign a moved thread belongs to wherever the provider keeps one, by the identifier `campaign.get` pins,
and `null` only for a thread outside every provider campaign: the runtime leaves a campaign's inbound read to your
plugin on the strength of that, so a plugin that names no campaign on a move silences the campaigns it serves. Return
no `external_ids`: a read pins nothing.

**Reading by date.** With `since`, return across the pages your cursors chain every event at or after it (and before
`until`); page a provider that pages only by offset newest first until the last item's moment passes `since`, and
answer what a provider holds when it holds less.

**Failures,** thrown with `host.fail`: `events_unreachable` (transient), `events_unauthorized`, `events_unsupported`
and `events_cursor_unknown` (permanent) — the last for a cursor your plugin can no longer read, after which the runtime
reads by date.

**How the runtime reads.** Every account a route binds at a provider that declares the contract is read on the
installation's cadence, `Dispatcher:EventsCadenceSeconds` — ten minutes unless set otherwise — and at once when the
runtime starts: up to twenty pages a read, one child each, the first read of an account reaching thirty days back. The
fixtures under [`contracts/fixtures/events.read/`](contracts/fixtures/events.read/) are what to check a plugin against.
