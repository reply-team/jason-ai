# Working in this repository

Jason is a local-first, vendor-neutral SDR domain runtime: a .NET runtime process that owns durable
campaign state and exposes a loopback Runtime API, a CLI that is a pure client of that API,
JavaScript plugins that execute provider operations in a separate plugin-host process, and skills
that teach AI agents the job. `docs/architecture.md` is the design authority for anything not
covered here.

## Layout

- `runtime/` — all C#: `Jason.slnx`, `src/Jason.App` (the single executable; picks the mode from
  argv), `src/Jason.Cli`, `src/Jason.Runtime`, `src/Jason.PluginHost`, `src/Jason.Contracts`,
  `tests/` (one xUnit project per source project).
- `plugins/` — plugin marketplace. `skills/runtime/`, `skills/business/` — skill packs. `docs/` —
  maintained documentation only.
- `global.json` at the root pins the SDK; run every command from the repository root.

## Commands

- Build: `dotnet build runtime/Jason.slnx`
- Test everything: `dotnet test --solution runtime/Jason.slnx`
- Test one project: `dotnet test --project runtime/tests/Jason.Runtime.Tests`
- Test one method or class: append `-- --filter-method "*MethodName"` or `-- --filter-class "Namespace.ClassName"`
- Run: `dotnet run --project runtime/src/Jason.App -- --version` · `-- runtime run` · `-- runtime status`
  · `-- runtime start` (background; nothing is registered until you ask) · `-- runtime stop`
  · `-- runtime autostart enable | disable | status` · `-- campaign list`
  · `-- workitem list` · `-- role list` · `-- plugin list` · `-- plugin reload`
- Add a migration: `dotnet tool restore` once, then
  `dotnet ef migrations add <Name> --project runtime/src/Jason.Runtime --startup-project runtime/src/Jason.Runtime --output-dir Persistence/Migrations`
- Publish: `dotnet publish runtime/src/Jason.App -c Release -r <rid> --self-contained -p:PublishSingleFile=true`

## Boundaries that the build enforces

- `Jason.Cli` references only `Jason.Contracts` and the HTTP client. Never `Jason.Runtime`, EF Core
  or SQLite: the CLI never touches the database, it talks to the Runtime API.
- Plugin JavaScript never runs inside the long-lived runtime process; it runs in the plugin-host mode
  of the same executable. `Jason.Runtime` references neither Jint nor `Jason.PluginHost`;
  `Jason.PluginHost` never references `Jason.Runtime`; manifests are read with YamlDotNet in
  `Jason.Runtime` only, so the contracts, the CLI and the host stay YAML-free.
- The dispatcher is deterministic runtime code; it never reads the meaning of a work item's context.
- Package versions live in `runtime/Directory.Packages.props` only.
- Warnings are errors. Every test runs offline and uses an isolated temporary data directory.

## Conventions

- Terminology: **plugin** (not adapter), **WorkItem** (not Task), **channels** (not handles).
  Skills, campaign context, role notes and learned practice are four distinct knowledge concepts;
  do not blur them. Membership in a Jason campaign is local; `campaign.enroll` — a later,
  provider-side, approval-gated act — is a different thing.
- Runtime API: RPC style, `POST /v1/{noun}.{verb}` for everything, reads included. Success is 200
  with a bare DTO; errors are `{"error":{"code","message","retryable"}}` with snake_case codes.
  JSON is snake_case, enums are snake_case strings, timestamps are ISO-8601 UTC, public ids are
  prefixed ULIDs (`cmp_…`, `wi_…`, `att_…`, `rol_…`). Partial patches distinguish absent from null
  (`Optional<T>`); lists paginate with `limit`/`cursor`; every mutating request may carry `actor`
  and `reason`.
- Work execution: work items move through one transition table (`WorkItemTransitions`) and end
  through one routine (`AttemptOutcomes`); never write a status elsewhere. The attempt id is the
  fencing token: executor operations change state through a single guarded UPDATE, never
  read-then-write. The dispatcher claims inside `BEGIN IMMEDIATE`, at most as many items as it has
  free handler slots, one per campaign per scan. The launch envelope goes to the child on stdin;
  nothing goes on argv, and the capability token never reaches a child's environment, a
  work-directory file or an attempt row (redacted). `docs/work-execution.md` is the contract.
- Canonical operations: a contract is data, not code. One JSON document per operation lives under
  `docs/contracts/operations/` and is embedded into `Jason.Contracts` as a resource, so the file a
  plugin author reads is the file the runtime enforces — never write a second copy of a rule that
  document already states. Schemas are the restricted JSON Schema dialect `SchemaValidator`
  publishes; a keyword outside that vocabulary is refused rather than ignored. `context.input` is the
  reserved work-item context key carrying an operation's arguments: it is validated at
  `workitem.create`, and again at `workitem.update` whenever a patch names the key. The fixtures
  under `docs/contracts/fixtures/` are executed by tests, so a document and the code cannot drift
  apart. `docs/contracts/README.md` is the contract.
- Routing: which plugin performs an operation for a campaign is a route, resolved by a pure function
  over an immutable snapshot — campaign operation override, campaign default, global operation
  override, global default, with exact masking and no fallback to another plugin. Routes are frozen
  by the same reload that freezes plugins: the `Routes` section of `settings.json` is read only
  there and at startup, `route.set`/`route.unset` copy the global half from the active snapshot, and
  a bad route rejects the whole reload. The claim is fail-closed: twelve checks in one published
  order, one code per reason, decided before a child exists, with the attempt kept. A binding names
  an account and never a credential. A provider identifier is pinned once and never overwritten — a
  different value is recorded beside it as a divergence and the attempt still succeeds. Provenance
  is written at the claim and completed at the end; nothing rewrites it. `docs/routing.md` is the
  contract.
- Plugins: a package is `~/.jason/plugins/<id>/` with `plugin.yaml`, `main.js` and optional modules;
  the directory name is the id. The manifest is validated against one fixed vocabulary of problem
  codes — package problems reject the whole reload and keep the previous snapshot, the four
  `executable_*` ones are environmental and only make that plugin `unavailable`. Every package has a
  `sha256:` content digest that the child recomputes before it runs anything. The invocation goes to
  the child on stdin, exactly one outcome comes back on stdout, diagnostics are JSON Lines on stderr,
  and the exit code says only whether the protocol completed (0 outcome, 2 usage, 3 rejected,
  4 host failure). Declaration is not permission: capabilities are requested by the manifest and
  granted under `Plugins:Grants:<id>`, resolved and frozen at each load. A failure carries one of
  four classes — `transient` is retried, `permanent` and `validation` are final, and `ambiguous` says
  the provider may already have acted, so it is repeated only where the operation's own contract
  declares that repeating it is safe or that a recovery read makes it safe. An activated load journals
  `plugins_reloaded`; there is no `plugin.invoke` operation.
  `docs/plugins.md` is the contract.
- CLI: prints the exact API response as compact JSON on stdout by default, `--human` renders for
  people, stderr is diagnostics only. Exit codes: 0 success, 1 understood and refused (an API
  business error, or a local act the CLI refused to perform), 2 usage error, 3 runtime unreachable
  or unauthorized. The verb map is one-to-one with API operations with **two declared exceptions**:
  `jason status` answers "can this installation start work?", half of which is not the runtime's to
  know, so no operation can stand behind it; and `jason uninstall` takes this installation off the
  machine — a logon registration, an executable, a PATH entry, the files a deployment recorded — which
  the runtime neither performs nor is asked about. `status` exits 0 or 1 and **never 3** — "I could not
  ask the runtime" is its answer rather than a reason it has none — and its body carries `ready` so an
  agent branches on the body, never on the code. `uninstall` removes only what a receipt names and keeps
  the data directory unless `--purge-data` says otherwise. `CliApp` states both where the rule is
  written, each verb's `--help` publishes its own contract — which checks are required, what is removed
  and what is never touched — and a guard holds the list to these two.
- Persistence: EF Core over SQLite in WAL mode, snake_case tables and columns, integer primary keys
  plus a unique public ULID. The `journal` table is append-only, enforced three times over: no API
  path that changes an entry, an EF interceptor, and database triggers. Schema-less JSON — campaign
  context, contact custom fields, channel data — is stored as JSON text through a converter, and
  channels live in `contact_channels`. Migrations apply automatically on runtime start after a
  pre-migration backup. Never edit a migration after it has been committed; raw SQL such as the
  journal triggers is added to a new migration when it is created. `work_items.result_json` is
  frozen by a trigger once the item is finished; a filtered unique index allows one live attempt
  per item.
- Configuration: strongly typed options only; user overrides in `~/.jason/config/settings.json`,
  environment variables `JASON_*`; the data directory itself comes from `JASON_DATA_DIR` or
  defaults to `~/.jason`. Settings are bound through `IOptions<T>` with validation on start; the
  dispatcher reads `IOptionsMonitor<T>` each tick, so edits to `settings.json` apply live except
  `Dispatcher:MaxParallel`, which sizes the handler pool when the runtime starts, and
  `Dispatcher:Enabled`, which decides at start whether this runtime has a dispatch loop at all.
- Logs are JSON Lines under `~/.jason/logs/` and never contain request bodies, prompts, work-item
  contexts or the capability token.
- Everything in the repository is written in English.

## Working style

- Test-driven: write the failing test, make it pass, commit small. Every behaviour arrives with its
  test.
- Feature branches; one squash-merged pull request per increment, described for an outside reader:
  what changed, why, how it was verified.
- Design notes, plans and scratch files do not belong in the repository.
- Commit messages and pull requests carry no ticket references, links to private systems, or
  tooling attribution trailers.
