# jason-ai

**The open-source Jason AI runtime** — a local-first, vendor-neutral SDR domain runtime that
turns your own AI agents, installed SDR skills, and external execution providers into a
durable outbound-sales operation.

> **Status: pre-implementation.** This repository currently publishes the product's
> [target architecture](docs/architecture.md). The runtime described there is being built in
> the open, incrementally; nothing should be assumed to exist until the code is here. No
> dates, no roadmap promises.

## What this is

A generic LLM or coding agent can *discuss* an outbound goal, but it does not automatically
become a durable SDR operation: it has no authoritative campaign state, no reliable
schedules and retries, no approval enforcement, no recovery, and no operational history that
survives the chat session. Jason is that missing layer — three parts working as one product:

1. **Jason Runtime** — a deterministic, long-running, user-scoped process (.NET, SQLite)
   that owns durable campaign and work state, scheduling, dispatch, approvals, safety
   guardrails, recovery, and observability. Its **Runtime API** is the only authoritative
   boundary; the **Jason CLI** (built for both humans and AI agents) is its client.
2. **Skills** — the semantic layer, published in
   [`reply-team/reply-skills`](https://github.com/reply-team/reply-skills): vendor-neutral
   SDR expertise, role reasoning, and guardrails that teach *your* AI agents how to plan and
   run outbound work through Jason.
3. **Provider adapters** — strict, vendor-neutral canonical operations executed by
   sandboxed-by-convention JavaScript adapter packages in a short-lived host process.
   [Reply.io](https://reply.io) is the configured default execution provider (through
   [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli)) — supported and
   commercial, but never hard-coded: the same public adapter mechanism works for other
   providers.

Your AI stays yours: Jason ships no AI client, no model account, and no model credentials.
Interactive agent sessions drive Jason through skills + CLI; background AI work runs through
agent environments you already own and have authenticated.

## Architecture

The complete product vision and target architecture — components, state authority, the
durable work model, AI roles and execution profiles, canonical operations, the JavaScript
adapter host, accepted decisions, invariants, and the register of deliberately unresolved
design — lives in **[docs/architecture.md](docs/architecture.md)**.

## Ecosystem

| Repository | Role |
|---|---|
| `reply-team/jason-ai` (this repo) | Product entry point; the Runtime, Jason CLI, adapter host, and official Reply adapter will live here |
| [`reply-team/reply-skills`](https://github.com/reply-team/reply-skills) | SDR skills packs: `ai-sdr-core`, `reply-adapter`, `agentic-runtime` |
| [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) | Reply.io provider CLI: auth, profiles, teams, full API v3 access |
| [`reply-team/reply-mcp`](https://github.com/reply-team/reply-mcp) | Remote MCP server exposing curated Reply tools to MCP-capable agents |

## Naming

“Jason AI” is used descriptively here; the repository identity is `jason-ai`. Final public
product naming may change — the architecture does not depend on it.

## License

[MIT](LICENSE)
