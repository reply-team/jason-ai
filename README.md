# jason-ai

**Jason AI** — an AI SDR harness: a local-first, vendor-neutral SDR domain runtime, distributed as one
self-contained executable per platform, with open skills, contracts and plugins, that turns your own AI agents,
installed SDR skills, and external execution providers into a durable outbound-sales operation.

> **Alpha.** Jason is early software: it does what this page says, and what it does changes from one release to the
> next.
>
> **Approve real sends only in a test team.** Work that reaches real people waits for a person's approval; give it
> only in a provider account you keep for testing, never in the one your company sells from.
>
> **What leaves your machine:** what the plugins you install send to their providers, and what the AI agents you
> configure Jason to start send to theirs; the runtime's check for a newer release — five minutes after it starts
> and then once a day, at the default settings, a single `GET` that identifies only the runtime's version, including
> its build (`Update:CheckEnabled` set to `false` turns it off); and the requests you start yourself — the install
> scripts and `jason update`, and `jason skills install` and `update`. The check, the install scripts and
> `jason update` go to `jasonharness.ai`, an address Reply.io operates, which redirects them to this repository's
> releases; the skills come from this repository directly. What that address keeps, and for how long, is
> section 7.2 of the [runtime's licence](LICENSE-RUNTIME.md). Nothing else, and no telemetry: the file
> `jason doctor` writes stays on your machine unless you send it.

## Installing

```sh
curl -fsSL https://jasonharness.ai/releases/latest/download/install.sh | sh
```

```powershell
irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex
```

By installing Jason you accept the Jason Runtime Licence: [LICENSE-RUNTIME.md](LICENSE-RUNTIME.md). If you do not
accept it, do not install Jason.

A one-liner installs the executable. [docs/INSTALL.md](docs/INSTALL.md) is the rest, in order — teaching it its
skills, without which every role it launches runs untaught, asking whether it can work, and starting it — for a
person or an agent.

### Ask an agent to do it

Start the agent session for the install without external connectors: the prompt below needs none.
On Claude Code that is `--strict-mcp-config`. This is about installing only. Connectors in your agent's SDR work
are yours to use.

If your agent's default mode is auto, expect one of two things at the install: it asks you before the download
runs, or, where nothing can ask you, it refuses and says so. Both are right. Say yes, or type the one-liner above
yourself; never let it work around a refusal. A mode that asks before every command is the other way: on Claude
Code, `--permission-mode manual` (`default` before 2.1.200), or shift-tab in an open session; in any other agent,
the mode that asks before it runs a command.

Paste this into an agent that has a shell on the machine you want Jason on. It is the whole prompt — no
preamble, no repository tour:

```text
Install Jason on this machine. The instructions are
<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/INSTALL.md>. Read all of it
first, the whole file word for word rather than a summary of it.

Before you run anything, ask me one question in plain words: what you will install and from
where, what it writes and where (the executable and its directory, the PATH entry, Jason's data
directory, the role skills), and each act the instructions mark "Ask first", offered to me one by
one as a choice. Offer me the other way too: switching this session to a mode that asks me before
every command. Then do the steps yourself, in order from section 2, doing only what I said yes to.

Jason is downloaded from its releases at <https://jasonharness.ai/releases/>, which redirect to
<https://github.com/reply-team/jason-ai/releases>; install it from there, or with the package
section 2 names for this platform.

By installing Jason you accept the Jason Runtime Licence:
<https://github.com/reply-team/jason-ai/blob/main/LICENSE-RUNTIME.md>. If you do not accept it, do
not install Jason.

At the end, tell me whether Jason can start work, what the next step is, and exactly what you ran
and what it changed. If it can, ask me once whether I want a synthetic walk to meet Jason first or
my real work at once.
```

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

What runs today is one executable that hosts the local Runtime API, a CLI that talks to it, and a database layer
that migrates itself on start. The runtime manages campaigns, their context and their append-only journal, a contact
directory with campaign membership, a suppression list, and **work items: units of work inside a campaign that a
deterministic dispatcher claims under a lease and hands to a role's entry command, which reports back through the
API** — all through the CLI and the Runtime API. **Plugin packages** are validated against the manifest schema,
listed with their content digest and their requested-versus-granted capabilities, and swapped in atomically by an
explicit reload; the plugin host runs one invocation of a package's JavaScript in its own process under the Host
SDK. **Canonical operation contracts** are published under [`docs/contracts/`](docs/contracts) — one
machine-readable document per operation, embedded into the runtime that enforces it — and a work item naming a
provider operation is measured against its contract when it is created: an operation this build publishes no
contract for, or arguments that do not satisfy one, are refused there and then. **Provider operations run through
routed plugins**: a route says which plugin performs an operation for a campaign and which account of that provider
it works in, the claim decides everything that could refuse the work before a child process exists, the plugin
performs it in its own process, and what ran is pinned to the attempt — the package, its digest, both snapshots and
the account by identity. **No provider is built in.** One arrives as a plugin, and a fresh runtime routes nothing
anywhere until somebody writes a route: work with no route fails at the claim with `no_route`. An operation that
needs a person's approval is parked until a person decides, and nothing runs but what they approved. **The first
such plugin is in this repository**: [`plugins/reply/`](plugins/reply/README.md) performs all eighteen published
operations against a Reply account through the Reply CLI, which owns the credential — so an operator installs that
CLI, signs in with it, unpacks the package from the release into `~/.jason/plugins/`, grants it that one program,
and writes the route. **The wiring check** — an empty installation brought to a routed plugin, one enrolment of
your own address approved by you and performed against the provider, and the history it leaves — is
[docs/golden-path.md](docs/golden-path.md), and every command on that page is executed by a test. It proves the
wiring and is not the real work, which begins with a campaign planned with the business skills
([`campaign-planning`](skills/business/campaign-planning/SKILL.md) first) and worked by Jason's roles. It is optional:
a synthetic walk, offered once when an installation is ready, which you may skip for the real work at once — and that
begins with you telling your agent about your first campaign in your own words. Releases arrive one self-contained
increment at a time; no dates, no roadmap promises.

## Architecture

One executable, three modes: the runtime service, which owns campaign and work state in SQLite and is the only thing
that writes it; the CLI, a pure client of the runtime's loopback API; and the plugin host, a short-lived process in
which a plugin's JavaScript runs, so that it never runs inside the long-lived runtime. The runtime's design and its
source are not published. What a person, an agent or a plugin author needs from them is here:
[docs/INSTALL.md](docs/INSTALL.md), [docs/plugins.md](docs/plugins.md), [docs/ui.md](docs/ui.md) for the browser UI, the contracts under
[docs/contracts/](docs/contracts/README.md), and the skills under [`skills/`](skills).

## Repository layout

| Path | Contents |
|---|---|
| `plugins/` | Plugin marketplace: one folder per plugin with its manifest and JavaScript |
| `skills/runtime/` | Skills that teach an agent to operate Jason: the interactive ones, and one per launched role |
| `skills/business/` | Skills that teach an agent the SDR profession, independently of any provider or runtime |
| `.claude-plugin/` | The skills marketplace: offers both packs to an agent harness by relative path. Distinct from `plugins/`, which is the plugin marketplace |
| `docs/` | The install manual, the wiring check, the plugin author's guide, the browser UI, the operation contracts, and the Runtime API's recorded exchanges under `docs/api/` |
| `install/` | The install scripts every release attaches, and the update feed's schema |
| `ui/` | The browser UI the runtime serves: a Preact client of the Runtime API; `npm run package` builds it into `jason-ui.zip` |
| `VERSION` | The runtime version this tree describes |

The runtime's source is not here. Each release attaches the runtime for every platform, built from that source at the
release's version, and this tree is published from the same release.

## The installation

Each install script works out your platform, verifies the download's SHA-256 before unpacking it, installs the
executable under your own account — `~/.local/bin` on macOS and Linux, `%LOCALAPPDATA%\Programs\jason` on
Windows — with its licence and the third-party notices beside it, as `jason-LICENSE` and
`jason-THIRD-PARTY-NOTICES`, and puts that directory on your PATH unless you pass `--no-modify-path`. Nothing
needs administrator rights. Both one-liners install the **latest release**. If either answers 404, the release
could not be reached — through the address or on GitHub — and that is not a failure of your machine: report it
as it stands rather than run the line again, and build nothing instead; there is nothing here to build
([docs/INSTALL.md](docs/INSTALL.md) §3).

**[docs/INSTALL.md](docs/INSTALL.md)** is the whole of it in order — what you need, from a release, from
source, teaching it, asking whether it can work, starting it, and taking it off again — written to be
followed by a person or an agent without reading anything else first. A running runtime finds out by itself that
a newer release exists, and these ask and act:

```sh
jason update check
jason update apply
jason update status
```

`check` downloads and installs nothing; it asks the feed and prints the answer. `apply` performs the update —
download, drain, stop, swap, start, health check — driven by a ledger written before each step, so a machine
that dies half-way is finished or put back by the next run, and it ends by redeploying the skills this machine
recorded at the new release's tag; an update applied by a build before 0.12.0 needs one `jason skills update` after
it. `status` says where one stands, and reads the ledger without needing a runtime.

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

One question, answered check by check. It is one of six names in this CLI with no API operation behind
it — `status`, `uninstall`, `logs` and `doctor`, and the `skills` and `update` nouns — and it cannot be
otherwise: half of what it reports is not the runtime's to know — an executable on PATH,
another vendor's CLI and whether it answers, files in a folder in your home directory. No API operation can
answer "am I ready to work?", because the runtime is not the authority on the machine it runs on.

| Required — a failure exits 1 | Optional — absent never fails |
|---|---|
| the runtime answers | a provider plugin is installed |
| the running runtime is the build this CLI is | a route is configured |
| migrations are applied | a binding names an account |
| the plugin registry is alive (zero plugins is alive) | a provider CLI **you name** answers |
| role skills are present, named after their directories and within the runtime's live cap | the skill packs are deployed to an agent harness |
| `jason` resolves on PATH — in this shell, or in every shell started from now on — and which file answers | autostart is registered |
| | notifications reach you |

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

### When something is wrong

```sh
jason logs --human
jason logs --level warning --since 2026-09-18T09:00:00Z
jason logs --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
jason doctor --dry-run
jason doctor
```

`jason logs` reads the runtime's log files straight from the data directory and never asks the runtime, so it
answers the same when the runtime is stopped, will not start, or has never run. Every filter you give must
hold — a level and everything above it, a campaign, a work item, a moment written with `Z` or an offset —
and the newest events that match come back, oldest first, exactly as the runtime wrote them; `--limit` says
how many.

`jason doctor` collects this installation into one file you can read before you share it. **Run it with
`--dry-run` first**: that composes the whole bundle, writes nothing, and lists what a plain run would
write — the sections and their sizes, the settings whose values it carries, what it never carries and what
it leaves out. The file holds the versions, the report `jason status` gives, the settings file and the
`JASON_*` variables by shape with a value only for the settings `jason doctor --help` lists, a bounded slice
of the log, what the runtime says about itself and each plugin by its id, version, digest, status and
problem codes, which plugin performs which operation, here and per campaign, by plugin id and operation
name — never which account a route binds — and the journal and the work items counted, with the newest entries by kind and ids and the
newest failures by their error's code — never by what they say. It never holds the capability token,
request or response bodies, prompts, work-item results, the database, plugin packages, or a route's
account and its identity. Only
`--include-journal` adds what the journal entries say: campaign names and fields as they changed, campaign
and work-item contexts as they changed, result formats, the addresses and numbers put on or taken off the
do-not-contact register, the accounts a campaign's routes bind, the options decisions were answered with,
the errors attempts ended with, provider identifiers, the effect, tool and operation a report names, the
failure text and receipts notification channels reported, entries people and agents wrote themselves, the
names actors gave, and the reasons they typed; it prints a warning to read the file before you share it. The
runtime's own report (`system_info`) is carried in part: channel detail, presence detail outside the
runtime's own words, skill problem text and the release-notes URL are withheld, and the bundle says so.
Before anything is written, your home directory becomes `~`, a data directory outside it `<data>`, a
different directory the runtime keeps its data in a mark of its own, and this account's user, machine and
domain names and its Windows account identifiers marks of their own.

The file is written under the data directory, in `logs/doctor`, and the verb prints its path and the SHA-256
of its bytes. **It is yours to share, and nothing sends it anywhere**: it never reaches the network, and the
only requests it makes are to this installation's own runtime, on this machine. Bundles are not rolled — they
stay until you delete them or `jason uninstall --purge-data` removes what Jason keeps in the data directory.
Like `status`, both verbs exit 0 or 1 and never 3: `logs` never asks the runtime, and a runtime that does not
answer `doctor` is written into the bundle rather than given as a reason there is none.

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
installer wrote it and its directory holds nothing but Jason — the executable, its two notice files and what an
upgrade left beside it; then the executable, its notice files, its install directory and the native libraries it
unpacked. A recorded file whose bytes have changed since is
reported and kept unless `--force` says otherwise.

**The data directory is kept** — the database, the settings, the plugins, the logs and the work directories
are the record of what you did — and the verb says in one line that it kept it and where. `--purge-data`
removes what Jason keeps there, and the directory once nothing else is in it; in the machine shape the word
has to be said in advance, because there is nobody there to be asked. Like `status`, this verb has no API
operation behind it: removing Jason from a machine is not something the runtime performs or is asked about.
[docs/INSTALL.md](docs/INSTALL.md) §7 is the fuller account.

## Using it

`jason runtime start` launches the runtime in the background and prints the instance it ended up
talking to; `jason runtime stop` asks that instance to shut down and returns only once it has
really gone. Nothing is registered until you ask — starting the service is something you, your
agent, or a registration you made yourself did. `jason runtime run` keeps the runtime in the
foreground instead, and `jason runtime status` asks it for `system.info` through the Runtime API.

To have the runtime start when you log on, register it once. It carries the data directory it was
registered under, and nothing supervises it afterwards — a runtime that stops stays stopped until
somebody starts it. It needs no administrator rights: on Windows it registers from an ordinary
prompt, on a standard account too, one task per account, and the runtime then runs in your session
and ends when you sign out. On Windows that is a logon task named for your account, on macOS a LaunchAgent, on
Linux a `systemd --user` unit; `jason runtime autostart status` says which is registered.

```sh
jason runtime autostart enable
jason runtime autostart status
jason runtime autostart disable
```

Campaigns, the people in them, and the work to be done about them are managed with one verb per
API operation:

```sh
jason campaign create --name "Latin America" --idempotency-key latam-campaign
jason campaign add-contacts <id> --file contacts.json --match-by email
jason campaign start <id>
jason role list
jason workitem create <id> --kind ai_role --role researcher --idempotency-key latam-research-1
jason workitem list --campaign <id> --status failed --status expired
```

`--idempotency-key` is optional: a create sent again with the same key answers with what the first one made
instead of making it twice, and a different request under a key already used is refused.

Every verb prints the exact API response as compact JSON on stdout (add `--human` for a readable
rendering). The runtime keeps its data under `~/.jason` (override with the `JASON_DATA_DIR`
environment variable).

A work item moves through one set of statuses: a dispatcher claims it under a lease, with at most one live
attempt at a time, and whatever performs it reports what happened through the same API; `jason workitem get
<id> --human` shows its attempts and how each ended.

Which agent host runs a piece of AI work is named by an **execution profile**, and each attempt keeps the
revision of the profile it ran under; when no profile can run a piece of AI work it waits, blocked, and
everything else keeps running:

```sh
jason profile create local-claude --host claude_code --program claude   --deny Write --host-version-verified 2.1.275
jason profile list --human
jason workitem create <campaign-id> --kind ai_role --role researcher --execution-profile local-claude
jason rolenote get <campaign-id> researcher
```

When a campaign is looked at, and by whom, is its own loop: a failure, a rejected approval, an
effect somebody reported or a reply in the campaign's conversations puts a **check-in** on the queue,
and a cadence does it anyway when nothing has happened at all. Replies are found by reading each live
campaign's conversations on a cadence. How often a manager looks at a campaign when nothing has happened is the
campaign's own setting:

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

Which plugin performs an operation for a campaign, and which account of that provider it works in, is a
**route**. A claim checks everything that could refuse provider work before a child process exists and refuses
with that reason's own code; what ran is recorded on the attempt:

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
Jason performed. They are read back like this:

```sh
jason report submit --actor human:you@example.com --effect email_sent --tool reply-cli \
  --summary "Sent the intro by hand after the enrolment failed." \
  --campaign <campaign-id> --contact <contact-id> --idempotency-key intro-1
jason report list --campaign <campaign-id> --human
```

## Building from source

The runtime's source is not published, so there is nothing in this repository to build. A person with access to it
builds by that repository's own guide. A build from source knows no published tag, so it takes its skills from a
checkout of this repository with `--source` ([docs/INSTALL.md](docs/INSTALL.md) §3 and §4).

## Ecosystem

| Repository | Role |
|---|---|
| `reply-team/jason-ai` (this repo) | Product entry point: the runtime and business skills, the official plugins, the operation contracts, the install path, and every release of the runtime — the executable for each platform; the runtime's source is not published |
| [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) | Reply.io provider CLI: auth, profiles, teams, full API v3 access, and the Reply-specific skills |
| [`reply-team/reply-mcp`](https://github.com/reply-team/reply-mcp) | Remote MCP server exposing curated Reply tools to MCP-capable agents |
| [`reply-team/reply-skills`](https://github.com/reply-team/reply-skills) | **Formerly** the home of the skills packs. Both halves now live here: the runtime skills in [`skills/runtime`](skills/runtime), re-authored around the Runtime API, and the business skills in [`skills/business`](skills/business), moved across unchanged and then held to this repository's own guards. The Reply-specific half belongs with `reply-cli` |

## Getting help

Jason is in alpha, and this repository takes no issues yet. If something does not work, write to
**support@reply.io**: what you ran, what it answered, and — if you choose to — the file `jason doctor` wrote. Read
that file before you send it; [When something is wrong](#when-something-is-wrong) says what it holds and what only
`--include-journal` adds. A vulnerability goes the way [SECURITY.md](SECURITY.md) says.

## License

Everything in this repository is MIT ([LICENSE](LICENSE)). The runtime binaries attached to each release are
licensed separately, under the Jason Runtime Licence ([LICENSE-RUNTIME.md](LICENSE-RUNTIME.md)): every archive
carries the licence and the third-party notices, and an install keeps both beside the executable as
`jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`.

The names "Jason", "Jason AI", "Reply" and "Reply.io", and their logos, are trademarks of ReplyApp Inc. Neither
licence grants a right to use them; [TRADEMARKS.md](TRADEMARKS.md) says what you may do with them without asking.
