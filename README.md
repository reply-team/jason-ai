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
> there and then. **Provider operations now run through routed plugins**: a route says which plugin
> performs an operation for a campaign and which account of that provider it works in, the claim
> decides everything that could refuse the work before a child process exists, the plugin performs it
> in its own process, and what ran is pinned to the attempt — the package, its digest, both snapshots
> and the account by identity. **No provider is built in.** One arrives as a plugin, and a fresh
> runtime routes nothing anywhere until somebody writes a route
> ([docs/routing.md](docs/routing.md)); an operation that needs a person's approval still fails
> closed, because nothing here can ask for one. **The first such plugin is in this repository**:
> [`plugins/reply/`](plugins/reply/README.md) performs all three published operations against a Reply
> account through the Reply CLI, which owns the credential — so an operator installs that CLI, signs in
> with it, copies the package into `~/.jason/plugins/`, grants it that one program, and writes the
> route. **One run of the whole of it** — an empty installation brought to a routed plugin, one enrolment
> approved by a person and performed against the provider, and the history it leaves — is written up in
> [docs/golden-path.md](docs/golden-path.md), and every command on that page is executed by a test. The runtime
> is being built in the open, one self-contained increment at a time; no dates, no roadmap promises.

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
   [Reply.io](https://reply.io) is the intended default execution provider (through
   [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli)) — supported and
   commercial, but never hard-coded: no provider ships configured, the same public plugin mechanism
   works for any other, and writing one against it is documented in
   [docs/plugins.md](docs/plugins.md).

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

## Installing

```sh
curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh
```

```powershell
irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1 | iex
```

Each script works out your platform, verifies the download's SHA-256 before unpacking it, installs the
executable under your own account — `~/.local/bin` on macOS and Linux, `%LOCALAPPDATA%\Programs\jason` on
Windows — and puts that directory on your PATH unless you pass `--no-modify-path`. Nothing needs administrator
rights. **Both lines resolve to the latest release, so until the first release is published they answer 404**;
build from source until then. `docs/release-and-update.md` is the contract for what a release publishes and how
a running runtime finds out that a newer one exists:

```sh
jason update check
jason update apply
jason update status
```

`check` downloads and installs nothing; it asks the feed and prints the answer. `apply` performs the update —
download, drain, stop, swap, start, health check — driven by a ledger written before each step, so a machine
that dies half-way is finished or put back by the next run. `status` says where one stands, and reads the
ledger without needing a runtime.

## Building from source

Requires the .NET SDK version pinned in `global.json`. From the repository root:

```sh
dotnet build runtime/Jason.slnx
dotnet test --solution runtime/Jason.slnx
dotnet run --project runtime/src/Jason.App -- --version
```

`jason runtime start` launches the runtime in the background and prints the instance it ended up
talking to; `jason runtime stop` asks that instance to shut down and returns only once it has
really gone. Nothing is registered until you ask — starting the service is something you, your
agent, or a registration you made yourself did. `jason runtime run` keeps the runtime in the
foreground instead, and `jason runtime status` asks it for `system.info` through the Runtime API.

To have the runtime start when you log on, register it once. It carries the data directory it was
registered under, nothing is elevated, and nothing supervises it afterwards — a runtime that stops
stays stopped until somebody starts it. `docs/release-and-update.md` §8 says what each platform
registers and what only a hand check can prove.

```sh
jason runtime autostart enable
jason runtime autostart status
jason runtime autostart disable
```

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

Which agent host runs a piece of AI work — the profile that names it, the revisions that make an
attempt's record of it permanent, the order the choice is made in, what a launched agent finds in its
work directory, what a role remembers between runs, and every reason AI work can be blocked while
everything else keeps running — is documented in
**[docs/execution-profiles.md](docs/execution-profiles.md)**:

```sh
jason profile create local-claude --host claude_code --program claude   --deny Write --host-version-verified 2.1.275
jason profile list --human
jason workitem create <campaign-id> --kind ai_role --role researcher --execution-profile local-claude
jason rolenote get <campaign-id> researcher
```

When a campaign is looked at, and by whom, is its own loop: a failure, a rejected approval or an
effect somebody reported puts a **check-in** on the queue, and a cadence does it anyway when nothing
has happened at all. What wakes a manager, how often one is woken, what it is told and what that
costs are documented in **[docs/campaign-manager.md](docs/campaign-manager.md)**:

```sh
jason campaign update <campaign-id> --review-seconds 3600
jason workitem list --campaign <campaign-id> --role manager
```

Jason ships no AI client and holds no model credentials: a profile names a host the user already
installed and authenticated, and has nowhere to put a secret.

What a launched role is taught is a **role skill**: one directory per role, which an operator composes
into `<data>/skills/roles/<role>/` from the packs under [`skills/`](skills) and which the runtime copies
into each attempt's own work directory. What a role *remembers* is a **role note** — one document per
campaign and role, written by that role for its own later runs, and deliberately not authoritative:
where a note disagrees with campaign state, the state is what is true.

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

Which plugin performs an operation for a campaign, which account of that provider it works in, every
reason a claim refuses provider work, and what is recorded about a run, are in
**[docs/routing.md](docs/routing.md)**:

```sh
jason route set --campaign <id> --plugin acme-provider --binding '{"workspace":"latam"}'
jason route resolve --campaign <id> --operation list_membership.add --human
jason route list --human
```

Once a route is in place, provider work is a work item like any other: the operation it asks for,
and the arguments that operation's own contract publishes.

```sh
jason workitem create <campaign-id> --kind provider_op --operation list_membership.add \
  --contact <contact-id> --input '{"list":{"external_id":"lst-q3"},"channel":"email"}'
jason workitem get <work-item-id> --human
```

Work whose operation asks for a person's approval — `campaign.enroll` does — is not performed by the
dispatcher. It waits, with a record of exactly what would be done, until somebody decides:

```sh
jason approval list --human
jason approval get <approval-id> --human
jason approval approve <approval-id> --actor human:you@example.com --reason "checked the list"
```

A launched role that cannot decide something for itself asks a person instead, and the answer wakes the
review that carries the work on:

```sh
jason decision list --human
jason decision get <decision-id> --human
jason decision answer <decision-id> --answer "pause it" --actor human:you@example.com
```

Effects that never went through Jason at all — something a person or an agent did with its own CLI, an
MCP tool or by hand — are told to Jason afterwards and kept as the reporter's assertion, never as work
Jason performed. What admitting one establishes, what it deliberately does not, and how reports are
read back are in **[docs/reports.md](docs/reports.md)**:

```sh
jason report submit --actor human:you@example.com --effect email_sent --tool reply-cli \
  --summary "Sent the intro by hand after the enrolment failed." \
  --campaign <campaign-id> --contact <contact-id> --idempotency-key intro-1
jason report list --campaign <campaign-id> --human
```

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
