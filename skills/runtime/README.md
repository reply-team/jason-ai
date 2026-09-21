# Runtime skills

Skills that teach an AI agent how to operate Jason itself: turning an objective into durable campaign work,
presenting what is waiting on a person, running the installation, reading why something stopped, and telling
Jason about effects produced outside it. They move in lockstep with the runtime's API and **ship with it**:
this pack ships with Jason **0.1.0**.

Every command any of these skills prints is handed to this build's own argument router by a test on every run,
so a skill cannot drift from the program it ships beside.

## The skills a person's own session reads

One per moment somebody asks for something, because a host chooses a skill by its description and a
description that promises everything matches nothing.

- [`managed-campaign-work`](managed-campaign-work/SKILL.md) — **verified.** Turning an objective into durable
  campaign work and following it: the provider check, the campaign, the people it is about, provider
  operations and AI role work, and what the runtime does with them afterwards.
- [`approvals-and-questions`](approvals-and-questions/SKILL.md) — **verified.** What Jason is waiting for a
  person to decide: the approvals parked at a claim and the questions a running role raised, presented in the
  person's own words and recorded as their decision.
- [`operating-the-installation`](operating-the-installation/SKILL.md) — **verified.** The installation itself:
  whether the runtime is up, starting and stopping it, having it start at logon, the plugins and routes it
  has, the hosts it can launch, and whether a newer version exists.
- [`troubleshooting-jason`](troubleshooting-jason/SKILL.md) — **verified.** Why work stopped: the runtime, the
  work item's error code and the chronicle; the states a person needs told apart, and the repairs that are
  safe.
- [`reporting-outside-effects`](reporting-outside-effects/SKILL.md) — **verified.** Telling Jason about an effect
  produced somewhere else, before it acts on a view of the world that is wrong.

### Installing them

```sh
jason skills install --dry-run --pack jason-runtime-skills
jason skills install --pack jason-runtime-skills
```

That deploys the five interactive skills into the agent harnesses it finds, and the nine role skills below
into the runtime's own data directory, where the runtime reads them at every launch. The second destination is
not optional: a runtime with no role skills launches every role untaught.
[`skills/README.md`](../README.md) is the contract the verb keeps.

A person without this build installs an interactive skill by copying its directory into wherever their own
agent host looks for skills — for Claude Code that is `~/.claude/skills/<name>/` for themselves or
`.claude/skills/<name>/` inside a project, as its own documentation describes. Other hosts have their own
location. The directory name is the skill's name, and the two must match: a skill whose front matter names
anything else is loaded by nobody and reported by nobody.

## The skills a launched role reads

`roles/<role>/` holds one skill per role, named after its directory because that is how a host looks one up.
All **nine roles this runtime seeds** are taught here, and every one of them carries the same launch contract
— what it may type, what its note is worth, what shape its answer takes — held identical by a test. What
differs between them is one paragraph of purpose and one of what this build can and cannot give that role.

- [`roles/researcher`](roles/researcher/SKILL.md) — **verified.** The brief a role is given when the runtime
  launches it: how to call home in the one command form that is permitted, how to read and keep its campaign
  note, and how to answer in the shape the work item asked for.
- [`roles/manager`](roles/manager/SKILL.md) — **draft**, because it was re-authored onto the shared launch
  contract after the run that last read it, and no host has read the text that ships. The review a manager
  is launched for: why it was
  woken, what it reads and in which order, what it may decide alone, how it asks a person the one question it
  cannot answer, and the line it leaves in the chronicle every time.
- [`roles/planner`](roles/planner/SKILL.md) — **draft.** The next short horizon of work: what a planner reads
  before it plans, what this build stores of a plan and what it does not, and how a horizon becomes work items
  a dispatcher will claim.
- [`roles/copywriter`](roles/copywriter/SKILL.md) — **draft.** The campaign-level messaging: what the campaign
  itself says about voice and guardrails, where the copy goes when it is written, and why writing it is not
  sending it.
- [`roles/personalizer`](roles/personalizer/SKILL.md) — **draft.** The messaging adapted to one person: what
  this runtime knows about them and where to read it, where the wording goes afterwards, and why a contact
  record is not the place for it.
- [`roles/critic`](roles/critic/SKILL.md) — **draft.** The review of a draft against the brief and the
  guardrails: what to read before judging it, what a verdict is worth here, and why saying plainly what fails
  is the whole of the job.
- [`roles/responder`](roles/responder/SKILL.md) — **draft.** What a reply means and what should happen
  next: the reply is in the brief or it is nowhere, and the answer is a reading and a draft rather than
  anything sent.
- [`roles/analyst`](roles/analyst/SKILL.md) — **draft.** What is working in a campaign, read off the three
  things this runtime keeps — the work items, the chronicle and the reports — and honest about where the
  record runs out.
- [`roles/deliverability-specialist`](roles/deliverability-specialist/SKILL.md) — **draft.** What the
  do-not-contact register and the failed items say about whether anything is arriving, and what somebody
  would have to do outside Jason about it.

**Which of them a host has read.** The words mean here what they mean for the skills above, and a `verified`
row carries the same three things: which host read the text, which Jason build it read it against, and the
SHA-256 of the text it read.

| role | status | host that read it | Jason | the text it read |
|---|---|---|---|---|
| `manager` | draft (re-authored onto the contract since its last reading) | — | — | — |
| `planner` | draft | — | — | — |
| `researcher` | verified | Claude Code 2.1.278 | 0.1.0 | `sha256:f7415241899c4ab082f21e0a0bc03036c96fec94da388f9b1b492a6cb2dcc389` |
| `copywriter` | draft | — | — | — |
| `personalizer` | draft | — | — | — |
| `critic` | draft | — | — | — |
| `responder` | draft | — | — | — |
| `analyst` | draft | — | — | — |
| `deliverability-specialist` | draft | — | — | — |

These are not read from here at run time. An operator composes the directory the runtime hands out,
`<data>/skills/roles/<role>/`, from this pack and — later — from the business pack, and the runtime copies it
into each attempt's own work directory. Composing rather than reading from two places keeps one question
answerable: what exactly was this role taught on this attempt.

**What each pack contributes to that one directory.** This pack contributes `SKILL.md` — the runtime's launch
contract and the role's purpose. The business pack will contribute `METHOD.md` beside it, the profession's
method for that role. They are separate files so that installing one never overwrites the other, and a role
with no `METHOD.md` is taught the contract and nothing else, which is complete on its own.

## What `verified` means

Every skill here carries `metadata.status`, and it has exactly two values.

- **`draft`** — no real agent host has read this text in the state it ships in. Its commands still parse
  against this build, because every skill's do.
- **`verified`** — a real agent host read *this* text and did the job it teaches. The row below records which
  host, which Jason build, and the SHA-256 of the text that was read, so that an edit without a new reading is
  a failing test rather than a claim nobody can check.

| skill | host that read it | Jason | the text it read |
|---|---|---|---|
| `managed-campaign-work` | Claude Code 2.1.278 | 0.1.0 | `sha256:01adfb08dec48564d6fef17639fd58cdac16deb7ddfbfea50ebcaa673989d855` |
| `approvals-and-questions` | Claude Code 2.1.278 | 0.1.0 | `sha256:7ec45ac3fc2b72a01a289c27a3b4b59f9b10fb9c9f56abd6a45148f761bb8a74` |
| `operating-the-installation` | Claude Code 2.1.278 | 0.1.0 | `sha256:47ac8082ae5a16fd7567adf11e7d698a3e0447eada201c9f35451845863496ef` |
| `troubleshooting-jason` | Claude Code 2.1.278 | 0.1.0 | `sha256:fac60f6d04618c2d4d614592024de133144c563b8462cc9c4ca751786cf77a9f` |
| `reporting-outside-effects` | Claude Code 2.1.278 | 0.1.0 | `sha256:9fb55ca5364ec65c9b629df5538ff26d7ce380e8d09017989e0d222fca4eb037` |
