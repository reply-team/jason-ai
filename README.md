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
   Runtime skills ship with the runtime under [`skills/runtime`](skills/runtime): five for a
   person's own session, one per moment they ask for something, and one for each of the nine
   roles this runtime seeds, every one of them held to the same launch contract.
   [`skills/business`](skills/business) holds the SDR profession's own knowledge — nine skills and a
   catalogue of 325 named business operations — refreshed on its own cadence rather than with the
   runtime. [`skills/README.md`](skills/README.md) says why the two are kept apart and how a pack is
   installed.
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
| `skills/runtime/` | Skills that teach an agent to operate Jason: the interactive ones, and one per launched role |
| `skills/business/` | Skills that teach an agent the SDR profession, independently of any provider or runtime |
| `.claude-plugin/` | The skills marketplace: offers both packs to an agent harness by relative path. Distinct from `plugins/`, which is the plugin marketplace |
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
rights. Both lines install the **latest release**, and a repository with no published release has no latest:
if they answer 404 there is nothing to install from, and [docs/INSTALL.md](docs/INSTALL.md) §3 — one file built
from this checkout, put on your PATH — is the way in.

**[docs/INSTALL.md](docs/INSTALL.md)** is the whole of it in order — what you need, from a release, from
source, teaching it, asking whether it can work, starting it, and taking it off again — written to be
followed by a person or an agent without reading anything else first.
`docs/release-and-update.md` is the contract for what a release publishes and how a running runtime finds out
that a newer one exists:

```sh
jason update check
jason update apply
jason update status
```

`check` downloads and installs nothing; it asks the feed and prints the answer. `apply` performs the update —
download, drain, stop, swap, start, health check — driven by a ledger written before each step, so a machine
that dies half-way is finished or put back by the next run. `status` says where one stands, and reads the
ledger without needing a runtime.

### Ask an agent to do it

Paste this into an agent that has a shell on the machine you want Jason on. It is the whole prompt — no
preamble, no repository tour:

```text
Install Jason on this machine and tell me whether it can start work.

Follow docs/INSTALL.md in this repository, in order, from section 2.

1. Run the install one-liner for this platform. If the one-liner itself answers 404, there is no
   published release yet. That is a fact about the repository and not a failure of anything, so
   build from source as section 3 describes rather than stopping to report it. If it installs, or
   says Jason is already installed, go on to step 2: a 404 from anything else you look up is not
   that answer.
2. Teach the runtime its roles before you judge it. Run this first and read where it says it
   would write:
   jason skills install --roles-only --dry-run --source .
   Then run the same line without --dry-run. A runtime with no role skills launches every role
   untaught, so this is not an optional step.
3. Ask whether it can work:
   jason status
   Branch on "ready" in the JSON body, never on the exit code. This verb exits 0 or 1 and never
   3, and "the runtime did not answer" is one of its answers rather than an error.
4. For each required check that is not ok, run the repair it prints. A repair that is a jason
   command you can run as it stands. The path check's repair is a line for this machine's own
   shell instead; read section 3's note about login shells before you run it, because a profile
   you append to is read by a login shell and not by your next non-interactive command.
   Optional checks are reported, not repaired: their repairs write what the rule below does not
   allow, into your agent's configuration or into what starts at logon.
5. Stop when ready is true, or when a required check still fails after you have run its repair.
   Report which checks failed, exactly what you ran, and what it changed.

Change nothing on this machine except what these steps write: the executable and its install
directory; this account's PATH entry for it; Jason's data directory, which is where the role
skills go; the system's temporary directory, which the installer downloads into; the directory
the executable unpacks its native libraries into when it first runs, which is under the
temporary directory on Windows and under ~/.net in this home directory on macOS and Linux; and,
only if you build from source, the checkout's own build output and what the .NET SDK writes
into this home directory as it builds — its package cache, and a first-run setup that may add
its own tools directory to this account's PATH. Nothing in your own agent's configuration:
without --roles-only, jason skills install would put skills there too, and that is mine to
decide, so ask.
Do not install anything else. Do not leave a long-running process behind other than through
jason runtime start.
```

### Can this installation start work?

```sh
jason skills install --roles-only
jason skills install --dry-run
jason skills install
jason skills update
jason status
jason status --human
```

`jason skills install --roles-only` puts the role skills where the runtime reads them at every launch — the
half a runtime cannot work without — and writes nowhere else. `jason skills install` does that and puts the
interactive and business packs where your agent harness looks as well, which is its configuration and yours
to allow. Both print the target roots before they write anything, and both refuse a deployment the runtime
would later refuse to launch, rather than writing one.
[`skills/README.md`](skills/README.md) is the contract both verbs keep.

One question, answered check by check. It is one of four names in this CLI with no API operation behind
it — `status` and `uninstall`, and the `skills` and `update` nouns — and it cannot be otherwise: half of what
it reports is not the runtime's to know — an executable on PATH,
another vendor's CLI and whether it answers, files in a folder in your home directory. No API operation can
answer "am I ready to work?", because the runtime is not the authority on the machine it runs on.

| Required — a failure exits 1 | Optional — absent never fails |
|---|---|
| the runtime answers | a provider plugin is installed |
| migrations are applied | a route is configured |
| the plugin registry is alive (zero plugins is alive) | a binding names an account |
| role skills are present, named after their directories and within the runtime's live cap | a provider CLI **you name** answers |
| `jason` resolves on PATH — in this shell, or in every shell started from now on — and which file answers | the skill packs are deployed to an agent harness |
| | autostart is registered |

**It exits 0 or 1 and never 3.** Exit 3 means "I could not ask the runtime", and this is the verb whose whole
job is to answer when the runtime cannot be asked — so a runtime that is down is a failed check rather than an
error envelope. The JSON body carries `ready` and a state per check, so an agent branches on the body rather
than on the code. A check reports a fact and, where there is one, the command that repairs it; it never reads
a log and never explains a failure, which is what keeps it from growing into a diagnostics verb.

The provider check runs only a program **you** name — `jason status --provider-cli <program>`, run as
`<program> --version` with a ten-second bound. Which provider you use is yours, and a readiness check that knew
one vendor's command by heart would be this product naming a vendor in its own sources, which it does not. It
names the exact command it ran in its own output, and prints no credential — not the key, not a prefix of it,
not its length.

### Taking it off again

```sh
jason uninstall --human --dry-run
jason uninstall
jason uninstall --purge-data --yes
```

**Skills go only by receipt.** Every root a deployment wrote into carries a record listing each path written
there, and this verb removes those skills and nothing else — a skill you copied into a harness by hand has no
receipt, so it is removed by nobody, including this. The rest goes by rule, in a fixed order: the logon
registration first, so a logon part-way through cannot start what is going; then the runtime, and if it will
not stop, nothing after that is removed; then the recorded skills; then the PATH entry, only where the
installer wrote it and its directory holds nothing but Jason; then the executable, its install directory and
the native libraries it unpacked. A recorded file whose bytes have changed since is
reported and kept unless `--force` says otherwise.

**The data directory is kept** — the database, the settings, the plugins, the logs and the work directories
are the record of what you did — and the verb says in one line that it kept it and where. `--purge-data`
removes what Jason keeps there, and the directory once nothing else is in it; in the machine shape the word
has to be said in advance, because there is nobody there to be asked. Like `status`, this verb has no API
operation behind it: removing Jason from a machine is not something the runtime performs or is asked about.
[docs/INSTALL.md](docs/INSTALL.md) §7 is the fuller account.

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
registered under, and nothing supervises it afterwards — a runtime that stops stays stopped until
somebody starts it. On Windows `enable` and `disable` have to be run from an elevated prompt, and
standard accounts are unsupported in this version. `docs/release-and-update.md` §8 says what each
platform registers, what it costs, and what only a hand check can prove.

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
into each attempt's own work directory. The packs contribute different files to it — this repository's
runtime pack the `SKILL.md` holding the launch contract, a business pack the profession's method beside
it — so that installing one never overwrites the other. What a role *remembers* is a **role note** — one document per
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
| [`reply-team/reply-skills`](https://github.com/reply-team/reply-skills) | **Formerly** the home of the skills packs. Both halves now live here: the runtime skills in [`skills/runtime`](skills/runtime), re-authored around the Runtime API, and the business skills in [`skills/business`](skills/business), moved across unchanged and then held to this repository's own guards. The Reply-specific half belongs with `reply-cli` |

## Naming

“Jason AI” is used descriptively here; the repository identity is `jason-ai`. Final public
product naming may change — the architecture does not depend on it.

## License

[MIT](LICENSE)
