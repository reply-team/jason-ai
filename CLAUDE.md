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
- Add a migration: `dotnet tool restore` once, then
  `dotnet ef migrations add <Name> --project runtime/src/Jason.Runtime --startup-project runtime/src/Jason.Runtime --output-dir Persistence/Migrations`
- Publish: `dotnet publish runtime/src/Jason.App -c Release -r <rid> --self-contained -p:PublishSingleFile=true`

## Boundaries that the build enforces

- `Jason.Cli` references only `Jason.Contracts` and the HTTP client. Never `Jason.Runtime`, EF Core
  or SQLite: the CLI never touches the database, it talks to the Runtime API.
- Plugin JavaScript never runs inside the long-lived runtime process; it runs in the plugin-host mode
  of the same executable.
- Package versions live in `runtime/Directory.Packages.props` only.
- Warnings are errors. Every test runs offline and uses an isolated temporary data directory.

## Conventions

- Terminology: **plugin** (not adapter), **WorkItem** (not Task), **channels** (not handles).
  Skills, campaign context, role notes and learned practice are four distinct knowledge concepts;
  do not blur them.
- Runtime API: RPC style, `POST /v1/{noun}.{verb}` for everything, reads included. Success is 200
  with a bare DTO; errors are `{"error":{"code","message","retryable"}}` with snake_case codes.
  JSON is snake_case, enums are snake_case strings, timestamps are ISO-8601 UTC, public ids are
  prefixed ULIDs (`cmp_…`, `wi_…`).
- CLI: prints the exact API response as compact JSON on stdout by default, `--human` renders for
  people, stderr is diagnostics only. Exit codes: 0 success, 1 API business error, 2 usage error,
  3 runtime unreachable or unauthorized.
- Persistence: EF Core over SQLite in WAL mode, snake_case tables and columns, integer primary keys
  plus a unique public ULID. Migrations apply automatically on runtime start after a pre-migration
  backup. Never hand-edit a generated migration; add a new one.
- Configuration: strongly typed options only; user overrides in `~/.jason/config/settings.json`,
  environment variables `JASON_*`; the data directory itself comes from `JASON_DATA_DIR` or
  defaults to `~/.jason`.
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
