# The Jason handbook

This is the technical page of a Jason installation: installing it, the installation itself, using it, the ecosystem
around it and getting help.

**Jason AI** — an AI SDR harness: a local-first, vendor-neutral SDR domain runtime, distributed as one
self-contained executable per platform, with open skills, contracts and plugins, that turns your own AI agents,
installed SDR skills, and external execution providers into a durable outbound-sales operation.

## Why we built Jason

The way people use software is changing: the agent's session is becoming the place where work gets done, between a
person and the services that open their capabilities through an API. You say what you need; the agent reads the
repository, files the ticket the way your company files them, writes the mail, and you open none of those interfaces. We
run outbound sales at Reply.io, and we watched the agents our own people use do the thinking well and the operation
badly: an agent can plan a campaign, write to a person and call a tool, but when the chat ends, so does the work.
Nothing holds what was sent, what waits for a decision, what failed and what comes next, and the next session starts
from a blank page. Jason is that layer, built for agents first: a harness in which your own agents keep the judgment,
skills carry the profession, a runtime on your machine keeps campaigns, work, approvals and history alive between
sessions, and plugins connect the operation to whichever provider owns the channels, Reply first.

We built it with the current, not against it. In a few years the agents themselves gained skills, connectors, sessions
and memory; Jason stands on those conventions, so most of the work happens inside your session, and each step their
makers take forward strengthens it. Nothing reaches a real person without your approval, and the skills, contracts and
plugins are open, so the harness grows the way agent tooling does: by people adding what they need.
The Runtime API is documented and open as well, so a plugin of your own — a program that reads and writes the runtime —
can be built over it: [docs/runtime-api.md](runtime-api.md).

The reasoning behind this, and who Jason is for and who it is not for, is in [docs/philosophy.md](philosophy.md).

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
> scripts, `jason update` and `jason ui install`, and `jason skills install` and `update`. The check, the install
> scripts, `jason update` and `jason ui install` go to `get.jasonharness.ai`, an address Reply.io operates, which
> redirects them to this repository's releases; the skills come from this repository's releases on GitHub directly. What that address keeps,
> and for how long, is in [docs/responsible-use.md](responsible-use.md#update-check-and-downloads). Nothing else,
> and no telemetry: the file `jason doctor` writes stays on your machine unless you send it.

## Installing

```sh
curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh
```

```powershell
irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex
```

Jason is released under the MIT licence: [LICENSE](../LICENSE). What it sends off your machine, and what stays your
responsibility when it reaches people, is in [docs/responsible-use.md](responsible-use.md).

A one-liner installs the executable and ends by teaching it its role skills, without which every role it launches
runs untaught. [docs/INSTALL.md](INSTALL.md) is the rest, in order — teaching it its skills again where that step
said it could not, asking whether it can work, and starting it — for a person or an agent.

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
<https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/INSTALL.md>: read the whole file
first, then follow it in order from section 2, installing from a release and building nothing. Before
you run anything, tell me in a few words what you will install and where, and wait for my yes; ask me
again before each act the guide marks "Ask first". At the end, tell me whether Jason can start work and
exactly what you ran and changed. Then do what the guide's hand-over says: offer me the wiring check
once, and bring me to my first campaign in my own words.
```

Once it runs, [examples/](../examples/README.md) is where cases of Jason applied to outbound work are collected, with a
template for sending your own.

## What this is

A generic LLM or coding agent can *discuss* an outbound goal, but it does not automatically
become a durable SDR operation: it has no authoritative campaign state, no reliable
schedules and retries, no approval enforcement, no recovery, and no operational history that
survives the chat session. Jason is that missing layer — three parts working as one product:

1. **Jason Runtime** — a deterministic, long-running, user-scoped process
   that owns durable campaign and work state, scheduling, dispatch, approvals, safety
   guardrails, recovery, and observability. Its **Runtime API** is the only authoritative
   boundary; the **Jason CLI** (built for both humans and AI agents) is its client.
2. **Skills** — the semantic layer: vendor-neutral SDR expertise, role reasoning, and
   guardrails that teach *your* AI agents how to plan and run outbound work through Jason.
   Runtime skills ship with the runtime under [`skills/runtime`](../skills/runtime): five for a
   person's own session, one per moment they ask for something, and one for each of the nine
   roles this runtime seeds, every one of them held to the same launch contract.
   [`skills/business`](../skills/business) holds the SDR profession's own knowledge — nine skills and a
   catalogue of 325 named business operations — refreshed on its own cadence rather than with the
   runtime. [`skills/README.md`](../skills/README.md) says why the two are kept apart and how a pack is
   installed.
3. **Provider plugins** — strict, vendor-neutral canonical operations executed by JavaScript
   plugin packages in a short-lived plugin-host process started from the same executable.
   [Reply.io](https://reply.io) is the intended default execution provider (through
   [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli)) — supported and
   commercial, but never hard-coded: no provider ships configured, the same public plugin mechanism
   works for any other, and writing one against it is documented in
   [docs/plugins.md](plugins.md).

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
SDK. **Canonical operation contracts** are published under [`docs/contracts/`](contracts) — one
machine-readable document per operation, embedded into the runtime that enforces it — and a work item naming a
provider operation is measured against its contract when it is created: an operation this build publishes no
contract for, or arguments that do not satisfy one, are refused there and then. **Provider operations run through
routed plugins**: a route says which plugin performs an operation for a campaign and which account of that provider
it works in, the claim decides everything that could refuse the work before a child process exists, the plugin
performs it in its own process, and what ran is pinned to the attempt — the package, its digest, both snapshots and
the account by identity. **No provider is built in.** One arrives as a plugin, and a fresh runtime routes nothing
anywhere until somebody writes a route: work with no route fails at the claim with `no_route`. An operation that
needs a person's approval is parked until a person decides, and nothing runs but what they approved. **The first
such plugin is in this repository**: [`plugins/reply/`](../plugins/reply/README.md) performs all twenty-four published
operations against a Reply account through the Reply CLI, which owns the credential — so an operator installs that
CLI, signs in with it, places the package from the release with `jason plugin install reply`, grants it that one program,
and writes the route. **The wiring check** — an empty installation brought to a routed plugin, one enrolment of
your own address approved by you and performed against the provider, and the history it leaves — is
[docs/golden-path.md](golden-path.md), and every command on that page is executed by a test. It proves the
wiring and is not the real work, which begins with a campaign planned with the business skills
([`campaign-planning`](../skills/business/campaign-planning/SKILL.md) first) and worked by Jason's roles. It is optional:
a synthetic walk, offered once when an installation is ready, which you may skip for the real work at once — and that
begins with you telling your agent about your first campaign in your own words. Releases arrive one self-contained
increment at a time; no dates, no roadmap promises.

## Architecture

One executable, three modes: the runtime service, which owns campaign and work state and is the only thing
that writes it; the CLI, a pure client of the runtime's loopback API; and the plugin host, a short-lived process in
which a plugin's JavaScript runs, so that it never runs inside the long-lived runtime. What a person, an agent or a
plugin author needs is here:
[docs/philosophy.md](philosophy.md) for what Jason is and who it is for, [docs/INSTALL.md](INSTALL.md),
[docs/plugins.md](plugins.md), [docs/ui.md](ui.md) for the browser UI, the contracts under
[docs/contracts/](contracts/README.md), and the skills under [`skills/`](../skills).

## Repository layout

| Path | Contents |
|---|---|
| `plugins/` | Plugin marketplace: one folder per plugin with its manifest and JavaScript |
| `skills/runtime/` | Skills that teach an agent to operate Jason: the interactive ones, and one per launched role |
| `skills/business/` | Skills that teach an agent the SDR profession, independently of any provider or runtime |
| `.claude-plugin/` | The skills marketplace: offers both packs to an agent harness by relative path. Distinct from `plugins/`, which is the plugin marketplace |
| `docs/` | What Jason is and who it is for, the install manual, the wiring check, the plugin author's guide, the browser UI, the operation contracts, and the Runtime API's recorded exchanges under `docs/api/` |
| `examples/` | Cases of Jason applied to outbound work, one folder each, and the template a case is written from |
| `install/` | The install scripts every release attaches, and the update feed's schema |
| `ui/` | The browser UI the runtime serves: a Preact client of the Runtime API; `npm run package` builds it into `jason-ui.zip`, which each release attaches and `jason ui install` places |
| `VERSION` | The runtime version this tree describes |

The runtime's source is here too, under `dotnet/`: the runtime, its CLI and its plugin host in C#, with their tests.
[DEVELOPMENT.md](../DEVELOPMENT.md) says how to build and run it from a checkout. Each release attaches the runtime for
every platform, built from this tree at the release's version.

## The installation

Each install script works out your platform, verifies the download's SHA-256 before unpacking it, installs the
executable under your own account — `~/.local/bin` on macOS and Linux, `%LOCALAPPDATA%\Programs\jason` on
Windows — with its licence and the third-party notices beside it, as `jason-LICENSE` and
`jason-THIRD-PARTY-NOTICES`, and puts that directory on your PATH unless you pass `--no-modify-path`. Nothing
needs administrator rights. Both one-liners install the **latest release**. If either answers 404, the release
could not be reached — through the address or on GitHub — and that is not a failure of your machine: report it
as it stands rather than run the line again, and build nothing instead.

**[docs/INSTALL.md](INSTALL.md)** is the whole of it in order — what you need, from a release, from
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
recorded at the new release's tag and placing the release's UI; an update applied by a build before 0.12.0 needs one
`jason skills update` after it, and one applied by a build older than the UI step needs one `jason ui install`. `status` says where one stands, and reads the ledger without needing a runtime.

### Can this installation start work?

```sh
jason skills install --roles-only
jason skills install --dry-run
jason skills install
jason skills update
jason ui install
jason status
jason status --human
```

`jason skills install --roles-only` puts the role skills where the runtime reads them at every launch — the
half a runtime cannot work without — and writes nowhere else; the install scripts run it as their last step. Both
verbs take the skills from the release's own package of them, `jason-skills.zip`, unless `--source` names a checkout
or a git repository. `jason skills install` does that and puts the
interactive and business packs where your agent harness looks as well, which is its configuration and yours
to allow. Both print the target roots before they write anything, and both refuse a deployment the runtime
would later refuse to launch, rather than writing one.
[`skills/README.md`](../skills/README.md) is the contract both verbs keep. `jason ui install` places this release's
browser UI, the page `jason ui open` opens; a runtime works without it.

One question, answered check by check. It is one of eleven names in this CLI with no API operation behind
it — `status`, `uninstall`, `logs`, `doctor`, `export` and `import`, the `skills` and `update` nouns, `ui install` and
`plugin install`, which change this installation's files as those two do, and `session open`, which starts your own
agent host in a terminal — and it cannot be otherwise: half of what it reports is not the runtime's to know — an executable on PATH,
another vendor's CLI and whether it answers, files in a folder in your home directory. No API operation can
answer "am I ready to work?", because the runtime is not the authority on the machine it runs on.

| Required — a failure exits 1 | Optional — absent never fails |
|---|---|
| the runtime answers, and no update stopped part-way from its swap on — its repair then is `jason update apply`, never `jason runtime start` | no update is waiting to be resumed |
| the running runtime is the build this CLI is | a provider plugin is installed |
| migrations are applied | each plugin package this release publishes is the one installed |
| the plugin registry is alive (zero plugins is alive) | every plugin installed is available |
| role skills are present for every role Jason ships — a role of your own is reported, never failed — named after their directories and within the runtime's live cap | a plugin is granted a journal kind |
| `jason` resolves on PATH — in this shell, or in every shell started from now on — and which file answers | a route is configured |
| | a binding names an account |
| | a provider CLI **you name** answers |
| | the skill packs are deployed to an agent harness, and the files their record names are there |
| | every enabled profile names an agent host detected on this machine |
| | autostart is registered |
| | notifications reach you |
| | a UI is installed, and the default one is this release's |
| | the harness root can take the first project or campaign |

**It exits 0 or 1 and never 3.** Exit 3 means "I could not ask the runtime", and this is the verb whose whole
job is to answer when the runtime cannot be asked — so a runtime that is down is a failed check rather than an
error envelope. The JSON body carries `ready` and a state per check, so an agent branches on the body rather
than on the code. The `role_skills` check alone also carries `details`, the deployment the record in the role root
names, as fields a program reads instead of the fact: `ref`, `source`, `commit` (empty where the record names none)
and `roles_taught`. `roles_taught`, how many roles the runtime reports it teaches, is left out where the runtime did
not answer or reported that it could not read its role root. Where the runtime did not answer, the record is the one
in this installation's own role root. With no record, or one that cannot be read, there are no `details`. `--human`
prints the facts alone. A check reports a fact and, where there is one, the command that repairs it; it never reads
a log and never explains a failure, which is what keeps it from growing into a diagnostics verb.

The provider check runs only a program **you** name — `jason status --provider-cli <program>`, run as
`<program> --version` with a ten-second bound. The program is found the way Jason finds every program it starts
by name, and on Windows an npm-installed CLI runs as what its shim starts, the interpreter and entry script it names
or the executable its package ships, then `--version`. Which provider you use is yours, and a readiness check that knew one vendor's command by heart would
be this product naming a vendor in its own sources, which it does not. In its own output it names the file the
name resolved to and, for npm's shim, what that runs, and, whenever it read a PATH, whose it was: the CLI's,
which the runtime's may differ from. It prints no credential — not the key, not a prefix of it, not its length.

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
name — never which account a route binds — which plugins each subscribed journal kind is handed to, where its
cursor stands and how its newest chains ended, by status and code — never the envelope a chain carried — and the
journal and the work items counted, with the newest entries by kind and ids and the
newest failures by their error's code — never by what they say. It never holds the capability token,
request or response bodies, prompts, work-item results, the database, plugin packages, a route's
account and its identity, the envelopes subscription chains carried, the names of execution profiles, or anything read from the folder Jason keeps what it learns about your business in —
`~/Jason` unless `Harness:Root` names another — whose folders and files it never lists or opens. Only
`--include-journal` adds what the journal entries say: campaign names and fields as they changed, project names as they changed, the folders a
project link recorded, campaign
and work-item contexts as they changed, result formats, the addresses and numbers put on or taken off the
do-not-contact register, the accounts a campaign's routes bind, the options decisions were answered with,
the errors attempts ended with, provider identifiers, the verdicts roles recorded on replies and the decisions
whose answers they carry, and the standing
approvals people set: their categories, reactions, goals and limits, the effect, tool and operation a report names, the
failure text and receipts notification channels reported, entries people and agents wrote themselves, the
names actors gave, and the reasons they typed; it prints a warning to read the file before you share it. The
runtime's own report (`system_info`) is carried in part: channel detail, presence detail outside the
runtime's own words, skill problem text and the release-notes URL are withheld, and the bundle says so.
Before anything is written, your home directory becomes `~`, a data directory other than `~/.jason` `<data>`
wherever it stands, a different directory the runtime keeps its data in a mark of its own, the harness root
`<harness_root>` wherever it stands, the folder the executable is in outside your home directory `<install_dir>`
(a package manager's prefix, such as `/opt/homebrew`, travels), and this account's user, machine and domain names and
its Windows account identifiers marks of their own; a plugin, a role or a skill pack Jason does not ship is named by an
alias, `<plugin-1>`, `<role-1>` or `<skill-pack-1>`, one per name, never by its own; and a skill source given with
`--source` other than this repository, with the ref it was taken at unless that is a release tag, is `<skill-source>`.

The file is written under the data directory, in `logs/doctor`, and the verb prints its path and the SHA-256
of its bytes. **It is yours to share, and nothing sends it anywhere**: it never reaches the network, and the
only requests it makes are to this installation's own runtime, on this machine. After a plain run it prints where a
problem goes — the issue form's address, filled in — and opening that issue is yours to do; a file made with
`--include-journal` goes to the support address under [Getting help](#getting-help), never to an issue. Bundles are not rolled — they
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
has to be said in advance, because there is nobody there to be asked. The folder Jason keeps what it learns about
your business in — `~/Jason` unless `Harness:Root` names another — is never touched, `--purge-data` included: it is
yours. Like `status`, this verb has no API
operation behind it: removing Jason from a machine is not something the runtime performs or is asked about.
[docs/INSTALL.md](INSTALL.md) §7 is the fuller account.

### Moving it to another machine

`jason export`, with the runtime stopped, writes the database, the settings, the plugins, the role skills, a custom
UI, the logs and the harness root's tree into one archive, with `export.json` listing every file's size and SHA-256;
`jason import <file>` on the other machine checks all of it before writing anything, moves an installation already
there aside under `--replace` and deletes nothing, rewrites the settings file's paths under the old home, data
directory and harness root to this machine's, registers at logon when the old machine did, then starts the runtime
and asks it to record each project folder where it now stands. The archive carries the settings file with every
secret in it, so keep it as you keep that file. Both verbs refuse while the runtime runs unless `--stop` is given,
and neither has an API operation behind it: the files are copied cold, which no runtime can do to itself while it
answers. [docs/INSTALL.md](INSTALL.md) §8 is the fuller account.

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
revision of the profile it ran under. The hosts a profile can name are those of the
[host matrix](execution-profiles.md#12-the-hosts); a profile naming another is refused when it is written.
When no profile can run a piece of AI work it waits, blocked, and everything else keeps running:

```sh
jason profile create local-claude --host claude_code --program claude   --deny Write --host-version-verified 2.1.275
jason profile list --human
jason workitem create <campaign-id> --kind ai_role --role researcher --execution-profile local-claude
jason rolenote get --role researcher --campaign <campaign-id>
```

Cursor CLI can run the background roles too. Install it from
[Cursor's installation page](https://cursor.com/docs/cli/installation.md), where its commands are kept current, sign in as
yourself, then name it:

```sh
agent login
jason profile create local-cursor --host cursor_cli --program agent
```

The [host matrix](execution-profiles.md#12-the-hosts) says what was built from documentation and what has not yet
been run on an account.

Codex CLI is named the same way: install it from [its own page](https://github.com/openai/codex), sign in with
`codex login`, then name it. On Windows, Codex needs a one-time setup by an administrator first, which
[the Codex section](execution-profiles.md#codex-cli) describes.

```sh
codex login
jason profile create local-codex --host codex --program codex
```

GitHub Copilot CLI too: install it from
[GitHub's installation page](https://docs.github.com/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli),
sign in with `copilot login`, then name it:

```sh
copilot login
jason profile create copilot --host copilot_cli --program copilot
```

Gemini CLI is named the same way, on a Code Assist Standard or Enterprise sign-in: install it from
[its own page](https://github.com/google-gemini/gemini-cli), sign in as yourself, then
`jason profile create local-gemini --host gemini_cli --program gemini`.

Antigravity CLI (`agy`) is the host individual Google plans run. Install it from the CLI tab of
Google's quickstart ([antigravity.google/docs/getting-started?tab=cli](https://antigravity.google/docs/getting-started?tab=cli)),
run `agy` once and sign in as yourself ([how Google describes signing in](https://antigravity.google/docs/install/)),
then name it:

```sh
jason profile create local-antigravity --host antigravity_cli --program agy
```

Each run is given its permissions by a workspace hook the launch writes, which approves the call back to the runtime,
the memory files and the result file; that route is built from the host's hooks page and has not yet been run on an
account. If a role is refused on `antigravity_cli`, add two rules to your own settings file,
`~/.gemini/antigravity-cli/settings.json`, in its `permissions.allow` array: `command(jason)` and
`read_file(<your harness folder>)`; on Windows shells write the first as `command(regex:jason .*)`. The
[host matrix](execution-profiles.md#12-the-hosts) gives the two lines of JSON, says why, and says what was built from
documentation and has not yet been run on an account.

When a campaign is looked at, and by whom, is its own loop: a failure, a rejected approval, an
effect somebody reported or a reply in the campaign's conversations puts a **check-in** on the queue,
and a cadence does it anyway when nothing has happened at all. Replies are found by reading each live
campaign's conversations on a cadence. How often a manager looks at a campaign when nothing has happened is the
campaign's own setting, and so is how many untouched people it wants to keep in reserve: when fewer than that are left,
the manager is told once, and told again only after the number has gone back up
([campaign manager](campaign-manager.md) §9):

```sh
jason campaign update <campaign-id> --review-seconds 3600
jason campaign update <campaign-id> --audience-threshold 50
jason workitem list --campaign <campaign-id> --role manager
```

Jason ships no AI client and holds no model credentials: a profile names a host the user already
installed and authenticated, and has nowhere to put a secret.

What a launched role is taught is a **role skill**: one directory per role, which an operator composes
into `<data>/skills/roles/<role>/` from the packs under [`skills/`](../skills) and which the runtime copies
into each attempt's own work directory. The packs contribute different files to it — this repository's
runtime pack the `SKILL.md` holding the launch contract, a business pack the profession's method beside
it — so that installing one never overwrites the other. What a role *remembers* is a **role note** — one document per
role at each of three levels, the client's, a project's and a campaign's, written by that role for its own later
runs, and deliberately not authoritative: where a note disagrees with campaign state, the state is what is true.

What a plugin package is, every rule its manifest is held to, what the Host SDK offers and under
which limits, how capabilities are granted, and the invocation protocol between the runtime and the
plugin host, are documented in **[docs/plugins.md](plugins.md)**:

```sh
cp -r ./my-plugin ~/.jason/plugins/
jason plugin reload --reason "installed my plugin"
jason plugin list --human
```

The canonical operations a plugin implements — one document per operation, the schema dialect they are
written in, the published argument and answer fixtures, and where Jason's strict subset departs from
the vendor-neutral contract it is seeded from — are in **[docs/contracts/](contracts/README.md)**.

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
jason approval get <approval-id> --prompt
jason approval approve <approval-id> --actor human:you@example.com --reason "checked the list"
```

`--prompt` prints, in place of the item, the text that briefs an agent session on it: what waits, the campaign and its
folder, the skill to use and what to bring back, and that the session decides nothing. It holds nothing of the item
itself, and it is the text the UI's Copy prompt button copies (`docs/ui.md`). To start your own agent host on the item
with that text as its first message, in a new terminal at the campaign's folder:

```sh
jason session open --approval <approval-id>
jason session open --question <decision-id> --dry-run
```

The named preference `agent.interactive_host` says which host (`claude_code` while it is unset); `--dry-run` starts
nothing and prints the whole command. The session proposes, and the decision stays yours. The UI's Open an AI session
link does the same on Windows (`docs/ui.md`).

A launched role that cannot decide something for itself asks a person instead, and the answer wakes the
review that carries the work on:

```sh
jason decision list --human
jason decision get <decision-id> --human
jason decision get <decision-id> --prompt
jason decision answer <decision-id> --answer "pause it" --actor human:you@example.com
```

When the provider refuses an account, or one sending account under it, for something the work cannot change, such as
billing, a plan's allowance or a disconnected channel, the runtime keeps that as a **block** and holds the work it
concerns until a call gets past it. A person who has removed the cause says so under their name, and the next scan lets
the work go; a clear that comes from a role or from a run is refused, and so is one that names no person:

```sh
jason block list --human
jason block get <block-id> --human
jason workitem list --block <block-id>
jason block clear <block-id> --actor human:you@example.com --reason "the invoice is paid"
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

## Ecosystem

| Repository | Role |
|---|---|
| `reply-team/jason-ai` (this repo) | Product entry point: the runtime and business skills, the official plugins, the operation contracts, the install path, and every release of the runtime — the executable for each platform |
| [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) | Reply.io provider CLI: auth, profiles, teams, full API v3 access, and the Reply-specific skills, which ship inside its npm package and which `reply skills install` writes |
| [`reply-team/reply-mcp`](https://github.com/reply-team/reply-mcp) | Remote MCP server exposing curated Reply tools to MCP-capable agents |
| [`reply-team/reply-skills`](https://github.com/reply-team/reply-skills) | **Formerly** the home of the skills packs; it is archived and read-only now. Its business pack (published there as `ai-sdr-core`) lives here in [`skills/business`](../skills/business), held to this repository's own guards. The runtime skills in [`skills/runtime`](../skills/runtime), re-authored around the Runtime API, replace its `agentic-runtime` pack, which is retired. `jason skills install` installs both. Its Reply-specific pack (`reply-adapter`) ships with `reply-cli` |

## Getting help

Jason is in alpha. Where to write ([CONTRIBUTING.md](../CONTRIBUTING.md) says the same):

- **A problem** — something that does not work: an
  [issue](https://github.com/reply-team/jason-ai/issues/new?template=problem.yml) with the file `jason doctor` wrote
  attached. That file is made to be shared, and `jason doctor` prints the form's address with the version and the
  platform filled in. Read the file first; [When something is wrong](#when-something-is-wrong) says what it holds.
- **Help with your own data**, or a file made with `jason doctor --include-journal`: **support@reply.io**, never an
  issue — such a file carries what the journal entries say, and an issue is public.
- **A question or an idea**: a [discussion](https://github.com/reply-team/jason-ai/discussions).
- **A vulnerability**: the way [SECURITY.md](../SECURITY.md) says, never anywhere public.

## License

Everything in this repository is MIT ([LICENSE](../LICENSE)), the runtime included, and so is the executable each
release builds from it: every archive carries the licence and the third-party notices, and an install keeps both
beside the executable as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`. ReplyApp Inc. releases this repository,
its history included, under the MIT licence from 0.24.0, and the licence files in older trees are superseded.

The names "Jason", "Jason AI", "Reply" and "Reply.io", and their logos, are trademarks of ReplyApp Inc. The licence
grants no right to use them; [TRADEMARKS.md](../TRADEMARKS.md) says what you may do with them without asking.
