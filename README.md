# jason-ai

**The open-source Jason AI runtime** — a local-first, vendor-neutral SDR domain runtime that
turns your own AI agents, installed SDR skills, and external execution providers into a
durable outbound-sales operation.

> **Status: early implementation.** This repository publishes the product's
> [target architecture](docs/architecture.md) and the runtime as it stands: one executable that
> hosts the local Runtime API, a CLI that talks to it, and a database layer that migrates itself
> on start. The runtime now manages campaigns, their context and their append-only journal, a
> contact directory with campaign membership, a suppression list, and **work items: units of work
> inside a campaign that a deterministic dispatcher claims under a lease and hands to a role's
> entry command, which reports back through the API** — all through the CLI and the Runtime API.
> **Plugin packages** are validated against the manifest schema, listed with their content digest
> and their requested-versus-granted capabilities, and swapped in atomically by an explicit reload;
> the plugin host runs one invocation of a package's JavaScript in its own process under the Host
> SDK. **Canonical operation contracts** are published under [`docs/contracts/`](docs/contracts) —
> one machine-readable document per operation, embedded into the runtime that enforces it — and a
> work item naming a provider operation is measured against its contract when it is created: an
> operation this build publishes no contract for, or arguments that do not satisfy one, are refused
> there and then. Routing a work item to a plugin arrives with the next increment; no provider
> operation runs yet, so nothing here sends outreach. The runtime is being built in the open, one
> self-contained increment at a time; no dates, no roadmap promises.

## What this is

A generic LLM or coding agent can *discuss* an outbound goal, but it does not automatically
become a durable SDR operation: it has no authoritative campaign state, no reliable
schedules and retries, no approval enforcement, no recovery, and no operational history that
survives the chat session. Jason is that missing layer — three parts working as one product:

1. **Jason Runtime** — a deterministic, long-running, user-scoped process (.NET, SQLite)
   that owns durable campaign and work state, scheduling, dispatch, approvals, safety
   guardrails, recovery, and observability. Its **Runtime API** is the only authoritative
   boundary; the **Jason CLI** (built for both humans and AI agents) is its client.
2. **Skills** — the semantic layer: vendor-neutral SDR expertise, role reasoning, and
   guardrails that teach *your* AI agents how to plan and run outbound work through Jason.
   Runtime skills ship with the runtime under [`skills/runtime`](skills/runtime); the SDR
   profession's knowledge lives under [`skills/business`](skills/business).
3. **Provider plugins** — strict, vendor-neutral canonical operations executed by JavaScript
   plugin packages in a short-lived plugin-host process started from the same executable.
   [Reply.io](https://reply.io) is the configured default execution provider (through
   [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli)) — supported and
   commercial, but never hard-coded: the same public plugin mechanism works for other
   providers, and writing one against it is documented in [docs/plugins.md](docs/plugins.md).

Your AI stays yours: Jason ships no AI client, no model account, and no model credentials.
Interactive agent sessions drive Jason through skills + CLI; background AI work runs through
agent environments you already own and have authenticated.

## Architecture

The complete product vision and target architecture — components, state authority, the
durable work model, AI roles and execution profiles, canonical operations, the JavaScript
plugin host, accepted decisions, invariants, and the register of deliberately unresolved
design — lives in **[docs/architecture.md](docs/architecture.md)**.

## Repository layout

| Path | Contents |
|---|---|
| `runtime/` | All C#: the solution, `src/Jason.App` (the single executable; picks CLI, runtime-service or plugin-host mode from its arguments), `src/Jason.Cli`, `src/Jason.Runtime`, `src/Jason.PluginHost`, `src/Jason.Contracts`, and `tests/` |
| `plugins/` | Plugin marketplace: one folder per plugin with its manifest and JavaScript |
| `skills/runtime/` | Skills that teach an agent to operate Jason |
| `skills/business/` | Skills that teach an agent the SDR profession |
| `docs/` | Maintained documentation |

## Building from source

Requires the .NET SDK version pinned in `global.json`. From the repository root:

```sh
dotnet build runtime/Jason.slnx
dotnet test --solution runtime/Jason.slnx
dotnet run --project runtime/src/Jason.App -- --version
```

`jason runtime start` launches the runtime in the background and prints the instance it ended up
talking to; `jason runtime stop` asks that instance to shut down and returns only once it has
really gone. Nothing about autostart is registered anywhere — starting the service is always
something you or your agent did. `jason runtime run` keeps the runtime in the foreground
instead, and `jason runtime status` asks it for `system.info` through the Runtime API.

Campaigns, the people in them, and the work to be done about them are managed with one verb per
API operation:

```sh
jason campaign create --name "Latin America"
jason campaign add-contacts <id> --file contacts.json --match-by email
jason campaign start <id>
jason role list
jason workitem create <id> --kind ai_role --role researcher
jason workitem list --campaign <id> --status failed --status expired
```

Every verb prints the exact API response as compact JSON on stdout (add `--human` for a readable
rendering). The runtime keeps its data under `~/.jason` (override with the `JASON_DATA_DIR`
environment variable).

How work items are claimed, launched and reported on — the lifecycle, the launch envelope an
executor is handed, the operations it reports through, and the settings that pace all of it — is
documented in **[docs/work-execution.md](docs/work-execution.md)**.

What a plugin package is, every rule its manifest is held to, what the Host SDK offers and under
which limits, how capabilities are granted, and the invocation protocol between the runtime and the
plugin host, are documented in **[docs/plugins.md](docs/plugins.md)**:

```sh
cp -r ./my-plugin ~/.jason/plugins/
jason plugin reload --reason "installed my plugin"
jason plugin list --human
```

The canonical operations a plugin implements — one document per operation, the schema dialect they are
written in, the published argument and answer fixtures, and where Jason's strict subset departs from
the vendor-neutral contract it is seeded from — are in **[docs/contracts/](docs/contracts/README.md)**.

## Ecosystem

| Repository | Role |
|---|---|
| `reply-team/jason-ai` (this repo) | Product entry point: the Runtime, Jason CLI, plugin host, the official Reply plugin, and the runtime and business skills |
| [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) | Reply.io provider CLI: auth, profiles, teams, full API v3 access, and the Reply-specific skills |
| [`reply-team/reply-mcp`](https://github.com/reply-team/reply-mcp) | Remote MCP server exposing curated Reply tools to MCP-capable agents |
| [`reply-team/reply-skills`](https://github.com/reply-team/reply-skills) | Earlier home of the skills packs; its content is moving into this repository and into `reply-cli` |

## Naming

“Jason AI” is used descriptively here; the repository identity is `jason-ai`. Final public
product naming may change — the architecture does not depend on it.

## License

[MIT](LICENSE)
