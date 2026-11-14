# jason-ai — product vision and target architecture

> **Status: target architecture.** This document describes the intended design of the
> Jason AI runtime and the harness around it. It is **not** an implementation-status report and
> **not** a release promise. Nothing described here should be assumed to exist until the repository
> contains it. The project is delivered incrementally; this document deliberately contains no
> roadmap, milestones, or delivery order.
>
> It lives in this repository beside the runtime's source, and changes with it.

## 1. About this document

This is the comprehensive product and high-level architecture vision for the Jason AI runtime
(working name; see [naming](#naming)). It defines:

- the product boundary and the problem it solves;
- the high-level components and their responsibilities;
- accepted decisions and invariants that future design must preserve;
- rejected and superseded directions;
- a register of design that is deliberately unresolved.

It is **not** a database schema, a state-machine specification, an API contract, a security
specification, or a backlog. Where design is marked *deferred*, it is intentionally
unresolved — not an invitation to guess.

### Decision language

- **Accepted** — part of the current vision; not to be reopened without new evidence.
- **Invariant** — a boundary that future designs and implementations must preserve.
- **Working term** — a useful concept whose final public name is not yet selected.
- **Deferred** — deliberately left for later detailed design.
- **Rejected** — considered and deliberately excluded under the accepted architecture.
- **Superseded** — an earlier published direction that has been explicitly abandoned.

Examples, command names, object names, and text diagrams are illustrative unless a section
explicitly declares them decisions.

### Terminology note

This document says **plugin** for the JavaScript execution packages and **Plugin Host** for the
process mode that runs them. Earlier drafts said "adapter"; the decision and invariant
identifiers that contain `ADP` keep their names because identifiers are stable.

### Naming

“Jason AI” is used descriptively in this document (“the Jason AI runtime”). The
repository identity is `jason-ai`. Final public product naming and branding are a separate
decision and may change; nothing in the architecture depends on the name.

## 2. Executive architecture statement

Jason AI is a **local-first, vendor-neutral SDR domain runtime**, open source under the MIT licence
and distributed as self-contained binaries with its skills, contracts and plugins, that turns a
user's existing AI agents, installed SDR skills, and external execution providers into a durable
operational system.

It is not an LLM, an AI client, a prompt library, a thin scheduler, or a self-hosted clone
of Reply.io. It is the missing system around user-owned AI intelligence:

1. a deterministic runtime that owns durable SDR state, orchestration, safety, and recovery;
2. a skills layer that gives AI agents domain expertise, role behavior, operational
   knowledge, and guardrails;
3. an agent-first control surface through which user-owned AI sessions translate intent into
   stable runtime operations;
4. a canonical provider-operation contract and plugin architecture that execute
   deterministic side effects without hard-coding any single vendor;
5. human-facing CLI and UI surfaces over the same authoritative local Runtime API.

Reply is the default, commercially important execution provider, but not a mandatory
dependency of the runtime. The runtime, its state, its contracts, and its
orchestration remain vendor-neutral. Reply's hosted capabilities (sending, data, inbox, and
other services) remain commercial Reply services reached through the official Reply plugin
and Reply CLI; the core contains no artificial license gate.

The system combines deterministic continuity with semantic autonomy:

```text
deterministic Jason Runtime
  owns facts, rules, schedules, dispatch, lifecycle, safety and recovery

user-owned AI agents + Jason/SDR skills
  interpret goals, investigate, reason, plan, personalize and manage

Runtime API + Jason CLI
  form the only supported boundary for authoritative Jason state

provider plugins
  translate canonical deterministic operations into Reply or other tools
```

## 3. Product context and problem

### 3.1 Context

Reply.io has evolved over roughly twelve years from email outreach automation into a broad
sales-engagement platform covering multichannel sequences, conditional workflows, contact
management, LinkedIn automation, calls, SMS, tasks, data sourcing, inbox workflows,
reporting, and deliverability. Jason AI SDR is the higher-autonomy product direction: an
AI-assisted or autonomous system that can take a business objective and carry substantial
parts of the outbound lifecycle — ICP development, sourcing, qualification, research,
personalization, multichannel outreach, reply handling, and progression toward meetings.

Reply API v3 is the strategic execution surface for the official Reply integration. It does
not expose every product capability, but it covers the large majority of practical daily SDR
operations.

### 3.2 Existing public ecosystem

The public ecosystem consists of these components, which users should meet as one product:

- `reply-team/jason-ai` — the product entry point and everything Jason is made of: the runtime's
  source, the plugin marketplace, the runtime and business skills (`skills/runtime`,
  `skills/business`), the contracts, the documentation, the install path and the runtime's
  releases;
- [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) — Reply authentication,
  profiles, team context, skill installation for its own Reply-specific skills, and broad
  API v3 access;
- [`reply-team/reply-mcp`](https://github.com/reply-team/reply-mcp) — a curated Reply tool
  surface for MCP-capable agent clients.

Nobody should have to mentally assemble independent CLI,
MCP, skills, runtime, and UI projects.

### 3.3 The missing product layer

A generic LLM or coding agent can discuss an SDR goal, but it does not automatically become
a durable SDR operation. It lacks, unless supplied externally:

- authoritative long-lived campaign state;
- reliable queues, schedules, retries, approvals, and recovery;
- domain invariants and compliance controls;
- a stable contract for creating and managing SDR work;
- campaign-scoped continuity across many short-lived agent sessions;
- deterministic execution of already-decided provider operations;
- visible operational history after the initiating chat ends;
- a coherent installation and product shell.

Skills improve reasoning but cannot safely serve as the sole correctness boundary for
unattended work. A file-scanning daemon can wake agents but does not by itself provide
transactional state, legal transitions, single-flight execution, reliable recovery, or
accountable external effects.

The missing layer is therefore a coherent local product consisting of deterministic runtime,
skills, execution plugins, control surfaces, and durable multi-session behavior.

### 3.4 Market signal, not implementation template

External open-source systems — agent-enabled CRM products, lightweight local agent
runtimes — demonstrate two relevant trends: durable domain agents are becoming first-class
workers rather than chat decorations, and successful open-source agent products present one
visible entry point. These examples validate the direction but are not architecture
templates. Jason AI is neither a complete CRM containing one agent nor a generic
agent shell without SDR expertise. Its differentiator is the combination of an SDR domain
kernel, rich skills, user-owned agents, and replaceable provider execution.

## 4. Product thesis and guiding principles

### 4.1 Three product layers

#### Layer 1 — execution toolkit and plugins

Provider CLIs and APIs perform real external operations. Reply CLI/API is the default and
commercially important implementation. Other providers can be integrated through documented
plugins without changing the vendor-neutral Runtime core.

#### Layer 2 — SDR business expertise in skills

Skills teach agents how SDR work should be understood and performed: planning, ICP, audience
construction, research, personalization, channel decisions, safety, approvals, handoffs,
escalation, provider-tool usage, and expected outputs.

#### Layer 3 — durable local Runtime

Runtime keeps authoritative campaign and orchestration state alive across sessions and
machine restarts. It owns deterministic mechanics, exposes stable operations, launches
eligible work, validates outcomes, and makes the system observable and recoverable.

None of the layers is sufficient alone:

- tools without skills have hands but little domain judgment;
- skills without Runtime reason well but cannot guarantee durable correctness;
- Runtime without user-owned AI and skills has continuity but lacks semantic intelligence;
- hard-wiring Runtime to one vendor would sacrifice legitimate vendor neutrality.

### 4.2 Guiding principles

1. **Local-first ownership.** The user's orchestration state lives locally under the user's
   control.
2. **Single authoritative state boundary.** Runtime API, not files or direct SQLite access,
   governs Jason state.
3. **Determinism where possible.** Scheduling, validation, safety, retries, and known
   provider calls do not require AI judgment.
4. **AI where meaning is required.** Research, strategy, personalization, ambiguity
   resolution, and replanning belong to AI roles.
5. **Agent-first interaction.** A user's existing AI agent is expected to be the most
   natural semantic interface.
6. **Provider neutrality without commercial ambiguity.** Reply is the default supported
   provider, while alternatives remain genuinely possible.
7. **One public extension mechanism.** The official Reply plugin and community plugins use
   the same architecture.
8. **No hidden infrastructure burden.** Docker, service meshes, message brokers, and
   vendor-specific daemons are not assumed.
9. **Fail closed for side effects.** Missing routes, incompatible plugins, or invalid
   outcomes do not silently select another provider.
10. **Explicit provenance.** Managed execution, externally performed effects, agent-host
    choices, and plugin routes remain distinguishable.
11. **No roadmap masquerading as architecture.** The vision describes the whole system;
    implementation order is separate work.

## 5. System boundary and deployment topology

### 5.1 Local user boundary

Jason installs one Runtime and one Jason CLI for each operating-system user. One Runtime
manages multiple isolated business campaigns. It is not instantiated per campaign, folder,
repository, provider account, provider profile, or workspace.

```text
one operating-system user
  └── one Jason Runtime installation
        ├── business campaign A
        ├── business campaign B
        ├── business campaign C
        └── shared runtime services, registries and control API
```

Per-campaign isolation exists inside the Runtime domain: campaign-scoped state, a role's notes
at the campaign's level, configuration overrides, work, execution lineage, plugin routes, and
provider bindings must not leak between campaigns. Explicitly configured user-level defaults,
shared Runtime services, and a role's notes at the client's and a project's level, which cover
every campaign of that scope, are shared by design.

Multi-user shared-server operation, remote Runtime topology, and coordinated multi-machine
execution are not part of the currently accepted architecture.

One installed CLI does not mean one CLI process. Many short-lived Jason CLI invocations,
Plugin Host children, and agent sessions may coexist while one Runtime remains the sole
state owner for the OS user. Campaign isolation is logical/domain isolation, not OS-level
tenant isolation.

All installation paths must converge idempotently on the same user-scoped Runtime and data
domain rather than creating another daemon per installer, repository, or provider account.

### 5.2 System context

```text
┌──────────────────────────── User's operating-system account ────────────────────────────┐
│                                                                                          │
│  Human user                                                                              │
│     │                                                                                    │
│     ├── user-owned interactive AI agents ── skills ── Jason CLI ─┐                       │
│     │          │                                                 │                       │
│     │          └── direct CLI / MCP / API tools                  │                       │
│     │                                                            ▼                       │
│     ├── Jason CLI ───────────────────────────────────────── Runtime API                  │
│     │                                                            │                       │
│     └── Jason UI ────────────────────────────────────────────────┤                       │
│                                                                  ▼                       │
│                                                         Jason Runtime                    │
│                                                  ┌───────────────┼──────────────┐        │
│                                                  │               │              │        │
│                                              SQLite         dispatcher     registries    │
│                                                                  │              │        │
│                                      ┌───────────────────────────┴──────┐       │        │
│                                      │                                  │       │        │
│                              background AI work              canonical operation│        │
│                                      │                                  │       │        │
│                              Agent Host integration                 routing     │        │
│                                      │                                  │       │        │
│                              user-installed AI host       short-lived Plugin Host       │
│                                                                         │                │
│                                                                  JavaScript plugin      │
│                                                                         │                │
│                                                                  vendor CLI / API        │
└─────────────────────────────────────────────────────────────────────────┼────────────────┘
                                                                          ▼
                                                           Reply.io / CRM / provider systems
```

The diagram is conceptual: it defines authority and direction, not exact processes,
transports, ports, or tables.

For a direct out-of-band side effect, the accountability path returns toward Jason rather
than through the provider route:

```text
direct CLI / MCP / API side effect
  ···> post-factum external-effect report ···> Jason CLI / Runtime API
```

### 5.3 Local-first does not mean provider-self-hosted

The Runtime, orchestration state, skills, contracts, plugins, and control surfaces are open
and local. Provider capabilities remain where the provider owns them: Reply sending, data,
inbox, and other hosted capabilities still require Reply accounts and services when the
Reply plugin is selected. The same applies to other CRMs, data sources, messaging
platforms, and AI hosts. Jason coordinates them; it does not reproduce them locally.

## 6. Component and responsibility model

### 6.1 Responsibility summary

| Component or actor | Owns | Must not own or bypass |
|---|---|---|
| Human user | Goals, constraints, approvals, corrections, final escalation decisions | Hidden state mutations outside supported contracts when relying on managed behavior |
| Interactive AI agent | Natural-language interpretation, skill selection, research, proposals, CLI/tool use | Jason SQLite or internal Runtime objects |
| Skills | SDR knowledge, role reasoning, tool guidance, guardrails, output and handoff guidance | Queues, schedules, authoritative statuses, retries, Runtime state machines |
| Jason Runtime | Authoritative local state, domain invariants, dispatch, lifecycle, recovery, safety, registries | Vendor-specific provider business calls in core code; open-ended semantic judgment |
| Runtime API | Supported reads and mutations over Jason state | Exposure of tables, ORM models, or storage implementation |
| Jason CLI | Administrative and business projections over Runtime API | Duplicate business logic or private storage access |
| Jason UI | Human observability and control over Runtime API | A second source of truth or separate domain implementation |
| Dispatcher | Deterministic readiness, routing, lifecycle, triggers and mechanical policy | AI role behavior or subjective SDR decisions |
| Background AI role | Scoped semantic work under skills and supplied context | Direct authoritative state mutation; assumption that memory is truth |
| Agent Host integration | Launch and lifecycle translation for a supported user-installed AI host | Model credentials or a Jason-owned AI provider account |
| Provider plugin | Canonical-to-vendor mapping, provider invocation, output/error normalization | Jason domain authority, durable campaign state, implicit provider fallback |
| Plugin Host | Isolated JavaScript execution and restricted Host SDK | Business reasoning, vendor-specific mapping, long-lived state |
| Vendor CLI/API | Provider authentication and remote operations | Jason orchestration truth |

### 6.2 Human user

The user supplies business intent, constraints, corrections, approvals, and decisions that
cannot or should not be resolved autonomously — through an AI conversation, Jason CLI, or
Jason UI. The product does not require the user to understand internal database, process,
plugin, or agent-host mechanics. It does require honest visibility when work is blocked,
uncertain, awaiting approval, missing a provider route, or unable to launch an AI host.

### 6.3 User-owned interactive AI agent

The interactive AI agent is expected to be the primary semantic interface. A user may start
in any capable environment and state a goal such as entering a new market. The agent uses
installed skills to clarify intent, research context, propose decisions, and materialize
accepted work through Jason CLI.

Jason does not install, license, authenticate, or require a particular interactive agent.
Any compatible agent can participate if it can read the relevant skills and invoke the Jason
CLI or Runtime API contract available to it. When the person asks, `jason session open` starts the agent host they
named in the preference `agent.interactive_host` (Claude Code where none is named) in a terminal of their own, on one
item that waits, or at a campaign's folder, a project's, the harness root or the folder it runs in with no first input,
where Claude Code or Codex may continue one of its own conversations by its id. The interactive agent
may also use external
CLI, MCP, API, web, or research tools directly; the rules for side effects performed outside
Runtime are defined in section 11.

### 6.4 Skills

Skills are the semantic knowledge layer. They explain:

- how to understand SDR goals and constraints;
- how planners, managers, strategists, researchers, reviewers, writers, and other roles
  should reason;
- when to request another role or human decision;
- how to use Jason's business operations through Jason CLI;
- how vendor-neutral SDR concepts map to Reply CLI, Reply MCP, Reply API, or other tools;
- which approval, compliance, deliverability, and channel guardrails apply;
- how to structure results, artifacts, handoffs, and escalation context.

Skills do not implement orchestration correctness. They may strongly guide agents to follow
the managed path, but Runtime independently validates authoritative changes.

Skills come in three projections with three homes, split by what must move in lockstep with
what: Reply-specific skills (mapping, authentication, CLI usage, errors) ship with the Reply
CLI; runtime skills (how an agent operates Jason: CLI usage, role entry points, approvals,
troubleshooting) live in this repository under `skills/runtime` and ship with the runtime;
business skills (vendor-neutral SDR operations, workflows, approvals, guardrails) live under
`skills/business` and are refreshed on their own cadence. Previously published runtime guidance
describing a file-based state model is superseded by this architecture. The runtime skills in
`skills/runtime` are the revision: they teach the managed path, what waits on a person, operating an
installation, why work stopped, and reporting effects produced outside Jason, in the runtime's own
concepts. Retiring the earlier published pack where it still lives is separate work.

### 6.5 Jason Runtime

Jason Runtime is a long-running user-scoped background process — a substantial deterministic
SDR domain application, not a generic queue and not a daemon that merely scans files and
asks an LLM what to do.

It owns:

- authoritative local campaign and orchestration state;
- deterministic domain invariants and legal transitions;
- schedules, dependencies, readiness, priorities, pauses, and cancellation;
- dispatch into deterministic provider execution or AI execution;
- attempts, outcomes, retries, timeouts, and recovery policy;
- approvals and mechanically enforceable safety boundaries;
- event-driven triggers and scheduled reconciliation triggers;
- the local Runtime API;
- plugin registry, routing, binding context, and compatibility validation;
- agent-host execution profiles and resolvability information;
- durable observability and operational history;
- persistence and migrations over SQLite.

Runtime does not encode open-ended SDR strategy or role reasoning. When the correct next
action requires meaning rather than testable policy, Runtime schedules AI work.

### 6.6 Runtime API

Runtime API is the only supported programmatic boundary for Jason state and behavior. It
hides SQLite, the ORM, internal domain objects, migrations, transaction boundaries, and
dispatcher internals.

Every authoritative read or mutation is expressed as an explicit Runtime operation and
validated by Runtime. Local execution does not imply that direct database access is
acceptable: generic SQLite tools, MCP integrations, scripts, agents, plugins, UI code, and
CLI code must not bypass this boundary.

The exact transport, authentication, endpoint shapes, streaming model, and versioning policy
are deferred.

### 6.7 Jason CLI

Jason CLI is a short-lived complete client of Runtime API with two coherent projections:

1. **administrative control** — install, start, stop, restart, status, health, diagnostics,
   logs, updates, plugin reload, and related lifecycle operations;
2. **business control** — create, inspect, schedule, prioritize, pause, resume, cancel,
   approve, report, reconcile, and otherwise manage supported domain entities and work.

The CLI is designed for people **and** AI agents. Its eventual contract must be
deterministic, non-interactive by default, structured, scriptable, and explicit about errors
and approvals. Stable JSON output, exit codes, idempotency, stdin/file payload options, and
preview behavior are detailed-design requirements, not finalized syntax here.

Reply CLI may expose convenience installation or update commands, but these must delegate to
the Jason mechanism rather than create a competing Runtime implementation.

### 6.8 Jason UI

Jason UI is the human-oriented visual client of Runtime API: campaigns, work, schedules,
runs, history, approvals, anomalies, failures, manager activity, provider status, and update
availability, with supported control actions through the same API. The UI is part of the
complete product vision; this document does not assign it to a release. It must never become
a second source of business logic or a privileged database client.

As built: the runtime serves the UI itself under `/ui/` from a module directory beside its data (`custom` when it
holds a build, else `default`, the release's own build, which `jason ui install` places), as static files under a `default-src 'self'` policy. A
browser pairs once: `ui.open` mints a single-use one-minute ticket, the page trades it at `POST /ui/session` for a UI
token the runtime keeps as a hash and accepts as a bearer until `ui.revoke`; a request carrying a foreign `Origin` is
refused. The UI is one more consumer of the API and the runtime keeps nothing for it; see the `DEC-UI` decisions.

**Jason Desktop** (`desktop/`, described in [desktop.md](desktop.md)) is the same kind of client in an application window
for Windows, macOS and Linux. It carries the release's `jason` executable, works on the one installation the CLI uses
(the same data directory, harness root and settings file; never a second one) and drives it only through the CLI's
verbs and the settings file a person would edit. It loads the UI the runtime serves at its loopback address, in a second
shell with the chat as its home, and reaches the page through one object, `window.jasonDesktop`. What it adds is what a
browser tab cannot be trusted with: a chat that starts the person's own agent host (Claude Code, or Codex to read) as a
child of the desktop and asks the person before the agent acts, a tray, and the setup of the connections that are a
person's alone. The runtime starts no process for a screen and knows no desktop: nothing in it, no route, state or file,
exists for the application, and a runtime with no desktop is unchanged.

### 6.9 Dispatcher

Dispatcher is deterministic Runtime code, not an AI role. It evaluates explicit state and
policy to determine what work is eligible, when it may start, which execution class it
requires, which configured route or execution profile applies, whether a result releases a
predefined continuation, whether work should wait, retry, block, cancel, escalate, or
request review, and when scheduled management or reconciliation work is due.

Dispatcher does not interpret market evidence, choose an ICP, write personalized copy, or
invent strategy. It schedules an appropriate AI role when such judgment is required.

### 6.10 Agent Host integration

Agent Host integration is the Runtime-side launch boundary for user-installed AI
environments that support unattended execution. It translates a provider-neutral AI work
request into the concrete invocation and lifecycle of a supported host: command syntax,
session creation or continuation, working directories, skill/context injection, model and
effort settings, output capture, process identity, cancellation, timeout, and resume
identifiers. Those details are deferred.

This component is not a Jason-owned AI client and does not own model-provider credentials.

### 6.11 Provider plugin and Plugin Host

Provider plugins implement deterministic canonical SDR operations against real vendor
tools. Plugin JavaScript owns vendor-specific transformations and calls a constrained Host
SDK. A short-lived Plugin Host supplied by Jason executes that JavaScript outside the
long-running Runtime process. Sections 12–14 describe this in detail.

## 7. Authority, persistence, and domain boundary

### 7.1 Authoritative operational state

Jason Runtime is the only authority for Jason-managed operational state. Structured state is
persisted transactionally over SQLite. Conceptually this includes:

- business campaigns and accepted goals;
- durable plans and their accepted revisions;
- managed work, dependencies, schedules, and priorities;
- attempts, results, errors, approvals, and escalations;
- deterministic events and management-review triggers;
- provider-operation invocations and external identifiers;
- plugin routes, plugin bindings, and pinned execution provenance;
- agent-execution profiles and causal execution lineage;
- facts required for compliance, eligibility, and recovery;
- durable observability needed to explain what happened.

The exact aggregate boundaries, tables, constraints, indexes, and transactions are deferred.
The authority boundary is not.

### 7.2 Explicit supersession of Markdown operational state

An earlier published design used Markdown/YAML files as the durable interoperability layer
for goals, plans, work items, statuses, schedules, approvals, and execution history. That
design is **explicitly superseded**. For Jason-managed work:

- skills and agents do not maintain an alternative file-based state machine;
- work-item files, plan files, or repository folders are not Runtime queues or lifecycle
  records;
- a Git repository or filesystem workspace does not define a separate Runtime instance;
- direct file edits cannot create, complete, retry, approve, or cancel Runtime work;
- there is no parallel skills-only Markdown runtime mode.

Published skills and documentation that still prescribe Markdown operational state do not constrain
this architecture. The guidance that ships with the runtime, under `skills/runtime`, no longer does;
what is still published elsewhere is superseded whether or not it has been withdrawn yet.

### 7.3 Possible file usage

Files may still be useful for large or human-readable artifacts, exported reports and
snapshots, role-produced documents, explicitly non-authoritative contextual memory,
import/export or diagnostic bundles, and executable plugin packages with candidate
configuration supplied for validation and activation. The person's memory files in the harness root are contextual
memory of this kind: the Runtime makes the folders that hold them and never reads a word of them (DEC-HARNESS-001,
INV-FILE-002).

A file must never silently become a competing source of truth for current statuses,
schedules, approvals, work ownership, or accepted execution outcomes. Plugin packages and
candidate routing/binding files are a configuration input boundary: Runtime validates and
activates them through its supported control path and owns the active immutable registry
snapshot. Editing a candidate file does not silently change an in-flight route or
authoritative campaign state.

### 7.4 External provider authority

Provider systems remain authoritative for their own remote entities and effects: provider
contacts, campaigns/sequences, messages, inbox conversations, sending results, CRM records,
enrichment records, channel activity and opt-out state, and provider account/organization/
team/environment structure.

Jason stores the local references, normalized observations, constraints, and orchestration
facts required to manage its campaigns. It need not mirror every provider-specific field
into SQLite.

### 7.5 Credential authority

Credentials do not belong in campaign payloads, plugin-routing files, work prompts,
artifacts, logs, or ordinary Runtime state. Authentication remains owned by the component
already responsible for the external system:

- Reply CLI owns Reply credentials, token refresh, profiles, and provider authentication;
- a user-installed AI host owns its model-provider authentication;
- another vendor CLI or secret store owns that provider's credentials;
- a plugin may receive a non-secret reference or selector, not necessarily the secret.

The precise operating-system secret-store and environment-reference policy is deferred.

### 7.6 Contextual memory is not truth

AI-role memory may contain summaries, hypotheses, preferences, prior reasoning, unresolved
questions, and references to important artifacts. It exists to help a fresh agent session
continue coherently. Memory is not authoritative for current work status, schedules,
priorities, approvals, compliance eligibility, provider effects, accepted results, or
current campaign facts. When memory conflicts with Runtime state, Runtime state wins.

### 7.7 Conceptual domain kernel

The following concepts are expected to exist in, or be referenced by, the eventual domain
model: business campaign/initiative; goal and accepted plan; lead/contact reference; planned
action; reaction or external event; ICP, audience, research, targeting, and personalization
results; work unit (a generic envelope for heterogeneous durable work); attempt, result,
event, artifact, approval, and escalation; management review; provider operation invocation;
external-effect report; role memory and execution lineage.

These are conceptual categories, not a final entity list. **Business campaign** and
**work unit** are working terms. The word **task** is too overloaded to serve as a precise
universal term.

### 7.8 State ownership map

```text
Jason Runtime / SQLite
  authoritative for Jason campaigns, work, lifecycle, approvals and orchestration;
  keeps, append-only, what providers said happened in their accounts (the events store),
  a record of their words and never an authority over their state

Provider systems
  authoritative for remote contacts, messages, conversations and provider effects

Provider CLI / secret store
  authoritative for provider credentials and authenticated account/profile selection

User-owned AI host
  authoritative for model-provider authentication and host session mechanics

Role memory and artifacts
  contextual evidence only; never authoritative operational state
```

## 8. Durable work and orchestration model

### 8.1 Heterogeneous work

Runtime needs one durable conceptual envelope for work that can be scheduled, observed,
related, and accounted for, even though different executor kinds require different payloads
and outcomes. Executor kind is distinct from work purpose.

| Executor class | Executor | Typical purpose | Authority after completion |
|---|---|---|---|
| Deterministic internal work | Runtime code | Mechanical state maintenance, triggers, reconciliation checks | Runtime validates and commits |
| Deterministic provider operation | Configured plugin | Send, enrol, fetch, update, or another canonical provider effect | Runtime validates normalized outcome and commits |
| AI-role work | User-installed agent host under a role | Research, planning, interpretation, personalization, management | Runtime accepts only supported results and mutations |
| Human decision or approval | User through agent, CLI, UI, or notification path | Approval, clarification, final escalation | Runtime records the exact accepted decision |

Manager review is AI-role work because it requires semantic judgment. Reconciliation is not
a fifth executor class. The common envelope does not imply identical schemas; exact payload
and state-machine design is deferred.

### 8.2 Conceptual AI work request

AI-role work may need: role identifier; business-campaign scope; intent or initial prompt;
context, authoritative projection, and artifact references; expected result definition;
execution profile and optional model/effort overrides; schedule, dependency, priority,
timeout, and escalation behavior; permissions or capability expectations; and
session-continuation references where meaningful. These fields describe the high-level need,
not a public role-manifest language.

### 8.3 Accountable outcomes

Every Runtime-managed execution must end in an accountable terminal outcome or a durable
non-terminal state: a canonical success or classified failure; a structured semantic result;
accepted domain changes submitted through Runtime operations; artifact references and
summaries; a retryable, blocked, cancelled, or awaiting-approval state; an escalation
request; newly proposed follow-up work; or an explicit statement that the result is
ambiguous and requires recovery or reconciliation.

Persuasive prose alone is not enough to complete managed AI work when a structured result
was required.

### 8.4 Execution-path decision

```text
Which executor does the accepted work require?
  ├── human judgment or approval
  │     → durable human-decision / escalation state
  ├── semantic judgment
  │     → AI-role work through a resolvable Agent Host integration
  ├── deterministic external provider access
  │     → canonical operation → plugin route → Plugin Host
  └── deterministic local mechanics
        → internal Runtime execution

If an agent performs an external effect directly outside Runtime
  → post-factum external-effect report → validation and reconciliation
```

AI is not used as glue between deterministic components. Deterministic code is not used to
imitate semantic judgment.

### 8.5 Readiness and dispatch

Dispatcher evaluates mechanically expressible conditions: dependencies, schedules, priority,
pause state, approval state, capability availability, plugin compatibility, and execution
limits. When eligible, work is routed to its supported executor class. The eventual design
must prevent duplicate execution and survive process or machine restarts; claims, leases,
heartbeats, single-flight behavior, fairness, and missed schedules are recognized
requirements whose detailed design is deferred.

### 8.6 Predetermined continuation

When accepted work already defines what follows and the completion conditions are satisfied,
dispatcher releases the next work without an AI review:

```text
research result accepted
  → predefined personalization work becomes eligible
```

Unnecessary AI arbitration would add cost and nondeterminism without improving the decision.

In the current runtime, sequencing between work items is expressed with `not_before` and with
waits — a role's run that ends with `workitem.wait` on the items it raised, a moment, or both, and is
run again, told why, once the wait is over — and left to the planning roles, which create the next
items as facts arrive; a predetermined-continuation
mechanism inside the runtime — one item releasing another on completion — is deferred and not
implemented. This section describes the accepted target, not a shipped capability.

### 8.7 Semantic continuation

When the next step depends on interpretation, Runtime schedules the appropriate AI role:

```text
research produces conflicting evidence
  → manager or strategist review becomes eligible
  → AI role chooses more research, replanning, escalation, or closure
```

The role proposes or invokes supported changes through Jason CLI/Runtime API; Runtime
validates and authoritatively commits them.

### 8.8 Reactive triggers and scheduled reconciliation

Two mechanisms combine:

1. **reactive triggers** — results, external events, errors, escalations, anomalies, or
   explicit handoffs can immediately release follow-up work;
2. **scheduled reconciliation** — Runtime periodically creates scoped review work to look
   for missing progress, stale assumptions, changing external conditions, ignored work, or
   strategic drift.

Classifying an inbound reply as interested may deterministically trigger immediate AI work
rather than waiting for the next periodic review; conversely, work that simply remains
unclaimed produces no transition event, so scheduled review remains necessary.

**In this build:** two scan steps follow a campaign's audiences and start nothing. The poll reads each search in
flight at the provider on a cadence, lands the people each read brings as members of the campaign, says once per hold
that the provider holds a search (`audience_search_held`) and once that it ended (`audience_search_ended`), and stops
the searches of a campaign that was archived. The reserve counts the members nobody has started on against the
threshold the campaign set and says once per crossing that it fell under it (`audience_reserve_low`). The held, ended
and low lines are default manager triggers: whether the campaign needs more people, and from which audience, is the
manager's (`docs/campaign-manager.md` §9 and §10).

### 8.9 Deterministic guardrails

Rules that can be stated and tested deterministically must not depend on eventual AI
oversight. An opt-out or channel prohibition known to Runtime must block an ineligible
Runtime-managed send immediately. A later AI manager may decide the strategic response, but
cannot be the only safety mechanism. An out-of-band agent can still bypass Jason by calling
an external provider directly; such an effect is outside Runtime's enforcement guarantees
until reported or reconciled.

The general rule: contractual, compliance, safety, eligibility, and lifecycle invariants
belong to Runtime; meaning, prioritization, strategy, and ambiguity resolution belong to AI
roles; AI oversight supplements deterministic enforcement rather than replacing it.

### 8.10 Approval and escalation

Runtime owns approval state and validates whether a proposed operation has the required
authorization. The eventual model must make approval scope mechanically enforceable; exact
action/payload binding and duration are deferred. A person's standing approval for one campaign and one category
of verdict lets the draft that verdict's own work made go without asking again (`docs/work-execution.md`,
"Standing approvals"). A campaign's allowance is the same for its searches under one of its audiences: set only by a
person, capped in people, it lets the top-ups under that audience go until the people they ask for reach its ceiling,
and the first search under an audience, its probe, always waits for a person.

AI roles may escalate when they cannot safely or confidently proceed — through broader or
more senior AI roles and ultimately to the human user. Escalation preserves structured
references to the causal work tree, attempts, events, artifacts, authoritative campaign
context, and relevant role memory so a fresh session can continue without reconstructing
everything manually.

### 8.11 Failure and recovery responsibility

Runtime must distinguish transient, permanent, validation, ambiguous, and blocked failures and apply
deterministic policies appropriate to each. It must prefer recovery and continuation over
blind replay of whole plans. For provider effects, the canonical operation contract must
expose idempotency and recovery-read (`before_repeating`) semantics where possible. For AI
work, the design must distinguish a retriable host failure from an accepted partial result
or a semantic escalation. Exact retry counts, backoff, leases, checkpoints, cancellation
protocol, and orphan recovery are deferred.

## 9. AI roles, skills, memory, and execution profiles

### 9.1 Role boundary

Roles represent semantic responsibilities: planning, campaign management, strategy,
research, review, personalization, and other specialist functions.

- Runtime may know built-in role identifiers and the operational metadata required to launch
  and govern them;
- skills define each role's SDR meaning, reasoning method, tool guidance, guardrails, and
  expected output behavior;
- Runtime does not hard-code the role's business reasoning;
- users may improve or override skills for supported roles;
- arbitrary new runtime-visible roles and a public role-plugin manifest are not required by
  the current architecture.

### 9.2 Campaign manager

Campaign manager is an AI role, not the dispatcher. The manager can interpret broad campaign
context, evaluate whether completed work leads to a coherent next step, notice semantic
blockers or invalid assumptions, request specialists, replanning, or strategic review,
reprioritize, postpone, cancel, or create supported work, explain why a campaign is blocked,
and escalate to another role or the human user. All authoritative effects still pass through
Runtime API and remain subject to validation, approvals, and deterministic guardrails.

### 9.3 Logical manager context per campaign

Each business campaign has a dedicated logical manager context and a manager memory of its
own at the campaign's level; the manager's notes at the client's and the project's level are
its own craft, shared across the campaigns they cover (DEC-ROLE-003).
This does not imply an immortal process or a permanently open chat session. Runtime may
start a fresh agent session for each invocation and hydrate it with current authoritative
campaign projections, relevant work/events/results/artifacts, the manager's memory at the
three levels, the campaign's its own, the explicit review intent, and allowed operations plus
escalation context. Identity and continuity persist even when compute sessions are ephemeral.

### 9.4 User-owned AI environment

Jason ships no AI client, model, model-provider account, or provider credentials. The user
may use one or many independently installed and authenticated AI environments. Two paths:

- **interactive path** — the user starts any capable AI session and that session invokes
  Jason CLI;
- **background path** — Runtime starts an eligible AI work unit through a supported Agent
  Host integration.

An interactive environment can use Jason without being launchable by Runtime.

### 9.5 Agent host, model provider, model, and execution profile

Four distinct concepts:

- **agent host** — the executable or session environment Runtime can launch;
- **model provider** — the model service used by that host;
- **model and effort** — execution choices meaningful within the host/provider;
- **agent execution profile** — user-owned, non-secret Jason configuration identifying a
  launchable host and its defaults or host-specific options.

Authentication remains owned by the user-installed host. The profile selects an already
configured execution path; it does not contain raw model-provider credentials.

### 9.6 Execution-profile resolution

When an AI work unit executes, its agent execution profile resolves in this order:

1. explicit work-unit override;
2. campaign- or role-level execution policy;
3. the most relevant inherited profile in the work's causal execution lineage;
4. the configured global default profile.

This allows a user to state, for example, that planning uses one host/model while
implementation uses another, while preserving intuitive continuity when no override exists.

### 9.7 Causal execution lineage

Inheritance is not limited to the immediate creator. Deterministic work, provider
operations, events, and dispatcher-created reviews must not accidentally erase AI
provenance:

```text
planning session on host A
  → deterministic contact import
  → provider reply received
  → provider reply deterministically recorded and routed
  → event-triggered manager work
  → inherited host-A execution profile, unless a stronger policy overrides it
```

Runtime needs durable provenance sufficient either to recover the applicable profile from
the causal branch or to propagate a resolved lineage profile forward. The exact resolution
algorithm is deferred; it must detect missing or genuinely ambiguous lineage rather than
guessing.

Only genuine absence of a higher-priority choice permits resolution to continue toward the
global default. A present but invalid explicit profile, an invalid campaign/role policy, or
conflicting lineage that cannot be resolved safely **blocks visibly** rather than silently
changing the AI executor.

Execution lineage carries launch preference and provenance only. It does not inherit
approval, authorization, credentials, provider binding, campaign access, budget authority,
or an agent-session continuation identity.

Each AI attempt records the resolved execution-profile identity and revision, the resolution
source, and the effective host/model options used for that attempt. Later profile edits do
not mutate an already running attempt or rewrite its provenance.

### 9.8 Missing or unusable AI host

Installation and deterministic Runtime operation do not require an AI host. Runtime can
execute AI work only when the selected execution profile resolves to an installed,
authenticated, compatible host that supports the required unattended behavior. If no usable
profile exists: deterministic Runtime and provider work remain available where otherwise
valid; AI work becomes visibly blocked by a capability or configuration problem; Runtime
does not silently substitute an unrelated host or model; the user or another agent can
repair configuration or explicitly reroute the work.

## 10. Primary interaction and AI execution flows

### 10.1 Agent-first interactive planning

```text
User states a business goal
  → user-owned interactive AI agent loads relevant SDR/Jason skills
  → agent clarifies constraints and may use external research tools
  → agent proposes campaign decisions and work
  → accepted intent is submitted through Jason CLI
  → Runtime API validates and commits authoritative campaign state
  → dispatcher develops the durable work over time
```

The AI conversation remains flexible and natural-language-driven. The transition into
managed state is explicit and deterministic.

### 10.2 Direct human control

A user may invoke Jason CLI or Jason UI without an AI session — for status, diagnostics,
pausing, approvals, corrections, and direct business operations. The absence of an AI client
does not make Runtime unusable as software, but it removes much of the product's intended
semantic value; Jason does not solve that by bundling its own model client.

### 10.3 Background AI work

```text
dispatcher identifies eligible AI work
  → Runtime resolves role, context and agent execution profile
  → supported Agent Host integration starts a user-owned host session
  → session receives skills, scoped authoritative projections, memory and expected outcome
  → agent reasons and may use permitted external tools
  → agent returns structured result and/or invokes Jason CLI operations
  → Runtime validates and commits the accepted outcome
  → dispatcher releases a predefined continuation or schedules semantic review
```

### 10.4 Campaign-management review

```text
event, escalation, anomaly rule, or scheduled review
  → manager work becomes eligible
  → Runtime hydrates a fresh manager session with campaign context and memory
  → manager inspects Runtime projections and optional external evidence
  → manager proposes supported changes through Jason CLI
  → Runtime validates and commits them
```

The manager does not review every successful deterministic continuation; scheduled review
preserves broader oversight without placing AI in every execution path.

### 10.5 Human escalation

An unresolved AI escalation can reach the user through CLI, UI, notification plugins, or a
later interactive AI session. The user may ask their agent to locate the pending decision,
inspect the preserved context, discuss it, and submit the decision through Jason CLI.

## 11. External tool use and side-effect accountability

### 11.1 Three distinct cases

**Case A — semantic agent tool use.** An interactive or background AI agent may call CLI,
MCP, API, web, or research tools directly while investigating, interpreting, or making a
semantic decision — reading CRM data, researching a market, comparing contacts, examining a
conversation.

**Case B — Runtime-managed canonical operation.** When an external side effect is already
fully determined and belongs to a managed campaign, the preferred path is a canonical
operation submitted to Runtime before the effect occurs. Runtime then owns validation and
eligibility, approval enforcement, provider route and binding resolution, correlation
identity and the declared idempotency/recovery contract, the managed attempt, timeout/retry/
cancellation/recovery policy, normalized outcome validation, and authoritative history plus
downstream continuation.

**Case C — post-factum external-effect report.** An AI agent or user may nevertheless
perform a side effect directly through an external tool. This is allowed: Jason does not
control every user-owned agent, skill, or tool ecosystem and does not pretend it can prevent
out-of-band actions. The actor should then report the completed effect through Jason
CLI/Runtime API as promptly as possible.

### 11.2 Managed execution is strongly preferred, not technically exclusive

Skills strongly guide agents to submit campaign side effects through Runtime whenever the
canonical operation exists — that route provides guarantees that cannot be recreated after
the fact. Direct external execution remains possible. The resulting rule:

> Jason prefers managed execution for campaign side effects, permits external side effects,
> and strongly expects prompt, provenance-bearing post-factum reporting rather than
> pretending the external action did not happen.

### 11.3 Meaning of a post-factum report

A post-factum report is not a retroactive managed attempt. It records, as available: that
the reporter claims or observes the effect occurred outside Runtime; who or what reported
it; the claimed or observed occurrence time; the actual provider, tool, account/profile or
binding context, and external identifiers where known; any claimed association with a Jason
route, explicitly marked unverified unless independently established; payload summary or
evidence; observed result and uncertainty; missing information; and correlation to relevant
campaign, work, contact, or operation intent.

It must not claim that Runtime approved the action before execution, enforced idempotency,
selected or verified the route, supervised the attempt, guaranteed the result, performed
retries or recovery, or possessed complete evidence.

Admission validation establishes that a report is well-formed and attributable; it does not
prove that every reported fact is true. Later provider evidence may confirm or contradict
the report; reconciliation can update Runtime's current view without erasing the original
assertion, its provenance, or its audit history.

### 11.4 Reconciliation and acknowledged incompleteness

An accepted external-effect report may update a provenance-qualified current view and cause
future work to be held or reconsidered, according to explicit policy. Admission never makes
the original effect approved or compliant. Runtime may later discover unreported effects by
reading provider state through deterministic synchronization or AI-assisted investigation;
the exact reconciliation model is deferred.

Until an external effect is reported or discovered: Runtime has an incomplete view of the
world; duplicate or contradictory future action is possible; approvals and audit history are
incomplete; skills and observability should make that risk visible. This is an accepted
consequence of combining an open user-controlled agent environment with a managed local
Runtime.

### 11.5 Direct provider reads and local authority

An agent may read provider data directly and use it for reasoning. If the resulting fact
must govern future managed behavior, it should be submitted or reconciled into Runtime
through a supported operation. A transient observation in a chat or memory file does not
become authoritative merely because the model saw it.

## 12. Canonical provider operations

### 12.1 Purpose

Runtime defines strict, versioned, vendor-neutral business operations for deterministic
provider effects. Managed work refers to these canonical operations rather than to provider
CLI command names or API endpoints.

```text
eligible managed work
  → canonical vendor-neutral operation
  → route and binding resolution
  → provider plugin
  → provider CLI or supported provider transport
  → normalized canonical outcome
  → Runtime validation and authoritative commit
```

### 12.2 Canonical semantics

Each operation eventually defines: a stable identifier and version; input schema and
validation; output and normalized outcome schema; side-effect and reversibility
classification; approval requirements; idempotency and correlation expectations;
`before_repeating` or other recovery-read behavior; transient, permanent, validation, ambiguous,
and blocked failure semantics; timeout and cancellation expectations; capability requirements;
and evidence needed for conformance and reconciliation.

The exact catalog and schemas are deferred. Runtime need not expose an entire vendor-neutral
SDR operation catalog at once; the architecture only requires that supported managed effects
use stable canonical semantics.

**In this build:** the audience family finds a campaign's people at a provider. `audience.search` is one metered search
the provider runs, approved by a person or let go by the campaign's allowance (§8.10); `audience.search_status` reads it
and `audience.search_cancel` stops it, by the reference the provider gave, wherever the search ran. Three reads answer a
campaign on request and are never work: `audience.preview` counts a definition and samples a few of its people,
`audience.filter_values` lists the values one of the provider's filters takes, and `audience.availability` says whether
the account may search now. A definition is the provider's own shape, held at the claim to the schema the routed plugin
declares, never a neutral language of criteria (`docs/contracts/operations/audience.search.md`, `docs/routing.md`).

### 12.3 Plugin responsibility

A plugin understands both sides of the boundary — Jason's canonical operation meaning and
the target vendor's CLI/API behavior. It validates and transforms canonical input into
provider input, invokes one or more provider calls, normalizes provider results, maps
provider errors into canonical failure classes, preserves correlation and idempotency where
the provider permits, reports ambiguity honestly, declares supported operations and contract
versions, and uses provider authentication without leaking secrets into ordinary Runtime
data. The plugin may be vendor-specific; Runtime core may not be.

### 12.4 Existing provider tools do not implement Jason's contract

An existing CLI or API is not expected to copy Jason's operation names, parameters, result
shapes, and failure semantics — that would be an unrealistic integration barrier. The
plugin performs the translation: a canonical operation may map to one CLI command, several
CLI calls, a provider API, or a composition of provider capabilities.

The selected minimum Host SDK guarantees structured process execution through `host.exec`.
An API-backed plugin can delegate to an allowed provider CLI or HTTP-client executable
through `host.exec`. Plugin JavaScript does not receive native unrestricted network access
in the selected minimum SDK; a first-class `host.http` capability and its security policy
remain deferred.

### 12.5 CLI as preferred deterministic provider tool

Short-lived vendor CLIs are the preferred downstream mechanism when available because they
commonly already own authentication and token refresh, profile/account/organization/team
selection, endpoint routing and serialization, provider error handling, structured output,
and provider-specific release behavior. They are language-neutral from Runtime's
perspective, naturally process-isolated, and directly usable by AI agents during semantic
work. Reply CLI is therefore the default provider tool for the official Reply plugin.

### 12.6 MCP boundary

MCP remains an agent tool, not the deterministic Runtime provider transport. Interactive and
background AI sessions may use MCP servers directly. Runtime does not initially become a
generic MCP client responsible for server discovery, sessions, authentication, lifecycle,
compatibility, and transport recovery merely to perform deterministic work. If a future
concrete need justifies deterministic MCP execution, that boundary must be reopened
explicitly.

### 12.7 AI is not a deterministic tool proxy

Runtime must not launch an AI session merely to hope that the agent discovers and invokes
the correct provider command for an already prepared effect. Known input, route, approval,
timeout, and expected output belong on the canonical plugin path. AI belongs where tool
selection, payload construction, interpretation, or recovery genuinely requires reasoning.

## 13. Provider routing and plugin bindings

### 13.1 Routing and binding answer different questions

- **plugin routing** answers: *which plugin implementation performs this canonical
  operation?*
- **plugin binding** answers: *which configured provider identity, account, workspace, or
  environment should that plugin use?*

Combining them into one opaque concept would either leak vendor details into Runtime core or
force stateful configuration into a stateless plugin.

### 13.2 Routing scopes and precedence

Runtime supports provider composition at global and campaign scopes. The accepted
precedence:

```text
campaign operation override
  → campaign default plugin
  → global operation override
  → global default plugin
```

A campaign default intentionally masks global operation-specific routes; preserving a global
exception inside that campaign requires an explicit campaign operation override. This
precedence is part of the accepted composition model. The exact configuration format is
deferred; whether operation-family wildcard patterns are part of the first schema is
unresolved.

An operation whose contract names the argument a route reads its channel from may also be routed per channel, so that
one campaign sends email through one plugin and LinkedIn messages through another. A channel route sits above its
operation's own route at its scope, a plugin that reads no events may not take one, and the rest of routing keeps its
rule unchanged: masking is exact and nothing falls back to another plugin. The operations, the levels and the checks
are in [`docs/routing.md`](routing.md).

### 13.3 Reply as configured default

The installer or bootstrap configuration can register Reply as the global default plugin.
Runtime core does not contain an implicit rule that an absent route means Reply. This
preserves both goals: a zero-effort Reply-connected default experience, and genuine vendor
neutrality in the Runtime.

### 13.4 Fail-closed behavior

If the selected plugin is missing, incompatible, disabled, invalid, or does not support the
operation — or if its required binding is missing, invalid, incompatible, or unresolved —
managed work blocks with a clear failure. Runtime does not silently fall back to another
provider for a side-effecting operation. A deliberate route change is a configuration
action, not an error-recovery guess.

### 13.5 Plugin-scoped binding context

Runtime supports opaque or extensible plugin-scoped binding context associated with the
appropriate global or campaign route. For the Reply plugin, this context may select a Reply
CLI profile; one installed Reply CLI can hold multiple profiles representing different
credentials, users, organizations, teams, or environments. Runtime remains unaware of those
Reply concepts:

```text
Runtime
  resolves plugin = reply
  passes non-secret binding selector/context

Reply JavaScript plugin
  interprets binding.profile
  prepares Reply CLI arguments

host.exec
  executes the prepared command
  knows nothing about profiles, organizations or teams
```

Credentials remain owned by Reply CLI; the selector stored or passed by Runtime is not
itself the credential. Another plugin may define entirely different binding fields; the
plugin owns their interpretation and validation. Exact binding schema, validation
handshake, resolution hierarchy, and whether bindings are named and reusable are deferred.

### 13.6 Stateless plugins and campaign context

A plugin does not own campaign state and does not retain mutable profile selection between
invocations; Runtime supplies the relevant binding and permitted invocation context each
time. This preserves plugin statelessness, per-campaign provider selection, concurrent
invocation (subject to provider and rate-limit constraints), vendor neutrality of Runtime
core, and explainability of the resolved execution context.

### 13.7 Invocation pinning

Once a managed attempt resolves its execution path, Runtime records enough provenance to
explain and recover the attempt: plugin ID and version; plugin/operation contract version;
canonical operation version; routing revision or immutable snapshot identity; binding
identity or non-secret resolved binding provenance; binding revision; invocation and
correlation identity.

Changing routing or plugin packages does not mutate an already running attempt. A plugin
ID and version are not sufficient if package contents can be edited in place: detailed
design must give the executed package an immutable content identity (digest or snapshot)
before claiming reproducible plugin execution.

## 14. JavaScript plugin and Plugin Host architecture

### 14.1 Selected physical plugin model

A plugin is a stateless JavaScript package executed by a trusted, short-lived Plugin Host
process supplied by Jason. The official Reply plugin and community plugins use the same
package format, Host SDK, process boundary, routing, and validation. There is no private
in-process Reply implementation.

### 14.2 Package structure

```text
<plugin-id>/
├── plugin.yaml
├── main.js
└── modules/          # optional local JavaScript modules
```

The manifest declares: plugin ID and version; supported contract versions; implemented
canonical operations; required executables and compatible versions; requested Host SDK
capabilities; permitted environment-variable names; resource or timeout expectations; entry
module and function; and future trust or publisher metadata. One plugin package may
implement many operations and may cover only part of the canonical contract.

### 14.3 JavaScript execution contract

The semantic entry point is conceptually:

```javascript
invoke(operation, input, context)
```

Plugin JavaScript receives validated canonical input and permitted invocation context,
performs provider-specific pre-processing and mapping, calls vendor tools through the Host
SDK, may compose several provider calls, maps outputs and errors into a canonical result,
and returns no durable hidden state. Exact module syntax and type definitions are deferred.

### 14.4 Why JavaScript and Jint

JavaScript is the single selected plugin language: cross-platform, familiar to contributors
and coding agents, expressive enough for non-trivial mapping, and embeddable without
requiring an external Node, Python, PowerShell, or Bash environment. Jason embeds
[Jint](https://github.com/sebastienros/jint), a .NET JavaScript interpreter, into its
self-contained distribution. Plugin source uses JavaScript syntax but runs in the Jason
plugin environment, not Node or a browser. The selected model does not promise npm packages
or arbitrary external JavaScript dependencies; optional local modules are bundled with the
plugin.

### 14.5 One binary, separate process mode

Jason does not publish a second large self-contained Plugin Host binary. The same
platform-specific Jason executable is launched in a dedicated internal plugin-host mode for
each invocation:

```text
long-running Jason Runtime process
  → starts same Jason executable in short-lived plugin-host mode
  → child loads one plugin invocation
  → child returns outcome and exits
```

This retains process isolation without duplicating the .NET runtime, installer, release, and
compatibility lifecycle. Plugin JavaScript never runs in the long-lived Runtime process.

### 14.6 Runtime-to-Host protocol

The conceptual boundary uses redirected standard streams: command-line arguments contain
only small, non-sensitive launch metadata (mode, plugin identity, protocol identity);
canonical invocation JSON is written to child stdin; one canonical outcome JSON is written
to stdout; diagnostics and plugin logs go to stderr; process exit status represents
Host-protocol completion, while business success/failure remains in the structured outcome.
Secrets and large payloads do not belong in process arguments. Exact envelope schemas are
deferred.

### 14.7 Meaning of `host.exec`

Standard ECMAScript cannot launch operating-system processes, and Jint does not provide
Node's `child_process` APIs. The C# Plugin Host explicitly injects a restricted global
object named `host` into the Jint engine; `host.exec` is a JavaScript-visible method backed
by trusted C# process-execution code:

```text
plugin JavaScript calls host.exec(request)
  → Jint invokes the explicitly registered C# binding
  → C# starts the vendor executable with a structured argument array
  → C# redirects stdin/stdout/stderr and applies limits
  → structured process result returns to plugin JavaScript
```

`host.exec` is **not**: an executable named `host.exe`; a JavaScript wrapper process; Node's
`child_process.exec`; direct CLR access; arbitrary shell-string interpolation; or a magical
interception of unknown JavaScript calls.

### 14.8 Selected minimum Host SDK boundary

The selected minimum Host SDK centers on structured executable invocation with an argument
array; redirected stdin, stdout, and stderr; timeout, cancellation, and output limits;
allowlisted environment access; and structured logging with redaction hooks. The C#
implementation does not invoke a shell merely to execute a composed string. Direct HTTP
support is a possible later capability requiring an explicit destination, credential, retry,
and redaction policy; it is not assumed.

### 14.9 Isolation and trust

Isolation is layered: Jint receives only explicit Host SDK bindings and configured
interpreter limits; plugin code runs in a separate short-lived OS process; manifest and
policy constrain expected capabilities and executables; Runtime validates the canonical
result again after the child exits.

By default, plugin JavaScript must not receive direct access to Runtime SQLite, internal
APIs, arbitrary CLR classes, unrestricted filesystem paths, unrelated environment variables,
or long-lived Runtime memory.

**This is not a Docker, VM, or kernel security sandbox.** The Plugin Host runs under the
local user's OS identity. Plugin packages are executable local code and require an eventual
trust, provenance, signing, warning, and resource-control model. A manifest *requests* and
declares capabilities; it does not grant them — user or Runtime policy decides which
capabilities are permitted for an installed plugin.

### 14.10 User-scoped registration and atomic reload

Plugin packages and routing configuration live in Jason's user-scoped data area, separate
from replaceable application binaries. Runtime does not require filesystem watchers; an
explicit Jason operation reloads the plugin registry:

1. scan candidate packages;
2. parse and validate manifests;
3. validate IDs, versions, contracts, operations, requested capabilities, and routes;
4. build a new immutable registry/routing snapshot;
5. replace the active snapshot only if the entire candidate configuration is valid.

If reload fails, the previous valid snapshot remains active and diagnostics identify the
problem. Files in the user data area are candidate inputs; the active registry and
routing/binding snapshot become authoritative only after successful Runtime-controlled
validation and activation.

### 14.11 Plugin statelessness and concurrency

Each Host process handles one invocation and exits; JavaScript globals do not survive.
Durable orchestration state remains in Runtime; provider state and credentials remain in
provider-owned systems. Runtime may launch several Plugin Hosts concurrently subject to
global, plugin, provider, operation, and external-rate limits. Startup overhead is accepted
because provider network latency usually dominates and process isolation simplifies
correctness. Warm pools or persistent plugins may be reconsidered only when evidence
justifies reopening the lifecycle boundary.

### 14.12 Official Reply plugin

The official Reply plugin dogfoods the public architecture: `plugin.yaml` declares
identity, compatibility, capabilities, and operation coverage; `main.js` and local modules
implement canonical mappings; `host.exec` invokes Reply CLI; Reply CLI owns authentication,
profiles, organization/team context, endpoint calls, and structured provider responses;
plugin JavaScript interprets the opaque binding context and maps canonical outcomes;
Runtime routes to Reply through normal configuration.

If Reply cannot be implemented cleanly through the public Host SDK, the extension model must
be improved rather than bypassed privately.

### 14.13 Community plugin ownership

Community and customer plugins are an advanced but legitimate extension path. Their authors
or adopters own correctness, security, vendor compatibility, installation, testing, updates,
and migration across contract versions. Jason must make the contract documented,
deterministic, testable, and possible; it does not need to make third-party provider
integration effortless in order to remain honestly extensible.

## 15. Product surfaces, platform, distribution, and updates

### 15.1 One application contract, several surfaces

Runtime API is the behavioral core shared by Jason CLI administrative commands, Jason CLI
business operations, Jason UI, user-owned AI agents invoking Jason CLI, and future SDK, MCP,
editor, or automation clients where justified. Business rules and transitions live in
Runtime once; clients render or invoke them.

### 15.2 Selected implementation platform

The accepted Runtime baseline: **.NET**, **EF Core**, **SQLite** as the only selected
Runtime database, platform-specific **self-contained publication**, a user-scoped
long-running background process, and no separate .NET installation required for the user.

.NET is selected for mature hosting, local API, concurrency, cancellation, configuration,
structured logging, dependency injection, testing, SQLite, migrations, and domain-modeling
support — and because the maintainers' deep .NET expertise materially reduces delivery and
architectural risk. Go was the strongest alternative (compact binaries, operational
simplicity) but does not outweigh the development leverage and richer domain-application fit
here. TypeScript/Node remains suitable for surrounding components but not the selected
deterministic core.

The runtime is being rewritten in TypeScript in this repository, decided on 5 November 2026, which
supersedes both the rejection of TypeScript/Node as the core platform (REJ-PLAT-001) and the port
to Rust decided on 30 October 2026. Plugins stay JavaScript, under the contract the runtime
enforces, whichever language the runtime is written in. Until the rewrite is complete, the C#
runtime under `dotnet/` is the one released, and the baseline above is the one it is built on.

### 15.3 EF Core, Dapper, and Native AOT

EF Core is the accepted persistence and migration technology. Dapper is explicitly excluded
as an active alternative: trading away model integration, migrations, productivity, and
maintainability merely to reduce distribution size is not justified. Native AOT is deferred;
it may be reconsidered when EF Core and the required dependency ecosystem are mature and
low-risk under AOT.

### 15.4 Self-contained platform builds

The distribution contains the .NET runtime dependencies required by Jason and is built for
supported operating-system and architecture combinations. The package may be tens or
hundreds of megabytes; that size is accepted as a minor cost compared with product
reliability and development leverage. The same JavaScript plugin packages run across
supported platforms, while the Jason executable and underlying vendor CLIs remain
platform-specific. The exact supported OS/architecture matrix must be limited to
combinations that can be built, signed, and tested.

### 15.5 Main product entry point

`reply-team/jason-ai` is the primary marketed public entry point: a new user should see one
coherent product and guided start rather than discovering several repositories in the correct
order. Two legitimate installation paths:

- **Reply-connected path** — Reply CLI detects the platform, installs Jason and relevant
  skills, configures the Reply plugin, and connects the user's Reply account/profile;
- **standalone path** — a user or their coding agent installs Jason from the public
  repository's release assets without requiring Reply CLI.

Reply CLI is a convenient installer, authentication surface, and default provider tool — not
a licensing gate around Runtime. A standalone install never silently implies a Reply
dependency or a preconfigured Reply route.

### 15.6 Background registration

Runtime starts as a user-scoped background process and can remain alive after the initiating
agent session ends. Platform-specific registration may use a Windows user-session mechanism,
a macOS LaunchAgent, or a Linux user service. It is not a privileged system-wide service by
default.

### 15.7 Release artifacts

A tagged version, not every merge to `main`, is the conceptual release trigger for
distributable binaries. CI builds and tests supported self-contained assets and publishes
versioned GitHub Release artifacts with integrity metadata, release notes, and each
platform's signature where it has one: the macOS executable is signed with Reply's Developer
ID and notarized, the Windows executable is signed through Azure Artifact Signing with a
certificate issued to Reply and timestamped, and the Linux executable is not signed
(DEF-DIST-001). The Windows signing identity is reached only from the release's Windows job,
in the GitHub environment `signing`, through that job's own OIDC token; no client secret is
stored. GitHub Releases can serve as the binary store. The product's own address,
`get.jasonharness.ai`, stands in front of it: the feed, the install lines and the release page
name the address, which redirects every request under `/releases/` to the same path in the
public repository, so the store behind it can change while those paths stay the same. This
mechanism describes artifact integrity and update compatibility; it does not impose a
roadmap, and source increments are not atomic product releases. Each release is built from a
tagged tree of this repository and published in it.

### 15.8 Update responsibility

.NET does not automatically update a self-contained application; Jason owns its update
behavior. The long-running Runtime may detect and advertise that a compatible update is
available. It does not overwrite its own running binary: a short-lived Jason CLI or
installer process performs controlled replacement. Architectural update requirements:
identify platform and compatible version; verify artifact integrity and platform trust; stop
accepting new work and drain or account for running work; stop Runtime before replacing
binaries; protect SQLite state before migrations; install and migrate coherently; restart
and verify health; preserve a viable rollback strategy where executable and schema
compatibility permit. There is no second permanently running updater daemon.

### 15.9 No Docker or service topology requirement

The accepted local product does not require Docker, Compose, PostgreSQL, Kafka, a service
mesh, or vendor-specific background services. Additional infrastructure would materially
harm onboarding and operation without solving a demonstrated need. This does not prohibit
future server or managed variants under different requirements.

### 15.10 Repository layout

The repository is organised by deliverable, not by one language's convention, because it
carries the runtime, the plugin marketplace and the skills together. The C# runtime sits under
`dotnet/`, which keeps the root free for the workspace of the TypeScript rewrite (15.2):

```text
jason-ai/
├── dotnet/                all C#: Jason.slnx, Directory.Build.props
│   ├── src/Jason.App      the single published executable; routes its arguments into the
│   │                      cli, runtime-service and plugin-host modes
│   ├── src/Jason.Cli      pure HTTP client of the Runtime API; references Jason.Contracts only
│   ├── src/Jason.Runtime  domain, EF Core over SQLite, dispatcher, Runtime API
│   ├── src/Jason.PluginHost  embedded JavaScript engine and Host SDK
│   ├── src/Jason.Contracts   DTOs and operation names shared by server and clients
│   └── tests/             xUnit projects mirroring src/
├── plugins/               the plugin marketplace: one directory per provider plugin
├── skills/runtime/        runtime skills
├── skills/business/       business skills
├── .claude-plugin/        the skills marketplace: offers both packs to an agent harness by
│                          relative path. A different thing from plugins/ above — that one holds
│                          JavaScript that executes provider operations, this one holds no code
├── docs/                  maintained documentation
├── install/               the install scripts and the update feed's schema
├── global.json            pins the .NET SDK; commands run from the repository root
└── README.md  CLAUDE.md  LICENSE  THIRD-PARTY-NOTICES  TRADEMARKS.md
```

Project references enforce two invariants at compile time: the CLI can only reach the runtime
through the Runtime API (INV-API-001, INV-AUTH-002), and the runtime library references neither
the plugin host nor the JavaScript engine — only the executable composes both — so plugin
JavaScript has no path into the long-running runtime process (INV-ADP-003).

## 16. Cross-cutting architectural qualities

### 16.1 Safety and approval enforcement

For Jason state transitions and Runtime-managed effects, Runtime mechanically enforces
safety and approval rules that can be expressed deterministically. AI roles may explain,
recommend, or escalate, but cannot bypass managed invariants through supported contracts,
direct storage writes, or invented transitions. Out-of-band use of external tools remains
outside this enforcement until reported or reconciled. Managed effects receive the strongest
guarantees because Runtime owns them before execution; external post-factum reports improve
awareness but do not recreate pre-execution approval.

### 16.2 Reliability, idempotency, and ambiguity

Reliable operation requires more than retrying failed calls. The architecture requires:
stable correlation and invocation identities; idempotency keys where the canonical operation
supports them; explicit recovery reads (`before_repeating`) where necessary; honest
classification of transient, permanent, validation, ambiguous, and blocked failure; durable attempt
and configuration provenance; recovery by reading known state before replaying side effects;
separation of provider uncertainty from Runtime protocol failure; and continuation from
accepted work rather than blind replay of a plan. These guarantees apply to Runtime-managed
operations according to their contracts; direct external effects may lack them and must be
represented with lower assurance.

### 16.3 Reconciliation

Runtime coordinates a world it does not fully own: provider state changes independently,
users act directly, credentials expire, agent hosts disappear, machines sleep.
Reconciliation therefore exists at several levels: deterministic verification of known
external facts; scheduled checks for missing progress or stale work; campaign-manager review
of semantic drift; post-factum registration of externally performed effects; repair of
missing capabilities, bindings, routes, or host profiles. The detailed reconciler
architecture is deferred; the requirement to detect divergence rather than assume perfect
isolation is accepted.

### 16.4 Security and secrets

The local-first model reduces centralized custody but does not eliminate risk. The
architecture requires: Jason-owned components never place raw credentials in Jason-managed
state, manifests, routes, work payloads, prompts they construct, artifacts, or logs;
provider authentication remains with provider CLI/secret mechanisms; agent-host
authentication remains with the user-owned host; allowlisted rather than unrestricted
plugin environment access; structured process invocation rather than shell interpolation;
result and log redaction; explicit plugin trust and provenance; least-capability Host SDK
design; and no direct plugin or agent access to SQLite. Host SDK code may transiently read
an allowlisted environment value or credential reference when a provider integration
genuinely requires it; that does not authorize durable storage or logging of the secret.

### 16.5 Plugin trust boundary

Community plugin code is **executable local code**. Jint constraints and child-process
isolation reduce blast radius and improve termination, but do not make the code untrusted in
the way a kernel-sandboxed workload is. Installation, signing, publisher identity, warnings,
capability review, and resource restrictions remain important deferred design. Documentation
must not claim that plugins are fully sandboxed.

### 16.6 Observability and audit

The user, interactive agent, CLI, UI, and manager roles need explainable views of: Runtime
health and version; campaigns and current operating state; queued, scheduled, running,
blocked, awaiting-approval, failed, cancelled, and completed work; attempts, agent sessions,
provider invocations, and durations; structured results and artifacts; retry, recovery,
escalation, and reconciliation decisions; plugin route, binding, contract, and
execution-profile provenance; externally reported versus Runtime-managed effects; and update
availability and drain/update state.

### 16.7 Provenance and reproducibility

Runtime preserves enough provenance to explain why an attempt used a particular role, agent
profile, plugin, binding, operation version, and route. Pinning code/configuration context
improves reproducibility, but cannot make an external provider's changing state perfectly
replayable. The architecture requires explainability and recoverability, not a false promise
of identical replay.

### 16.8 Versioning and compatibility

Versioning boundaries include: Runtime API and clients; canonical operation contracts;
plugin protocol and manifest; plugin and operation implementations; Host SDK; Agent Host
integrations and execution profiles; skills and built-in role metadata; SQLite schema and
application version. Runtime and plugins must declare compatibility and fail clearly before
execution when versions do not overlap. The official Reply plugin evolves with Runtime;
community plugin maintainers own their migrations.

### 16.9 Local-first portability

Jason's operational core should install on supported Windows, macOS, and Linux environments
without Docker or a server database. Local-first means local orchestration ownership and a
local control plane — not offline operation, remote-provider independence, or automatic
synchronization across machines.

### 16.10 Extensibility boundaries

The accepted extension point is the provider plugin architecture; skills are also openly
inspectable and extensible as semantic guidance. The architecture deliberately does not yet
promise: arbitrary runtime-visible role plug-ins; arbitrary compiled code inside Runtime;
several plugin languages; a generic long-running provider-service protocol; a marketplace
or package trust ecosystem; automatic database-provider portability. Extensibility grows
from demonstrated needs.

## 17. Canonical end-to-end scenarios

These scenarios demonstrate how the accepted components fit together. They are architecture
examples, not a release plan or final command/API specification.

### 17.1 Interactive objective to durable campaign work

**Trigger:** the user tells a user-owned AI agent they want to sell a product in a new
market.

1. The agent loads planning and SDR skills.
2. It asks for missing objective, audience, geography, constraints, approvals, and provider
   context.
3. It may use web, CRM, provider CLI/MCP, or other tools to gather evidence.
4. It proposes a campaign direction and initial work.
5. On acceptance, it invokes Jason CLI business operations.
6. Runtime validates and commits the campaign, accepted plan, work, execution-profile
   provenance, and relevant provider routing/bindings.
7. Dispatcher develops eligible work after the interactive session ends.

**Boundary:** only Runtime API commits Jason state; the conversation and skill output are
not authoritative by themselves.

### 17.2 Background semantic work with deterministic continuation

**Trigger:** research work becomes eligible.

1. Runtime resolves the researcher role, campaign context, execution lineage, and agent
   execution profile.
2. Agent Host integration launches a supported user-owned background agent.
3. The agent receives current Runtime projections, role skills, memory, artifacts, and an
   expected result.
4. It performs research and returns the required result.
5. Runtime validates and accepts the outcome.
6. If the accepted plan already defines personalization as the next step, dispatcher
   releases it without manager review.

**Failure behavior:** if no usable host exists, work blocks visibly. If semantic output is
invalid, Runtime does not pretend it completed successfully.

### 17.3 Deterministic managed provider operation

**Trigger:** an approved prepared email operation becomes eligible.

1. Runtime validates eligibility, approval, the canonical payload, and the operation's
   declared idempotency/recovery requirements.
2. Routing selects the plugin; binding selects the configured provider identity/workspace.
3. Runtime pins execution provenance and launches the short-lived Plugin Host.
4. Jint loads the plugin; plugin JavaScript maps input and calls `host.exec`.
5. C# safely starts Reply CLI or another selected vendor CLI.
6. Plugin JavaScript normalizes the provider outcome.
7. Runtime validates the result, commits the attempt and external identifiers, and releases
   valid continuation.

**Failure behavior:** missing route, incompatible plugin, malformed result, or ambiguous
provider outcome blocks or recovers according to the canonical contract. Runtime does not
launch AI merely to make the call.

### 17.4 Direct external effect followed by reporting

**Trigger:** a user-owned AI agent sends or changes something directly through an MCP or CLI
tool rather than submitting a managed operation.

1. The external provider executes the side effect outside Runtime supervision.
2. Skills instruct the agent to report the effect through Jason CLI.
3. Runtime validates a post-factum external-effect report with explicit provenance and
   available evidence.
4. Runtime records uncertainty and missing fields rather than inventing managed guarantees.
5. Dependent future work is reconsidered or reconciled.

**Failure behavior:** until the report is accepted or the effect independently discovered,
Runtime may have an incomplete view and may plan a duplicate action. That limitation is
visible and acknowledged.

### 17.5 Reactive manager work after an inbound event

**Trigger:** a deterministic synchronization observes an inbound reply and records a
condition that requires semantic action.

1. Runtime accepts the external event and applies deterministic safety/state rules.
2. A rule releases manager or specialist AI work immediately.
3. The work inherits the relevant causal execution profile unless a stronger override
   applies.
4. The launched AI role gathers context, interprets the opportunity, and proposes follow-up
   work.
5. Runtime validates and commits supported changes.

**Boundary:** the trigger is deterministic; the opportunity strategy is semantic.

**In this build:** the synchronization is a scan step that reads each linked active campaign's conversations on a
cadence and writes one `inbound_reply_observed` line for each thread that moved. A respond step after it reads that
line and, for a thread the campaign holds by its own pin, queues a read of the thread (`conversation.get`) and the
responder's work created waiting on it — one open responder a thread, a further move recorded on it — so the reply is
answered without waiting for a review. The kind is also a default manager trigger, so the same line releases a manager
check-in, queued after the responder, which decides at its review what the campaign as a whole calls for
(`docs/inbound-sync.md`). A plugin that reads its accounts' events says the same line from the provider's own record,
and a bounce, a reply the provider detected or an accepted invitation as `provider_event_observed`, which releases a
check-in the same way and wakes no responder (`docs/events.md`).

### 17.6 Scheduled campaign-management review

**Trigger:** a configured management-review schedule becomes due despite no new transition
event.

1. Dispatcher creates or releases scoped manager work.
2. Runtime resolves campaign/role policy, inherited lineage where applicable, or the global
   execution default.
3. A fresh manager session receives current campaign projections and the manager's memory at
   the three levels, the campaign's its own.
4. It searches for stale work, blockers, changing evidence, invalid assumptions, or missing
   progress.
5. It proposes reprioritization, new work, replanning, escalation, or no change.
6. Runtime validates and records the result.

### 17.7 Human escalation resumed from a new agent session

**Trigger:** an AI role escalates a decision to the user.

1. Runtime records the pending decision and its structured causal context.
2. The user later opens any compatible interactive agent and asks about pending Jason
   questions.
3. The agent queries Jason CLI, loads relevant skills and evidence, and explains the
   decision.
4. The user decides; the agent submits the decision through Jason CLI.
5. Runtime records the exact approval or correction and releases eligible continuation.

**Boundary:** continuity comes from Runtime state and references, not survival of the
original chat session.

## 18. Accepted decision register

**Accepted** means part of the target vision, not proof that the capability is implemented.

| ID | Accepted decision |
|---|---|
| DEC-PROD-001 | The product is Jason AI, an AI SDR harness: a local-first, vendor-neutral SDR domain runtime, open source and distributed as self-contained binaries, together with its skills, contracts, plugins, documentation and install path, presented as one coherent product. |
| DEC-PROD-002 | The product combines deterministic Runtime, skill-guided AI reasoning, and provider execution plugins. |
| DEC-OSS-003 | The runtime, its source and everything else in `reply-team/jason-ai` are open source under the MIT licence (`LICENSE`) and developed in that repository, with the trademark note `TRADEMARKS.md` beside it: the licence grants no right to the marks. Hosted provider capabilities remain external services. Supersedes SUP-OSS-001 and DEC-OSS-002. |
| DEC-ENTRY-001 | `reply-team/jason-ai` is the intended primary public product entry point. |
| DEC-REPO-001 | One repository, resolving DEF-REPO-001. `reply-team/jason-ai` (public) holds the whole product, the runtime's source included, with its history; it is the product entry point and the binary store. Until 0.24.0 the product was developed in a private repository, and each release published a copy of its open part here. |
| DEC-REPLY-001 | Reply is the configured ready-to-use default provider and commercial execution path, not a hard-coded dependency. |
| DEC-TOPO-001 | One user-scoped Runtime installation manages multiple logically isolated business campaigns for one OS user. |
| DEC-PROJ-001 | Every campaign is in exactly one project — one business goal of the client, a runtime entity of Jason's own that never reaches a plugin; the client is the installation and has no entity. Every installation has one default project, made by the migration that brought projects in under an id of its own; a campaign that names no project lands there, and a campaign can be moved. A project's slug is made once, is unique and never changes, which is also why `project.create` needs no idempotency key: a repeat is refused as `project_exists`, naming the project. `docs/projects.md` is the contract. |
| DEC-PREF-001 | What a person states about the client as a whole — who messages are written as, how the company sounds, which agent host opens the person's own sessions — is a named preference: one value per key for the installation, at the client's level only, each key a document of its own under `docs/contracts/preferences/` whose schema the value is checked against, a family of its own beside the operations. Only a named person sets or unsets one, as only a person decides an approval; a launched role reads it and never writes it. Unsetting deletes the value, and the chronicle holds a value's size and hash, never the value, because the person must be able to take personal data back and the chronicle could never forget it. `docs/projects.md` is the contract. |
| DEC-PREF-002 | The agent host `jason session open` starts is the named preference `agent.interactive_host`, one of the host words, and unset means Claude Code; the choice is the person's own word like every preference, and a host the build does not know, or does not find on the search path, is refused rather than replaced by another. `docs/contracts/preferences/agent.interactive_host.md` is the contract. |
| DEC-HARNESS-001 | What a person tells Jason in their own words is kept in memory files they can read and edit: an `AGENTS.md` for the client, each project and each campaign, in folders nested in that order under a visible harness root — `Harness:Root`, `~/Jason` by default, an absolute path that is never the home directory, a folder holding it or the top of a drive, nor the data directory, a folder inside it or one holding it — each beside a one-line import file for every host that reads a file of another name (`CLAUDE.md` for Claude Code) and a marker, `.jason/marker.json`, naming the level, its id and the format's version and nothing else. The folders are made silently at the first create that needs them, the row first and the folders inside its transaction, a project's folder made for a campaign in a step of its own before it; a project's is named by its slug and a campaign's from its name, numbered only where another campaign has the name; a folder already at a level's path is adopted by its marker when it has none and refused when marked for anything else, never walked around and never deleted; the absolute path is recorded on the entity and a folder is known by its marker, never its name. A folder the person moves, renames or chooses is recorded again only by `project link`, by its marker, reading the project's folder and the folders directly in its `campaigns/` and nothing deeper; a campaign found under another project is reported and never acted on, since the database decides a campaign's project; and the CLI takes a verb's project from the folder it runs in only where that folder is the one recorded. A create whose folder cannot be made is refused whole; a campaign that changes project takes its folder along by one rename in the same request, or is refused with the campaign's folder and every row as they were. The runtime never reads a memory file and the root is never made a repository by it. `docs/projects.md` is the contract. |
| DEC-STATE-001 | Runtime API is the only supported external boundary for authoritative Jason state. |
| DEC-STATE-002 | EF Core over SQLite is authoritative for Jason operational/orchestration state. |
| DEC-STATE-003 | The published Markdown operational-state/workspace model is superseded, with no parallel skills-only file runtime. |
| DEC-STATE-004 | External provider systems remain authoritative for their own detailed entities and effects. |
| DEC-SKILL-001 | Skills own SDR knowledge, role reasoning, tool guidance, and guardrails — not queues, schedules, retries, or state authority. |
| DEC-ROLE-001 | Runtime supports a coordinated/versioned role set operationally; role semantics live in skills. |
| DEC-ROLE-002 | Arbitrary new runtime-visible roles and a public role plug-in contract are deferred. |
| DEC-ROLE-003 | A role's memory is kept at three levels, the client's (its general craft), a project's and a campaign's, one note per role at each, through one verb family that names exactly one level per call; a reader composes them client, then project, then campaign, the lower winning where two differ; a note never carries what belongs to a level below it, so a person or one campaign's state is kept at the campaign's level alone; a note never outranks current state or the person's words (INV-MEM-001); the runtime composes nothing and reads none. `docs/execution-profiles.md` §8 is the contract. |
| DEC-DISP-001 | Dispatcher is deterministic Runtime code, not an AI role. |
| DEC-MGR-001 | Campaign manager is an AI role with isolated logical context and non-authoritative memory, kept per campaign and, above it, per project and for the client (DEC-ROLE-003). |
| DEC-MGR-002 | Management combines reactive triggers with scheduled proactive reconciliation. |
| DEC-WORK-001 | Runtime manages heterogeneous work through a common conceptual envelope without forcing identical payloads/results. |
| DEC-WORK-002 | Predetermined continuations proceed deterministically; semantic continuations become AI-role work. |
| DEC-SYNC-001 | Inbound sync is a deterministic scan step: for each active campaign routed to a plugin that reads conversations, and linked, it creates one read per cadence through the ordinary work-item routine and turns a completed read into `inbound_reply_observed` lines carrying identifiers, moments and the provider's own flags — never a subject, a preview or a name; it interprets nothing, and what a reply means is the manager's. |
| DEC-EVENTS-001 | A provider's events are read through a contract family of their own, `events.read` (`docs/contracts/events/`), by a cursor the plugin returns and the runtime stores per plugin and binding identity and never parses; never as work items. What is read is kept whole in an append-only store — each event once under a key per plugin, an event no pin matches kept concerning nobody — and reaches a campaign by the provider campaign that campaign's operations pinned or, where that finds none, by the contact its members are pinned as at the event's plugin, decided once when it is stored, as a reserved line in the campaign's chronicle: a thread the other side moved as `inbound_reply_observed` under the inbound read's own key, every other kind as `provider_event_observed`. A read pins nothing. A span a person asks for again, at most 366 days back, is read only in a scan where every account is current, by a cursor of its own that never moves the account's, and ends failed on an answer repeating cannot change; a signal from the program that receives a provider's webhooks names the account and nothing else and brings its next read forward, never within a minute of the last; `events.sources` is each account's record and never answers its cursor. Every account a route binds at such a plugin is read by a scan step that hands one read at a time to a reader off the scan — on `Dispatcher:EventsCadenceSeconds`, thirty days deep, a span older than that said once as a gap a notification counts — and the inbound read leaves a campaign to its plugin's account only where an event would reach it by the campaign-pin rule the store attributes by, and only while that account's last read that succeeded started within the shorter of two cadences and the inbound look-back, so a hand-back always reads from no later than that read; the skip rests on a plugin naming, under `campaign`, the provider campaign a moved thread belongs to (`docs/events.md`). |
| DEC-JOURNAL-001 | Every kind the runtime reserves for its own journal lines is a contract: one document per kind under `docs/contracts/journal/`, a sixth family embedded beside the others, stating the release that first wrote the kind, what a line of it records, whether a program may subscribe to it, which things its lines name and where, the release each later field arrived in, and the schema of the line's body — its key, old and new, each always present. A kind arrives with its document, and a guard holds the two lists to each other both ways. Every line of a reserved kind is checked against its document as it is written: refused under test, so a writer that drifts from its document fails the test that drives it, and written in an installation with its kind in the log, because a line the runtime meant to keep is never lost to its own check. A body grows by optional fields only; a field never changes meaning and is never removed, and a kind is never renamed or retired while a release that writes it is supported (`docs/contracts/README.md`, "The journal's kinds"). The runtime answers these documents through `journal.kinds`. |
| DEC-SUBSCRIPTIONS-001 | A plugin granted a journal kind is handed that kind's lines after the runtime wrote them, by a runner of the runtime's own off the scan, under a seventh contract family, `subscription.handle` (`docs/contracts/subscriptions/`), never a work item or a route. The subscribers of one kind form a chain in the order the settings give, each handed the envelope the one before returned, and a kind takes one line at a time in the journal's order from a position kept per kind, read afresh before each line and moved by a guarded update that carries the revision it read, so a rewind during a turn is seen at the next line, never later. A line is handed at least once: a stop writes nothing more and the line is run again. Only `transient` is tried again, after a doubling wait held to an hour that holds no child; every other class ends the chain for that line, said once by a reserved `subscription_failed` line in the same transaction as the position's move, never with the envelope or the failure's words; a member that cannot run makes its kind wait, and is no failure. There are no cascades one step deep: a line a plugin wrote, and a line that names a work item a plugin created, are passed over whoever wrote it; what a role that item runs creates afterwards (new work items, new contacts, its lines on other entities) is the role's work and is handed out as any work is (`docs/plugins.md`, "Writing a subscriber"). There is no hook before a line is written. |
| DEC-AUDIENCE-001 | A campaign's audience is a definition in the provider's own shape, held to the schema its routed plugin declares, and searched by the metered `audience.search`, which a person approves: the first search under an audience always, and the later ones by the campaign's allowance, capped in people. Two scan steps follow the audiences and start nothing — the poll reads each search in flight and lands its people, and the reserve counts the people nobody has started on against the campaign's threshold — so whether to search again is the manager's, never the runtime's. |
| DEC-IDEM-001 | A caller's idempotency key is a property of the entity it created — stored on that row under a unique index, installation-wide for a campaign and campaign-wide for a work item — never a replay cache with a lifetime: the same key with the same request answers the same entity, with a different request a non-retryable conflict; neither the runtime nor the CLI ever derives a key — a caller gives one or there is none. |
| DEC-AI-001 | Jason ships no AI client, model account, model-provider credentials, or mandatory interactive agent. |
| DEC-AI-002 | Interactive AI agents are user-owned external clients that use skills and Jason CLI. |
| DEC-AI-003 | Runtime-launched background AI work requires a supported unattended-capable Agent Host integration and execution profile. |
| DEC-AI-004 | Agent host, model provider, model/effort, and agent execution profile are distinct concepts. |
| DEC-AI-005 | Execution-profile resolution is explicit override → campaign/role policy → causal lineage → global default. |
| DEC-AI-006 | Causal execution lineage can cross deterministic work and must not be inferred magically from a caller process. |
| DEC-EXEC-001 | Runtime has separate deterministic internal, deterministic provider, AI-role, and human-decision execution classes. |
| DEC-EXEC-002 | A host with no flag that carries a role's permissions run by run gets them from a workspace hook: the launch writes a hook file into the attempt's work directory, and its command is the `host-hook` mode of the same executable, a pure function of one tool call and the attempt's rules and one look at what stands at the result path, that answers approved, refused or left to the person's own rules. The rules travel in the hook's command line, never the capability token or the envelope. The first host to take this form is Antigravity CLI; `docs/execution-profiles.md` is the contract. |
| DEC-APPR-001 | An approval's preview is declared by the operation's contract as a template over the composed input and rendered by the runtime at the claim; the plugin never sees it, and a population the preview names is held by the plugin at execution. |
| DEC-NOTIFY-001 | A notification is a pointer (counts and where to look), never a decision surface; made by a deterministic scan step with no work item and no AI in the path; recorded as the reserved journal line `notification_sent` with no campaign; a channel that cannot reach anybody is recorded and skipped, never a failure of the parked item. |
| DEC-DIAG-001 | A diagnostic bundle is shareable by construction. `jason doctor` composes it from the installation's files and the runtime's own metadata: versions, the readiness report, configuration as shape with literal values only from one published allowlist, a bounded log slice under the logging invariant, plugin status and routes — which plugin performs which operation, here and per campaign, never a binding's account or its identity — and journal and work-item metadata without values, with the home directory and the account's names rewritten; journal values travel only on an explicit flag with a printed warning; it reaches no network and is never uploaded; it is listed before it is written. `jason logs` reads the installation's files and asks the runtime nothing; `jason doctor` asks the runtime only through its API and never opens the database; they are the fifth and sixth declared verb-map exceptions. |
| DEC-EXT-001 | AI agents may call external CLI/MCP/API tools directly during semantic work. |
| DEC-EXT-002 | Campaign side effects should use managed canonical operations, but out-of-band effects remain possible. |
| DEC-EXT-003 | Out-of-band side effects may enter Runtime through provenance-bearing post-factum reports that preserve asserted/observed status and uncertainty. |
| DEC-OPS-001 | Runtime defines strict, versioned, vendor-neutral canonical provider operations. |
| DEC-OPS-002 | Runtime does not launch AI merely as a proxy for an already determined provider call. |
| DEC-OPS-003 | Canonical operations are published in subsets chosen by the role sentences they retire and the chain they complete, each copied from the vendor-neutral catalogue with its properties unsoftened; an operation on the catalogue's protective floor runs `auto`, and the claim's own guards do not block it. |
| DEC-MCP-001 | MCP is an AI-agent tool, not the accepted deterministic Runtime transport. |
| DEC-ROUTE-001 | Plugin routing selects the plugin implementation at global and campaign scopes; binding separately selects provider identity/workspace context. |
| DEC-ROUTE-002 | Routing precedence is campaign operation override → campaign default → global operation override → global default; a route that names a channel under an operation sits above that operation's own route at its scope (`docs/routing.md` §2). |
| DEC-BIND-001 | Plugin binding is separate from routing and supplies opaque, non-secret provider identity/workspace context. |
| DEC-BIND-002 | The Reply plugin, not Runtime or `host.exec`, interprets Reply CLI profile semantics. |
| DEC-ADP-001 | Official and community provider plugins use one public JavaScript package model. |
| DEC-ADP-002 | JavaScript is the single selected plugin language and runs through embedded Jint without Node/Python/PowerShell prerequisites. |
| DEC-ADP-003 | Plugins are stateless across invocations and do not own campaign state. |
| DEC-ADP-004 | Plugin JavaScript executes in a separate short-lived process using the same Jason executable in plugin-host mode. |
| DEC-ADP-005 | `host.exec` is an explicitly injected JavaScript-visible bridge to trusted C# structured process execution. |
| DEC-ADP-006 | Runtime and Plugin Host communicate through structured stdin/stdout, diagnostics on stderr, and process status. |
| DEC-ADP-007 | Plugin packages and candidate routes are activated by explicit, validated, atomic Runtime-controlled reload. |
| DEC-PLUGINS-001 | A plugin is one of three kinds: `provider`, which performs canonical operations; `notification`, a delivery channel; and `subscriber`, which implements no operation, is no channel, reads no provider events and handles only the journal kinds its manifest declares under `subscriptions`. A provider or a notification plugin may subscribe too, and routes, claims and runs its work as one that does not. Declaration is not permission: `Plugins:Grants:<id>:Subscriptions` grants the kinds. Which plugins handle a kind, and in what order — those `Subscriptions:Order:<kind>` lists first, the rest by plugin id — is frozen with the plugin snapshot by the same load, and an order naming a plugin not granted the kind rejects that load. |
| DEC-PLUGINS-002 | A plugin is configured under `Plugins:Config:<id>`, a flat map from a key to text in the settings, frozen at each load as grants are and handed to the child on stdin, where `host.config` reads it; no package holds it, so configuring a plugin never changes its digest, and every value is a secret of the invocation. It is a value channel and opens nothing: a host outside what the manifest declares is reached only where the manifest says `capabilities.http.any_granted_host: true` and a person grants that host by name, and `*` still means the declared hosts alone. It is settings, never a named preference. |
| DEC-PLAT-001 | Runtime is implemented in .NET and distributed as platform-specific self-contained builds. |
| DEC-DATA-001 | EF Core is selected; Dapper is excluded; Native AOT is deferred until compatible and low-risk. |
| DEC-INSTALL-001 | Standalone and Reply-assisted installation are both legitimate paths to the same user Runtime. |
| DEC-INSTALL-002 | The Reply plugin, from 0.30.0 on the journal forwarder, and from 0.32.0 on ntfy, Slack and Telegram ship as release assets: every release publishes `jason-plugin-<id>.zip` for each, the files of `plugins/<id>` at the tag at the archive's root, each built once and listed in `checksums.txt`, and named in the manifest's `plugins` with its version, file name, content digest and the digest of every version its list records, never among the manifest's artifacts, which are platforms. Installing one is `jason plugin install <id>`: download, verify against `checksums.txt` and the manifest's content digest, unpack into the plugins directory; a package's content digest is its identity, and both installation paths of §15.5 rest on those assets. |
| DEC-UPDATE-001 | Runtime may advertise updates; a short-lived CLI/installer performs controlled replacement. |
| DEC-UPDATE-002 | The feed, the install lines and the release page name the product's own address, `get.jasonharness.ai`, which redirects `/releases/*` to the same path in the public repository's releases with a temporary redirect; the licence page, links into the tree and the skills source stay on GitHub, and the page `docs/responsible-use.md` never names the address. |
| DEC-UPDATE-003 | An installation is Jason's own unless a package manager's own record says otherwise; a managed installation is updated and taken off by its manager, and Jason's verbs act only on what is Jason's (autostart, skills by receipt, data); and a status check says when the running runtime is not the build installed. |
| DEC-UI-001 | The Jason UI is served by the runtime itself under `/ui/`, from `<data>/ui/custom/` when that holds a build and else `<data>/ui/default/`, as static files under `Content-Security-Policy: default-src 'self'`; it is a client of the Runtime API and nothing else. |
| DEC-UI-002 | A browser is paired once: `ui.open` mints a single-use ticket good for one minute, the page trades it at `POST /ui/session` — the one door outside the operation catalog, and the only POST without a bearer — for a UI token the runtime stores as a hash and accepts as a bearer until `ui.revoke`; UI tokens never expire on their own. A request carrying an `Origin` that is not the runtime's own is refused. |
| DEC-UI-003 | The UI is an optional module, not a plugin: files the runtime serves, not code it invokes; installed by default and updated the way role skills are: a release publishes `jason-ui.zip` in `checksums.txt` (not in the manifest), and `jason ui install` places it as `ui/default/`, run by the install guide, at the end of `jason update apply` and as `jason status`'s repair; never fetched by the runtime; the runtime starts without it and makes no directory for it. The runtime's port defaults to 52766 so a bookmark is stable, moving to the next free port above it when that one is taken. |
| DEC-UI-004 | The UI is one more consumer, and Jason stays command-line first and agent first: everything the UI needs from the runtime is a CLI verb that makes sense typed, returns facts and never views, and leaves the runtime unaware a screen exists — no route named after a page, no state, preference or cache for it. |
| DEC-UI-005 | The UI's screen tests render exchanges the runtime's own tests recorded under `docs/api/fixtures/`, held to the code; its browser suite proves only what a browser alone can — pairing, the policy, the shape of every request; the runtime's logic is proven once, in the runtime's own tests. |
| DEC-UI-006 | On Windows the toast reaches the item that waits through a `jason:` link, Windows' own form for a URI scheme, registered per user (`HKCU\Software\Classes\jason`, nothing elevated) by the toast host beside the AppUserModelID key, and never over a class that is not Jason's: a registration another program holds leaves the toast without a click or a button. The link carries an opaque id of the item and nothing of its content, and the handler is the CLI itself, which makes every child through `ChildProcess` and asks the runtime nothing new. macOS and Linux reach the item through the UI. `docs/notifications.md` and `docs/ui.md` are the contract. |
| DEC-UI-007 | `jason session open` starts the person's own agent host in a terminal of their own in one of three forms: on one item, in the folder of the item's campaign, or the person's home folder when none is recorded, with a brief on that one item; or, with no item, at a campaign's folder, a project's, the harness root or, with `--here`, the folder it runs in; never by falling back to the home folder, running a host or the system's plain shell with no first input, where Claude Code or Codex may continue one of its own conversations by its id; or, with `--setup`, at the installation's role skills folder with the setup brief, which names the skill customising-jason, as its first input; it is the ninth declared exception of the verb map, because the terminal is the desktop's and the runtime never starts one. Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is the host's own permission settings' to decide. A standing approval is the automatic case. `docs/ui.md` is the contract. |
| DEC-INFRA-001 | Docker, server database, message broker, and vendor-specific background services are not required by the local architecture. |
| DEC-DOC-001 | This document describes the complete target vision without releases, milestones, implementation order, or backlog decomposition. |

## 19. Principal invariant register

Future decomposition and technical design must preserve these invariants unless the
architecture is explicitly revised with new evidence.

| ID | Invariant |
|---|---|
| INV-AUTH-001 | Runtime is the sole authority for Jason-owned operational and orchestration state. |
| INV-AUTH-002 | Agents, plugins, CLI clients, UI, and generic tools never mutate Jason SQLite directly. |
| INV-AUTH-003 | External providers remain authoritative for their own state; Jason's local view may be incomplete or uncertain. |
| INV-FILE-001 | Markdown/files never form a competing operational state machine or dual-write Runtime mode. |
| INV-FILE-002 | In the harness root the Runtime writes structure only — a level's directory, its marker, an empty `AGENTS.md`, and a one-line import file for every host that reads a file of another name (`CLAUDE.md` for Claude Code; the host matrix in `docs/execution-profiles.md` §12 lists them), each where none is there. It never reads, writes or deletes a word of any `AGENTS.md`, never writes over an existing import file, never deletes a folder or a file (archiving a campaign leaves its folder), never runs `git` and never writes a `.gitignore`; a rename changes a name, never a folder. Its one move is a campaign's folder, by one rename, when the campaign changes project. Nor does the CLI touch it: `jason uninstall` removes nothing under the root, `--purge-data` included, and `jason doctor` lists and opens nothing in it. |
| INV-API-001 | All supported authoritative Jason reads and mutations pass through Runtime-owned contracts. |
| INV-ROLE-001 | Dispatcher is deterministic; AI roles own semantic judgment. |
| INV-ROLE-002 | Semantic judgment never implicitly grants approval, credentials, or execution authority. |
| INV-MEM-001 | Role memory and the person's memory files are contextual and non-authoritative; current Runtime state wins on conflict. |
| INV-CAMP-001 | Campaign-scoped context, work, a role's campaign-level memory, route/binding overrides, and lineage remain logically isolated; explicitly configured user-level defaults, and a role's notes at the client's and a project's level, are shared by design. |
| INV-TOPO-001 | Exactly one Runtime state owner exists per OS user in the accepted local topology. |
| INV-SAFE-001 | Deterministic guardrails protect Runtime-managed effects based on facts known to Runtime. |
| INV-WORK-001 | Every Runtime-managed execution produces an accountable outcome or durable non-terminal state. |
| INV-CONT-001 | A predefined valid continuation does not require unnecessary AI arbitration. |
| INV-AI-001 | Jason does not own model-provider accounts or credentials. |
| INV-AI-002 | Background AI runs only through an explicitly supported and resolvable user-owned host profile. |
| INV-AI-003 | Missing AI capability blocks AI work, not unrelated deterministic Runtime functionality. |
| INV-AI-004 | Every background AI attempt pins its resolved execution-profile identity/revision, resolution source, and effective host/model options. |
| INV-LINEAGE-001 | Execution-profile lineage is explicit, durable, auditable, and preserved across relevant non-AI causal links. |
| INV-LINEAGE-002 | Lineage conveys execution preference/provenance only — not approvals, permissions, secrets, provider bindings, budgets, or campaign access. |
| INV-LINEAGE-003 | Invalid or conflicting higher-priority profile information blocks visibly; it does not silently fall back. |
| INV-EXEC-001 | Deterministic provider calls do not require AI sessions. |
| INV-EXT-001 | Direct external side effects remain outside Runtime's pre-execution enforcement and completeness guarantees. |
| INV-EXT-002 | Post-factum reports never masquerade as Runtime-executed attempts, retroactive approvals, or proof of truth. |
| INV-EXT-003 | Conflicting reconciliation evidence may update current understanding without erasing original reports and provenance. |
| INV-OPS-001 | Canonical operation identity and semantics are vendor-neutral; vendor commands are implementation details. |
| INV-ROUTE-001 | Routing and provider-identity binding are independent concepts. |
| INV-ROUTE-002 | Missing, invalid, or incompatible routes/bindings fail visibly with no implicit side-effect fallback. |
| INV-ATTEMPT-001 | Managed attempts preserve operation, plugin, route, binding, contract, and correlation provenance without storing secrets. |
| INV-ADP-001 | Provider plugins remain stateless; all permitted invocation context is supplied explicitly. |
| INV-ADP-002 | Official Reply and community plugins use the same public mechanism. |
| INV-ADP-003 | Community plugin JavaScript never executes inside the long-running Runtime process. |
| INV-ADP-004 | `host.exec` uses structured executable/argument invocation rather than arbitrary shell interpolation. |
| INV-ADP-005 | Plugin capability declaration is not permission; Runtime/user policy grants capabilities. |
| INV-ADP-006 | Process/Jint isolation reduces blast radius but is not represented as an OS sandbox. |
| INV-PROC-001 | Every child process the product starts is made and started at one door, `ChildProcess.StartInfo` and `ChildProcess.Start`, on Windows under the start's error mode, with no shell and no console window of its own, held by a guard over the compiled IL of the production assemblies. |
| INV-SECRET-001 | Raw secrets do not belong in ordinary campaign state, routes, manifests, prompts, artifacts, or logs. |
| INV-CONFIG-001 | Candidate files affect active routing/binding only after Runtime-controlled validation and atomic activation. |
| INV-REPLY-001 | Reply is configured as default rather than embedded as mandatory Runtime behavior. |
| INV-LOCAL-001 | Local-first means local Runtime state/control, not offline operation or self-hosting of external providers. |
| INV-DOC-001 | Accepted vision does not imply implemented capability, release membership, or delivery order. |

## 20. Superseded and rejected direction register

### 20.1 Explicitly superseded

| ID | Superseded direction | Current replacement |
|---|---|---|
| SUP-STATE-001 | Goals, plans, work items, schedules, and execution history as authoritative Markdown/YAML files. | SQLite-backed Runtime state accessed through Runtime API. |
| SUP-STATE-002 | Skills or agents operating a parallel file-based durable runtime. | No parallel skills-only Markdown runtime mode. |
| SUP-ORCH-001 | A daemon that scans workspaces and infers readiness/status from files. | A deterministic domain Runtime owns readiness and state transactionally. |
| SUP-OSS-001 | Runtime, contracts, skills, and the public plugin mechanism remain open source; hosted provider capabilities remain external services. | DEC-OSS-003: the runtime, its source and everything else are open source under MIT. |
| DEC-OSS-002 | Contracts, skills, the public plugin mechanism, documentation and the install path open (MIT) and published in `reply-team/jason-ai`, with the runtime released there as binaries without its source. | DEC-OSS-003: the runtime's source is open too, and developed in the same repository. |
| REJ-PLAT-001 | Rejected: Go, TypeScript/Node, Rust, Java, or Python as the selected core Runtime platform, since none outweighed .NET's product fit and existing team leverage. | The runtime is being rewritten in TypeScript in this repository, which supersedes this rejection of TypeScript/Node and the Rust port before it; until the rewrite is complete, the C# runtime under `dotnet/` is the one released (DEC-PLAT-001, 15.2). |

### 20.2 Rejected under the accepted architecture

| ID | Rejected direction | Reason |
|---|---|---|
| REJ-ORCH-001 | Skills/LLM as the orchestration correctness boundary. | Probabilistic, expensive, hard to test, unsafe for unattended lifecycle control. |
| REJ-ORCH-002 | A trivial file-scanning daemon as the final Runtime. | Lacks transactional state, lifecycle authority, recovery, and domain invariants. |
| REJ-ROLE-001 | Dispatcher as an AI role. | Readiness and lifecycle mechanics must be deterministic. |
| REJ-ROLE-002 | Hard-coded SDR role reasoning inside Runtime code. | Semantic knowledge belongs in skills and AI roles. |
| REJ-STATE-001 | Direct AI, plugin, CLI, UI, or MCP mutation of SQLite. | Violates validation, invariants, transactions, and API authority. |
| REJ-CONT-001 | AI review after every successful deterministic continuation. | Adds cost and nondeterminism without semantic value. |
| REJ-REPLY-001 | Hard-coded Reply operations or implicit Reply fallback in Runtime. | Violates provider neutrality and can create unintended side effects. |
| REJ-EXEC-001 | An AI session as proxy for a fully determined provider call. | Adds latency, cost, and uncertainty without requiring reasoning. |
| REJ-OPS-001 | Requiring existing vendor CLIs to copy Jason command names and schemas. | Unrealistic integration barrier; translation belongs in plugins. |
| REJ-ADP-001 | Raw shell templates as the complete plugin layer. | Unsafe and insufficient for complex transformation and normalized errors. |
| REJ-MCP-001 | Generic deterministic Runtime execution through MCP. | Introduces client/session/server/auth lifecycle not justified by the deterministic path. |
| REJ-SVC-001 | Vendor-specific long-running provider services and gRPC topology. | Adds installation, health, update, version-skew, and supervision burden. |
| REJ-INFRA-001 | Docker/Compose as a prerequisite. | Conflicts with lightweight local cross-platform onboarding. |
| REJ-ADP-002 | Community .NET DLLs loaded into Runtime. | Tight coupling, dependency conflicts, crash/security expansion. |
| REJ-ADP-003 | Community JavaScript executed in the long-running Runtime process. | Weak isolation and unacceptable authoritative-process risk. |
| REJ-ADP-004 | A second independently published self-contained Plugin Host binary. | Duplicates platform runtime and lifecycle without architectural value. |
| REJ-ADP-005 | Multiple plugin languages or external interpreter prerequisites. | Multiplies packaging, security, testing, and platform behavior. |
| REJ-ADP-006 | One script file per canonical operation. | Prevents coherent multi-operation packages and shared mapping code. |
| REJ-ADP-007 | Mutable durable plugin-local state. | Conflicts with reproducibility, recovery, isolation, and Runtime authority. |
| REJ-ADP-008 | Direct plugin access to CLR internals or Runtime SQLite. | Breaks isolation and state authority. |
| REJ-ADP-009 | Automatic filesystem watchers as the required discovery mechanism. | Cross-platform lifecycle complexity without need; explicit reload is sufficient. |
| REJ-DB-001 | Dapper as the selected persistence alternative. | Gives up EF integration and migrations for insufficient benefits. |

### 20.3 Deferred rather than rejected

Not active decisions, but not permanently prohibited: Native AOT once EF Core and
dependencies support it safely; PostgreSQL or another server database under future
remote/concurrent requirements; `host.http` with an explicit network and credential security
model; WebAssembly or compiled out-of-process plugin models; npm/dependency ecosystems for
plugins; warm Plugin Host pools based on measured need; arbitrary runtime-visible role
plug-ins based on demonstrated extension use cases; managed, multi-user, remote, or
multi-machine Runtime variants under a different topology.

## 21. Deferred design register

Deferred items are intentionally unresolved design. They are listed so that unresolved areas
are visible — none of them should be read as settled or implemented.

| ID | Unresolved design area | Fixed surrounding constraints |
|---|---|---|
| DEF-DOMAIN-001 | Final public entity names, aggregates, relationships, and domain boundaries. | Runtime remains a substantive SDR domain kernel; business campaign and work unit are provisional terms. |
| DEF-DATA-001 | SQLite tables, EF Core mappings, indexes, constraints, and transaction boundaries. | SQLite is authoritative; all access remains behind Runtime API. |
| DEF-DATA-002 | Migrations, backup, corruption recovery, and database-aware rollback. | EF Core and a controlled external update process remain selected. |
| DEF-FILE-001 | Exact storage of artifacts, exports, snapshots, and contextual memory. | Files cannot become authoritative operational state. |
| DEF-WORK-001 | Exact work/run/attempt/result/event/artifact state machines. | Heterogeneous work, accountable outcomes, deterministic authority, semantic separation. |
| DEF-WORK-002 | Dependencies, readiness, claims, leases, heartbeats, concurrency, fairness, starvation. | Runtime prevents duplicate managed execution and survives restarts. |
| DEF-WORK-003 | Machine sleep, missed schedules, recurrence, cancellation, checkpoints, drain, forced termination. | Runtime is long-lived and updates through controlled drain/stop. |
| DEF-RECOVERY-001 | Retry counts, backoff, ambiguity recovery, orphan handling, continuation checkpoints. | No blind replay of side effects; canonical failure classes and provenance required. |
| DEF-APPROVAL-001 | Exact approval classes, payload binding, anomaly stop rules. | Runtime mechanically enforces deterministic approval boundaries for managed effects. |
| DEF-ROLE-001 | Final built-in role catalog, operational metadata, permissions, coordinated skill versions. | Runtime supports roles operationally; semantic behavior remains in skills. |
| DEF-HOST-001 | Initially supported agent hosts and concrete host-integration interfaces. | Jason owns no AI client/credentials; background work uses user-installed unattended-capable hosts. |
| DEF-HOST-002 | Agent invocation, context hydration, progress, structured results, session continuation, cancellation, resume. | Runtime validates outcomes; agent sessions never access SQLite directly. |
| DEF-PROFILE-001 | Execution-profile schema, discovery, capability validation, configuration UX. | Host/provider/model/profile remain distinct; credentials stay with the host. |
| DEF-LINEAGE-001 | Exact causal-lineage algorithm, branch selection, propagation, ambiguity handling. | Priority order and no-silent-fallback invariants remain fixed. |
| DEF-API-001 | Runtime API transport, local authentication, caller identity, discovery, versioning, events. | Runtime API remains the only supported authoritative state boundary. |
| DEF-CLI-001 | Exact Jason CLI noun hierarchy, commands, JSON shapes, exit codes, stdin/files, previews, repair UX. | CLI remains a complete administrative and business Runtime client designed for agents. |
| DEF-UI-001 | UI information architecture, observability views, controls, localhost hosting. | UI remains a client of Runtime API with no duplicate business logic. |
| DEF-EXT-001 | Post-factum report schema, minimum evidence, confidence/uncertainty, conflict handling, provenance. | Reports never become retroactive managed attempts, approvals, or proof of truth. |
| DEF-RECON-001 | Deterministic and AI-assisted provider reconciliation mechanisms. | External reality may diverge; original reports/provenance remain auditable. |
| DEF-OPS-001 | Canonical operation subset, operation schemas, validation, normalized outcomes, conformance. | Operations remain strict, versioned, vendor-neutral. |
| DEF-OPS-002 | Contract compatibility windows, deprecation, plugin migration. | Runtime checks compatibility and fails before execution. |
| DEF-ROUTE-001 | Routing/binding configuration schema, operation-family patterns, binding reuse, validation handshake. | Routing and binding remain distinct; accepted precedence and fail-closed behavior remain. |
| DEF-ROUTE-002 | Behavior of queued work and retries after route, binding, or plugin reload. | Running attempts retain pinned immutable provenance. |
| DEF-EVENTS-001 | The transport of a listener's signal to read an account earlier — a tunnel, a polled listener — and whether a signal may stretch the cadence. | A signal carries no data and only brings a read forward; a webhook is never the carrier of an event, because a machine that is off loses it. |
| DEF-EVENTS-003 | A pointer for an account whose events keep failing to be read. | A notification is a pointer and the family exists (`docs/notifications.md` §1). A source whose reads keep failing is not yet among what it counts. Until it is, it shows in its own record (`jason events sources`) and the log, and the inbound read reads a campaign whose plugin's source is not healthy. |
| DEF-EVENTS-004 | A Reply mode of the events contract over its inbox's offset paging, before Reply has an account-level feed by cursor. | Reply's plugin declares the events contract only once that feed exists; until then the inbound read reads Reply. |
| DEF-ADP-001 | Final manifest schema, standard-stream envelope, Host SDK types, error model, content identity. | JavaScript/Jint, stateless child execution, structured streams, Runtime revalidation. |
| DEF-ADP-002 | Direct HTTP capability and its destination, credential, retry, redaction policy. | `host.exec` is the selected minimum; unrestricted network access is not assumed. |
| DEF-ADP-003 | Plugin installation, update, removal, signing, publisher identity, warnings, marketplace/discovery. | Plugin packages are executable local code; official/community mechanism remains shared. |
| DEF-ADP-004 | OS-level sandboxing, resource limits, executable/environment allowlists, log redaction. | Jint/process isolation is not represented as a complete sandbox. |
| DEF-DIST-001 | Exact supported OS/architecture matrix, a stapled macOS installer package, a Windows installer package, installer behavior. | Platform-specific self-contained builds and GitHub release assets remain selected; the macOS executable is signed with a Developer ID and notarized, and the Windows executable through Artifact Signing. |
| DEF-UPDATE-001 | Staging layout, drain timeout, backup format, migration health checks, rollback, repair. | Runtime does not self-replace; a short-lived control process owns updates. |
| DEF-PRODUCT-001 | Golden path, onboarding narrative, product success criteria, demonstration flow. | The complete architecture remains independent of delivery order. |
| DEF-PLAN-001 | Backlog decomposition, implementation sequence, technical spikes, testing strategy, delivery governance. | This vision is the source input and contains no roadmap. |

## 22. Assumptions and known risks

### 22.1 Assumptions

| ID | Assumption | Consequence if false |
|---|---|---|
| ASM-LOCAL-001 | Expected local write concurrency fits a single-user SQLite-backed Runtime. | Persistence and topology may require redesign, not just a connection-string change. |
| ASM-HOST-001 | At least some widely used user-owned AI hosts expose reliable unattended/background invocation. | Background AI automation becomes host-limited or requires a different integration boundary. |
| ASM-CLI-001 | Reply CLI can expose sufficiently structured, stable machine behavior for the official plugin. | The public Host SDK or Reply execution surface must expand without creating a private backdoor. |
| ASM-JS-001 | Jint supports the JavaScript subset and constrained interop needed for practical plugin mappings. | Another out-of-process plugin execution technology may be required. |
| ASM-PROC-001 | Per-invocation child-process startup is insignificant relative to provider/network latency. | Pooling or another worker model may be justified by measurement. |
| ASM-AGENT-001 | Users adopting Jason generally already possess or will configure an AI agent environment. | Deterministic software remains usable, but the agent-first value proposition weakens. |
| ASM-REPLY-001 | Reply API/CLI covers enough practical SDR operations to make the official plugin valuable. | The golden path needs narrower claims or additional provider capability. |
| ASM-OSS-001 | Extensibility by others — plugins and skills written against the published mechanism — is valuable even if custom plugin creation remains an advanced workflow. | The plugin ecosystem may remain primarily Reply-owned, reducing practical vendor-neutral value. |

### 22.2 Known risks and tensions

| ID | Risk or tension | Architectural response |
|---|---|---|
| RISK-EXT-001 | Direct external side effects can make Runtime state stale and produce duplicate work. | Prefer managed operations; instruct prompt post-factum reporting; add reconciliation and visible uncertainty. |
| RISK-AUTH-001 | Skills instruct well but cannot prevent a user-owned agent from bypassing Runtime. | Treat skills as guidance, never as a security boundary; scope guarantees to managed execution. |
| RISK-LINEAGE-001 | Branching AI history can make inherited execution profiles ambiguous. | Preserve explicit provenance, block unresolved conflict, design a deterministic lineage policy. |
| RISK-BIND-001 | A retry under a different provider profile/account could affect the wrong external workspace. | Pin plugin, route, binding identity/revision, operation, and correlation provenance. |
| RISK-ADP-001 | Community plugin code runs with the local user's OS identity. | Restrict the Host SDK, isolate the process, validate capabilities, design trust/signing/warnings. |
| RISK-PKG-001 | Plugin ID/version can lie about mutable package contents. | Add immutable content identity or snapshot semantics before claiming reproducibility. |
| RISK-DOC-001 | Previously published skills and documentation still describe the superseded file-state model. | Alignment work updates skills and documentation from this architecture. |
| RISK-PROD-001 | The complete vision can be mistaken for implemented capability. | Keep implementation status and roadmap separate; label this document's status explicitly. |
| RISK-UI-001 | A UI can accidentally accumulate duplicate business logic. | Treat Runtime API as the only behavioral core and test clients against it. |
| RISK-LOCAL-001 | Laptop loss or local corruption can destroy state without user backup. | Local ownership is explicit; backup/sync responsibility remains outside the current product promise until separately designed. |
| RISK-UPDATE-001 | Binary rollback and database rollback may diverge after migrations. | Require database-aware update/recovery design. |
| RISK-NEUTRAL-001 | Reply's zero-effort default may become de facto hard-coding. | Keep default registration/configuration separate from Runtime logic and dogfood the public plugin mechanism. |

## 23. Glossary

| Term | Meaning in this document | Must not be confused with |
|---|---|---|
| Jason AI | The complete local-first product direction described here: open source under the MIT licence and distributed as self-contained binaries with its skills, contracts, plugins and install path. | Reply's hosted Jason AI SDR product or a self-hosted Reply clone. |
| Jason Runtime | The one long-running user-scoped deterministic domain process. | Dispatcher alone, Plugin Host, agent host, or a Reply service. |
| Runtime API | The supported authoritative boundary around Jason state and behavior. | SQLite, CLI command syntax, or a provider API. |
| Jason CLI | Short-lived administrative and business client of Runtime API. | Reply CLI. |
| Reply CLI | Provider-specific CLI owning Reply authentication, profiles, and API access. | Jason CLI or Runtime. |
| Business campaign | Working term for an ongoing Jason business initiative and its durable context. | A Reply campaign/sequence, a customer company, or final public API naming. |
| Work unit | Working term for the generic durable envelope of heterogeneous work. | A provider task, sequence step, OS process, or final entity name. |
| Dispatcher | Deterministic Runtime logic for readiness, routing, lifecycle, and triggers. | Campaign manager or another AI role. |
| AI role | A semantic responsibility implemented through skills and a user-owned agent session. | A permanent process or authoritative state owner. |
| Campaign manager | Campaign-scoped AI role providing semantic oversight and replanning. | Dispatcher. |
| Agent host | Concrete agent executable/session environment that may be launched for background work. | Model provider, provider plugin, or Plugin Host. |
| Model provider | Service supplying AI models. | An agent host or a provider of SDR execution. |
| Agent execution profile | Non-secret Runtime configuration selecting a launchable agent host and defaults/options. | Reply CLI profile, credentials, role memory, or plugin binding. |
| Execution lineage | Causal provenance used to preserve AI execution preference across work branches. | Agent-session continuation, approval inheritance, or provider binding. |
| Provider plugin | Vendor-specific JavaScript implementation of canonical SDR operations. | Agent Host integration or the Plugin Host process. |
| Plugin Host | Short-lived Jason child-process mode that runs one JavaScript plugin invocation. | An agent host or a file named `host.exe`. |
| `host.exec` | JavaScript-visible C# Host SDK method that safely starts a vendor executable. | `host.exe`, Node `exec`, or shell interpolation. |
| Canonical operation | Strict vendor-neutral deterministic business-operation contract. | A Reply CLI command or Reply API endpoint. |
| Plugin routing | Selection of which plugin implements a canonical operation. | Plugin binding. |
| Plugin binding | Opaque, non-secret selection/configuration for the provider identity/workspace used by a plugin. | Credentials, routing, or campaign memory. |
| Managed execution | Runtime owns an operation attempt before its external effect. | A direct external side effect or a post-factum report. |
| External-effect report | Provenance-bearing assertion/observation about an effect already performed outside Runtime. | Retroactive approval, a managed attempt, or proof of truth. |
| Role memory | Non-authoritative contextual continuity for an AI role, kept at one of three levels: the client's, a project's or a campaign's. | Runtime facts or current provider state. |
| Artifact | A document or output referenced by Runtime or roles. | Automatically authoritative operational state. |

Use qualified terms where **provider** would otherwise be ambiguous: **SDR execution
provider** versus **AI model provider**.

## 24. Closing synthesis

Jason AI is designed to make a user's existing AI agents operationally durable
without claiming ownership of those agents or hard-coding one execution vendor.

Runtime provides the deterministic local domain, state authority, lifecycle, safety,
observability, and recovery. Skills give user-owned agents the SDR intelligence and role
behavior needed to make semantic decisions. Agent Host integrations allow selected
user-owned environments to perform background AI work. Canonical operations and JavaScript
plugins execute known provider effects through a vendor-neutral boundary, with Reply
configured as the supported default commercial path. Jason CLI and UI expose one Runtime
contract to agents and humans.

The architecture deliberately acknowledges imperfect boundaries: users can act outside
Runtime, providers own external truth, local state can be lost without backup, community
plugins are executable code, and agent-host capability varies. It responds with provenance,
post-factum reporting, reconciliation, fail-closed routing, explicit execution lineage,
constrained plugin hosting, and honest scope rather than pretending these realities do not
exist.

The result is not merely a daemon, a prompt collection, or a thin wrapper around one vendor.
It is the intended high-level architecture of a local, open, durable SDR system whose
deterministic mechanics and semantic intelligence remain clearly separated while working as
one product.
