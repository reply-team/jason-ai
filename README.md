# jason-ai

**The open-source Jason AI runtime** — a local-first, vendor-neutral SDR domain runtime that
turns your own AI agents, installed SDR skills, and external execution providers into a
durable outbound-sales operation.

> **Status: early implementation.** This repository publishes the product's
> [target architecture](docs/architecture.md) and the first slice of the runtime: one
> executable that hosts the local Runtime API, a CLI that talks to it, and a database layer that
> migrates itself on start. Nothing here runs outreach yet. The runtime is being built in the
> open, one self-contained increment at a time; no dates, no roadmap promises.

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
   providers.

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

`jason runtime run` starts the runtime in the foreground; `jason runtime status` asks it for
`system.info` through the Runtime API and prints the answer as JSON (add `--human` for a
readable summary). The runtime keeps its data under `~/.jason` (override with the
`JASON_DATA_DIR` environment variable).

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
