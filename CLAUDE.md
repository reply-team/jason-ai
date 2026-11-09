# Working in this repository

Jason is a local-first, vendor-neutral SDR domain runtime: a .NET runtime process that owns durable
campaign state and exposes a loopback Runtime API, a CLI that is a pure client of that API,
JavaScript plugins that execute provider operations in a separate plugin-host process, and skills
that teach AI agents the job. `docs/architecture.md` is the design authority for anything not
covered here. Jason is used from a release; nothing here is built, cloned or tested to use it: this
repository is for changing Jason.

## Layout

- `dotnet/` — all C#: `Jason.slnx`, `src/Jason.App` (the single executable; picks the mode from
  argv), `src/Jason.Cli`, `src/Jason.Runtime`, `src/Jason.PluginHost`, `src/Jason.Contracts`,
  `tests/` (one xUnit project per source project).
- `plugins/` — plugin marketplace. `skills/runtime/`, `skills/business/` — skill packs. `docs/` —
  maintained documentation only.
- `ui/` — the browser UI: a React client of the Runtime API, built into the release asset `jason-ui.zip`;
  `ui/README.md` says how it is built and tested.
- `LICENSE` — MIT, for everything in the repository, the runtime included; every archive carries it as `LICENSE`, beside
  the executable and `THIRD-PARTY-NOTICES`.
- `global.json` at the root pins the SDK; run every command from the repository root.

## Commands

Building, testing, migrations, the UI's checks, and running a build of this checkout with its own data, skills and UI:
[`DEVELOPMENT.md`](DEVELOPMENT.md).

## Boundaries that the build enforces

- `Jason.Cli` references only `Jason.Contracts` and the HTTP client. Never `Jason.Runtime`, EF Core
  or SQLite: the CLI never touches the database, it talks to the Runtime API.
- Plugin JavaScript never runs inside the long-lived runtime process; it runs in the plugin-host mode
  of the same executable. `Jason.Runtime` references neither Jint nor `Jason.PluginHost`;
  `Jason.PluginHost` never references `Jason.Runtime`; manifests are read with YamlDotNet in
  `Jason.Runtime` only, so the contracts, the CLI and the host stay YAML-free.
- The dispatcher is deterministic runtime code; it never reads the meaning of a work item's context.
- Every child process is made by `ChildProcess.StartInfo` and started by `ChildProcess.Start`, with no console
  window of its own and no shell, and on Windows with the starting thread's error mode keeping the boxes the start
  itself raises, for a file that is no program image, off the desktop (`StartErrorMode`); a started child's own
  loader errors, a missing DLL say, follow the process error mode it inherits; a guard reads the compiled IL of
  every production assembly and refuses a
  `ProcessStartInfo` made anywhere else, a `Process.Start` anywhere else, a write to `CreateNoWindow` or
  `UseShellExecute`, `Process.Start` from a file name, and a read of a process's `StartInfo`. Reflection,
  expression trees and `new T()` are beyond what it reads.
- Publication: this repository is public. Every file in it, every commit message and every pull request's title and
  body is read publicly, so none of them carries a tracker key outside a branch name, an attribution trailer, a
  pointer to an internal note, plan or system, a link to a private repository, a real person's name or account in the
  text (git's author fields and CODEOWNERS aside), or a secret. `RepositoryHygieneTests` holds every file of the tree
  to the patterns a machine can check, and every link to a path of this repository to a file the tree holds; the
  `hygiene.yml` workflow holds each pull request's added lines, title and body to the same patterns. The rest is the
  author's to keep.
- Package versions live in `dotnet/Directory.Packages.props` only. `THIRD-PARTY-NOTICES` covers every package of
  `Jason.App`'s closure and the runtime packs the executable carries; after a package or SDK change
  `ThirdPartyNoticesTests` is red until `pwsh .github/scripts/Write-ThirdPartyNotices.ps1` writes it again.
- Warnings are errors. Every test runs offline and uses an isolated temporary data directory.
- The UI is one more consumer of the Runtime API, and Jason stays command-line first and agent first. Three tests for
  anything the UI needs from the runtime: it gets a CLI verb that makes sense typed by a person or an agent (the one door
  outside the catalog is the pairing exchange `POST /ui/session`); it returns facts, never views; the runtime never
  knows a screen exists — no route named after a page, no state, preference or cache kept for it, and with `<data>/ui/`
  absent the runtime is unchanged (`UiGuardTests`). A UI token has the run token's rights: one level of trust, the
  machine's user. The UI is tested in three layers (`ui/README.md`, "Testing"): unit tests of dense logic in Node, screen tests against the exchanges the runtime's own tests recorded under
  `docs/api/fixtures/` (`ApiFixtures`, held one-to-one with the files and rewritten only under
  `JASON_UPDATE_FIXTURES=1`), and a browser smoke of the seams alone. Six guards hold the layers: the unit project runs
  without a DOM and may not import React; no `.json` under `ui/` but the configuration; a dependency allowlist; the
  fixture catalog; only the smoke's harness starts processes; one CI job runs all of it.

## Conventions

- Terminology: **plugin** (not adapter), **WorkItem** (not Task), **channels** (not handles).
  Skills, memory files, named preferences, campaign context, role notes and learned practice are six distinct
  knowledge concepts; do not blur them. Membership in a Jason campaign is local; `campaign.enroll` — a later,
  provider-side, approval-gated act — is a different thing.
- Runtime API: RPC style, `POST /v1/{noun}.{verb}` for everything, reads included, with one declared exception:
  `GET /v1/journal.stream`, the journal's lines as they are written over a WebSocket, is a `GET` because a WebSocket
  upgrade is one by its protocol, and `jason journal watch` maps to it one to one. Success is 200
  with a bare DTO; errors are `{"error":{"code","message","retryable"}}` with snake_case codes.
  JSON is snake_case, enums are snake_case strings, timestamps are ISO-8601 UTC, public ids are
  prefixed ULIDs (`cmp_…`, `wi_…`, `att_…`, `rol_…`). Partial patches distinguish absent from null
  (`Optional<T>`); lists paginate with `limit`/`cursor`; every mutating request may carry `actor`
  and `reason`. `campaign.create` and `workitem.create` also take a caller's `idempotency_key`, kept on
  the entity it created and never derived by the runtime or the CLI (`docs/work-execution.md`).
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
  apart. `docs/contracts/README.md` is the contract. The delivery contract, `notification.send`, is a family of its
  own under `docs/contracts/notifications/`, embedded the same way and never in the operation catalog.
- Routing: which plugin performs an operation for a campaign is a route, resolved by a pure function
  over an immutable snapshot — campaign operation override, campaign default, global operation
  override, global default, with exact masking and no fallback to another plugin. Routes are frozen
  by the same reload that freezes plugins: the `Routes` section of `settings.json` is read only
  there and at startup, `route.set`/`route.unset` copy the global half from the active snapshot, and
  a bad route rejects the whole reload. The claim is fail-closed: twenty checks in one published
  order, one code per reason, decided before a child exists, with the attempt kept. A binding names
  an account and never a credential. A provider identifier is pinned once and never overwritten — a
  different value is recorded beside it as a divergence and the attempt still succeeds. Provenance
  is written at the claim and completed at the end; nothing rewrites it. `docs/routing.md` is the
  contract.
- Plugins: a package is `~/.jason/plugins/<id>/` with `plugin.yaml`, `main.js` and optional modules; the
  directory name is the id. The manifest is validated against one fixed vocabulary of problem codes —
  package problems reject the whole reload and keep the previous snapshot, and so does a settings file the
  reload cannot read (`settings_file_unreadable`; one that is not there rejects nothing); the four `executable_*`
  ones and, at a start only, `probe_timed_out` are environmental and only make that plugin `unavailable`.
  Every package has a `sha256:` content digest that the child recomputes before it runs anything. The
  invocation goes to the child on stdin, exactly one outcome comes back on stdout, diagnostics are JSON
  Lines on stderr, and the exit code says only whether the protocol completed (0 outcome, 2 usage, 3
  rejected, 4 host failure). Declaration is not permission: capabilities are requested by the manifest
  and granted under `Plugins:Grants:<id>`, resolved and frozen at each load; a manifest's `any_granted_host` lets a person
  grant a host it does not declare, and `*` still means the declared hosts alone. Configuration is `Plugins:Config:<id>`:
  text only, frozen at each load, handed on stdin and read with `host.config`, a secret of the invocation, never in a
  package. A failure carries one of
  five classes — `transient` is retried, `permanent`, `validation` and `blocked` are final, and `ambiguous` says the
  provider may already have acted, so it is repeated only where the operation's own contract declares
  that repeating it is safe or that a recovery read makes it safe. An activated load journals
  `plugins_reloaded`; there is no `plugin.invoke` operation. `docs/plugins.md` is the contract.
- Inbound sync: a scan step between the lease enforcer and the summon reads each active campaign routed to a plugin
  that reads conversations, and linked there, once per cadence — one `conversation.list` work item per page — and
  writes one reserved `inbound_reply_observed` line per thread that moved — identifiers, moments and the provider's
  flags, never a subject, a preview or a name — said once per campaign by a key. It interprets nothing; the line is a
  default manager trigger. A respond step between it and the summon reads those lines above a per-campaign watermark,
  whichever writer said them, and for a thread the campaign holds by its own pin queues a `conversation.get` and a
  `responder` created waiting on it — one open responder per thread, a further move patched onto its `inbound_reply` — with
  identifiers and moments only, and never an idempotency key; its `conversation.list` items sit ten above
  `Responder:Priority`, so the sends wait and detection does not. `docs/inbound-sync.md` is the contract.
- Events: a third contract family, `events.read` under `docs/contracts/events/`, embedded like the delivery contract
  and never in the operation catalog. A provider that declares `contracts.events` reads an account by its own cursor,
  which the runtime stores per plugin and binding identity and never parses; a read pins nothing. What is read lands
  in an append-only store — `event_sources`, `events`, `event_attributions`, under the journal's three guards — each
  event once by a key per plugin, and an event no pin matches is kept. An event reaches a campaign only by the campaign
  identifier its operations pinned, decided once when it is stored, as a reserved line: a thread the other side moved as
  `inbound_reply_observed` under inbound sync's key, every other kind as `provider_event_observed`. A scan step
  between the lease enforcer and inbound sync hands one read at a time to a reader off the scan — never a work item or a
  handler slot — for every account a route binds, on `Dispatcher:EventsCadenceSeconds`, thirty days deep, a span older
  than that said once as `events_gap_observed`; inbound sync skips a campaign routed to such a plugin only where an
  event would reach it by the campaign-pin rule — one function, which the store attributes by — and only while that
  account's last read that succeeded started within the shorter of two cadences and the look-back. A person may ask for a
  span of an account to be read again (`events.backfill`: at most 366 days back, eight waiting an account), read only in
  a scan where every account is current, by a cursor of its own that never moves the account's; `events.signal` names an
  account and nothing else and brings its next read forward, never within a minute of the last; `events.sources` is the
  accounts' record and never answers a cursor. `docs/events.md` is the contract.
- Notifications: a parked approval, a raised question, failed protective work, an account whose events were not
  read, an open provider block and an item held at a daily limit of launches are notified as a pointer —
  counts and where to look — by a scan step after the dispatch, delivered off the scan and recorded in a reserved `notification_sent` line.
  `docs/notifications.md` is the contract.
- Projects and preferences: every campaign is in exactly one project — the default one, which every installation has,
  unless it names another — and a project's slug is unique and never changes. The client is the installation and has
  no entity; what a person states about it is a named preference, a fourth contract family under
  `docs/contracts/preferences/` embedded like the others and never in the operation catalog: one value per key for the
  installation, checked against the key's schema, set and unset by a named person only, and journaled by its size and
  hash, never its value. The client, each project and each campaign have a folder under the harness root, made at the
  first create that needs it, with a marker naming its level and id and the person's memory file, `AGENTS.md`, beside a
  one-line import file for every host that reads a file of another name (`CLAUDE.md` for Claude Code); the runtime
  writes structure only — never a word of a memory file, never a delete, never
  `git` — and moves a campaign's folder by one rename when the campaign changes project, or refuses the move. A path
  is recorded on the entity; a folder is known by its marker, never its name, and a folder the person moves is
  recorded again only by `project.link`, which reads the project's folder and each folder directly in its
  `campaigns/` and never takes a campaign's project from a folder. A verb that takes a project and is given none takes
  it from the nearest marker above the folder the CLI runs in, when that folder is the one recorded, and says so on
  stderr; never a campaign. `docs/projects.md` is the contract.
- CLI: prints the exact API response as compact JSON on stdout by default, `--human` renders for
  people, stderr is diagnostics only. Exit codes: 0 success, 1 understood and refused (an API
  business error, or a local act the CLI refused to perform), 2 usage error, 3 runtime unreachable
  or unauthorized. The verb map is one-to-one with API operations with **eleven declared exceptions**:
  `jason status` answers "can this installation start work?", half of which is not the runtime's to
  know, so no operation can stand behind it; `jason uninstall` takes this installation off the
  machine — a logon registration, an executable, a PATH entry, the files a deployment recorded — which
  the runtime neither performs nor is asked about; the `skills` and `update` nouns and the
  `jason ui install` and `jason plugin install` verbs change this installation's files — deploying skills,
  replacing the executable and redeploying the recorded skills, placing the release's UI as the default
  build, placing a plugin package the release publishes — rather than ask the runtime anything (each is the
  one such verb under a noun whose other verbs are operations, `plugin install` asks only the reload
  `plugin reload` asks and never starts a runtime, and the verb-map tests hold the `ui` group to its three
  operations and `install` and the `plugin` group to its two and `install`); `jason logs` reads the
  installation's log files, which the runtime writes and is never asked about; `jason session open` starts the
  person's own agent host — the one the named preference `agent.interactive_host` names — in a new terminal window at
  the campaign's folder with the item's brief as its first input, a terminal being no runtime's to start or know about,
  so the runtime is asked only what `approval get --prompt` and `decision get --prompt` ask and `preference.list`, and
  Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is the host's own
  permission settings' to decide (the verb-map tests hold the `session` noun to `open` alone); `jason export`
  writes this installation's files — the database, the settings, the plugins, the role skills, the custom UI, the logs and
  the harness root's tree — into one archive, which is a true copy only while the runtime is stopped and so no operation of the
  runtime's can stand behind it: of the runtime it asks only to stop and to start, and only under `--stop`;
  `jason import` places such an archive on this machine cold, the database among them, which no runtime can take
  while it runs on them: of the runtime it asks to stop, only under `--stop`, to start, and `project.link` once for each
  project folder it placed; and `jason doctor` collects one installation into one file, half of which is not the
  runtime's to know — of the runtime it asks only what the runtime says about itself, its roles,
  profiles, routes and subscriptions, and the journal and work-item pages it counts, keeping their metadata and
  dropping their values. `status`, `logs` and
  `doctor` exit 0 or 1 and **never 3** — `logs` never asks the runtime, and for the other two "I could
  not ask the runtime" is an answer rather than a reason there is none — and the body of `status`
  carries `ready` so an agent branches on the body, never on the code. `uninstall` removes skills only
  by receipt and the rest of the installation by rule, and keeps the data directory unless
  `--purge-data` says otherwise. `doctor` lists what it would write before it writes — `--dry-run`
  lists it and writes nothing — reaches no network and uploads nothing; only `--include-journal` adds
  what the journal entries say: campaign names and fields as they changed, project names as they changed, the folders a project link
  recorded, campaign and work-item
  contexts as they changed, result formats, the addresses and numbers put on or taken off the
  do-not-contact register, the accounts a campaign's routes bind, the options decisions were answered
  with, the errors attempts ended with, provider identifiers, the verdicts roles recorded on replies and the decisions
  whose answers they carry, and the standing approvals
  people set: their categories, reactions, goals and limits, the effect, tool and operation a report
  names, the failure text and receipts notification channels reported, entries people and agents wrote
  themselves, the names actors gave, and the reasons they typed — and it prints a warning; a plugin's configuration under
  `Plugins:Config` travels under no flag, withheld whole, keys and values; the runtime's
  own report (`system_info`) travels projected, withholding channel detail, presence detail outside the
  runtime's own words, skill problem text and the release-notes URL, and the bundle says so; and before
  anything is written the home directory becomes `~`, a data directory other than `~/.jason` `<data>`
  wherever it stands, a different one the runtime keeps its data in a mark of its own, the harness root
  `<harness_root>` wherever it stands, the executable's folder outside home `<install_dir>` unless a package
  manager's prefix holds it, the account's user, machine and domain names and SIDs marks of their own, every
  plugin id, role name and skill pack name Jason did not ship an alias per distinct name (`<plugin-N>`,
  `<role-N>`, `<skill-pack-N>`), and a skill source other than the public repository, with its ref unless
  that is a release tag, `<skill-source>`.
  `CliApp` states all eleven where the rule is written, each verb's `--help`
  publishes its own contract — which checks are required, what is removed, what is never touched and
  what a bundle never carries — and guards hold the list to these eleven. `export` refuses while the runtime runs unless `--stop`
  is given, never writes over a file, carries the settings file with every secret in it and says so, and refuses a
  link inside a carried place, following one at its top and judging it where it leads, or a name another system cannot
  hold before anything is written. `import` refuses, before anything is written, an
  archive that cannot be read, a name that escapes, a digest that is not the listed one, an archive a newer Jason wrote
  and, without `--replace`, an installation already here; it deletes nothing, moving what is here aside, and rewrites
  in the settings file only the paths under the other machine's home, data directory and harness root.
- Persistence: EF Core over SQLite in WAL mode, snake_case tables and columns, integer primary keys
  plus a unique public ULID. The `journal` table is append-only, enforced three times over: no API
  path that changes an entry, an EF interceptor, and database triggers. Schema-less JSON — campaign
  context, contact custom fields, channel data — is stored as JSON text through a converter, and
  channels live in `contact_channels`. Migrations apply automatically on runtime start after a
  pre-migration backup; a migration that fails puts that backup back and the start fails saying so,
  and older backups are pruned only after a migration has succeeded. Never edit a migration after it
  has been committed; raw SQL such as the journal triggers is added to a new migration when it is
  created. `work_items.result_json` is frozen by a trigger once the item is finished; a filtered
  unique index allows one live attempt per item.
- Configuration: strongly typed options only; user overrides in `~/.jason/config/settings.json`,
  environment variables `JASON_*`; the data directory itself comes from `JASON_DATA_DIR` or
  defaults to `~/.jason`, and the harness root from `Harness:Root` or defaults to `~/Jason` — an absolute path, never
  the home directory, a folder holding it or the top of a drive, nor the data directory, a folder inside it or one
  holding it, read at every create and move; a test runtime is given a home of its own, so no test makes a folder in a
  real one. Settings are bound through `IOptions<T>` with validation on start; the
  dispatcher reads `IOptionsMonitor<T>` each tick, so edits to `settings.json` apply live except
  `Dispatcher:MaxParallel`, which sizes the handler pool when the runtime starts, and
  `Dispatcher:Enabled`, which decides at start whether this runtime has a dispatch loop at all. A live
  edit never stops the runtime: a refused edit keeps the last good settings and is logged by path and
  type, and a start with a malformed or mistyped file still fails, saying where — on stderr under
  `jason runtime run`, and after "It said:" in what `jason runtime start` answers: the line and byte
  of a malformed file, the setting's path and expected type for a mistyped one.
- Logs are JSON Lines under `~/.jason/logs/` and never contain request bodies, prompts, work-item
  contexts or the capability token: no text a person gives Jason — a context, a field, a brief, an
  operation's argument, a package's manifest, a refused setting, a reason — reaches them, held by a
  guard over every message template the runtime writes and by a run of the real executable that reads
  its own log back; the paths the installation runs from do, and a bundle rewrites them. `jason logs`
  reads them without the runtime; `jason doctor` carries a bounded slice of them.
- Everything in the repository is written in English.

## Working style

- Test-driven: write the failing test, make it pass, commit small. Every behaviour arrives with its
  test.
- Feature branches; one squash-merged pull request per increment, described for an outside reader:
  what changed, why, how it was verified.
- Design notes, plans and scratch files do not belong in the repository.
- Commit messages and pull requests carry no ticket references, links to private systems, or
  tooling attribution trailers.
