# Runtime skills

Skills that teach an AI agent how to operate Jason itself: turning an objective into durable campaign work,
presenting what is waiting on a person, running the installation, reading why something stopped, and telling
Jason about effects produced outside it. They move in lockstep with the runtime's API and **ship with it**:
this pack ships with Jason **0.17.0**.

Every command any of these skills prints is handed to this build's own argument router by a test on every run,
so a skill cannot drift from the program it ships beside.

**What they do not teach yet.** None of the seven mentions `jason skills install`; `jason skills update` and
`jason status` are mentioned only by `operating-the-installation`, and `jason logs` and `jason doctor` are
taught by `troubleshooting-jason` only; both of those skills are drafts until a host reads them. `verified` in
this repository means a named agent host read *that exact text* — its description and its body — while driving
this runtime and the digest of what it read is recorded, so a verified text cannot gain the new verbs without
being re-read by a host — which is why the ones verified before 0.2.0 still teach what this build did before those
verbs existed, correctly and incompletely.

## The skills a person's own session reads

One per moment somebody asks for something, because a host chooses a skill by its description and a
description that promises everything matches nothing.

- [`managed-campaign-work`](managed-campaign-work/SKILL.md) — **draft** until a host reads its new text.
  Turning an objective into durable campaign work and following it: the provider check, what the person has
  already written down, the campaign, the people it is about, provider operations and AI role work, what the
  runtime does with them afterwards, answering a conversation, and stopping or holding work.
- [`approvals-and-questions`](approvals-and-questions/SKILL.md) — **verified.** What Jason is waiting for a
  person to decide: the approvals parked at a claim and the questions a running role raised — including when a
  notification brought the person — presented in their own words and recorded as their decision.
- [`operating-the-installation`](operating-the-installation/SKILL.md) — **draft** until a host reads its new text.
  The installation itself: whether the runtime is up, starting and stopping it, having it start at logon, opening the browser UI and
  unpairing a browser (`jason ui`), the plugins and routes it has, the accounts whose events it reads and reading a
  span of one again, the hosts it can launch, whether notifications reach the person and turning them off, whether a
  newer version exists, and installing it or taking it off again.
- [`troubleshooting-jason`](troubleshooting-jason/SKILL.md) — **draft** until a host reads its new text. Why
  work stopped: the runtime, the work item's error code and the chronicle; the states a person needs told
  apart, the repairs that are safe, and the diagnostic file `jason doctor` writes.
- [`reporting-outside-effects`](reporting-outside-effects/SKILL.md) — **verified.** Telling Jason about an effect
  produced somewhere else, before it acts on a view of the world that is wrong.
- [`planner-in-session`](planner-in-session/SKILL.md) — **draft.** The planner's judgement in a person's own
  conversation about a campaign: its first words on where the person's files live, an opening in the order a real
  start needs, with who writes and how the company sounds set as the person's word, taking a brief as it comes and
  asking for what is missing one question at a time, what stays a quick look, what is agreed with the person before
  anything is created, among it a campaign named for what it is for, under a project named from what they sell, when it
  becomes campaign work, and the first message seen before anything is sent. The session file of the planner, below.
- [`researcher-in-session`](researcher-in-session/SKILL.md) — **draft.** The researcher's judgement in a person's own
  conversation about an account or a person: taking the question as it comes, what stays a quick look and what
  becomes research work items, what Jason already holds and the launched researcher's notes read as a lead, the
  method left to `sdr-operations`, and an answer that says what was established and what was not. The session file
  of the researcher, below.

### Installing them

```sh
jason skills install --dry-run --pack jason-runtime-skills
jason skills install --pack jason-runtime-skills
jason skills update --pack jason-runtime-skills
```

That deploys the seven interactive skills into the agent harnesses it finds, and the nine role skills below
into the runtime's own data directory, where the runtime reads them at every launch. The second destination is
not optional: a runtime with no role skills launches every role untaught — which is why it can be deployed on
its own, into nothing but the runtime's directory:

```sh
jason skills install --roles-only
```
[`skills/README.md`](../README.md) is the contract the verb keeps.

A person without this build installs an interactive skill by copying its directory into wherever their own
agent host looks for skills — for Claude Code that is `~/.claude/skills/<name>/` for themselves or
`.claude/skills/<name>/` inside a project, as its own documentation describes. Other hosts have their own
location. The directory name is the skill's name, and the two must match: a skill whose front matter names
anything else is loaded by nobody and reported by nobody.

## The skills a launched role reads

`roles/<role>/` holds one skill per role, named after its directory because that is how a host looks one up.
All **nine roles this runtime seeds** are taught here, and every one of them carries the same launch contract
— what it may type, what it reads first and in which order, what its notes are worth, what shape its answer
takes — held identical by a test. What
differs between them is one paragraph of purpose and one of what this build can and cannot give that role — and,
for a role that sits with a person, the judgement it shares with its session file (below).

- [`roles/researcher`](roles/researcher/SKILL.md) — **draft.** What a brief asks about an account or a person,
  found and verified: what counts as established, why anything from outside Jason is the host's doing and never
  the runtime's promise, an answer that says what was established and what was not, and what belongs in its note.
- [`roles/manager`](roles/manager/SKILL.md) — **draft**, because it was re-authored onto the shared launch
  contract after the run that last read it, and no host has read the text that ships. The review a manager
  is launched for: why it was
  woken, what a noticed reply or provider-event line asks of it and whose thread it is, what it reads and in which
  order, what it may decide alone, how it asks a person the one question it cannot answer, and the line it leaves in
  the chronicle every time.
- [`roles/planner`](roles/planner/SKILL.md) — **draft.** The next short horizon of work: what a planner reads
  before it plans, the order a first campaign opens in, what this build stores of a plan and what it does not, and
  how a horizon becomes work items a dispatcher will claim.
- [`roles/copywriter`](roles/copywriter/SKILL.md) — **draft.** The campaign-level messaging: what the campaign
  itself says about voice and guardrails, the tone and the sender it writes in, where the copy goes when it is
  written, and why writing it is not sending it.
- [`roles/personalizer`](roles/personalizer/SKILL.md) — **draft.** The messaging adapted to one person: what
  this runtime knows about them and where to read it, the tone and the sender's forms it writes in, where the
  wording goes afterwards, and why a contact record is not the place for it.
- [`roles/critic`](roles/critic/SKILL.md) — **draft.** The review of a draft against the brief and the
  guardrails: what to read before judging it, what a verdict is worth here, and why saying plainly what fails
  is the whole of the job.
- [`roles/responder`](roles/responder/SKILL.md) — **draft.** What a reply means and what should happen
  next: the reply is in the brief, in the result of a provider operation that read it, or nowhere; the answer
  goes into the person's own thread as a reply a person approves, one approval for each send, and the thread is
  filed only under a category the campaign names as its own.
- [`roles/analyst`](roles/analyst/SKILL.md) — **draft.** What is working in a campaign, read off the four things
  this runtime keeps — the work items, the chronicle, the reports and the campaign's activity — and honest about
  where the record runs out.
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
| `researcher` | draft | — | — | — |
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

## A role that sits with a person

Some of a role's work happens in a person's own conversation rather than on a work item: somebody who brings a
campaign objective wants the planner's judgement while they talk, not after a launch. A role that sits with a
person has two files. **The launched role's file**, `roles/<role>/SKILL.md`, is the one the runtime hands to the
role it launches: it answers in the shape a work item asks for and carries the launch contract. **The session
file**, `<role>-in-session/SKILL.md`, is one of the skills a person's own session reads: it talks with the person,
carries no launch contract, and hands the commit to `managed-campaign-work`. Today that is the planner,
[`roles/planner`](roles/planner/SKILL.md) and [`planner-in-session`](planner-in-session/SKILL.md), and the
researcher, [`roles/researcher`](roles/researcher/SKILL.md) and [`researcher-in-session`](researcher-in-session/SKILL.md).
The manager has none: it organises the other roles' work on a check-in, and a person keeping a campaign moving does
that through `managed-campaign-work`, `approvals-and-questions` and `troubleshooting-jason`.

The two share one block, the role's judgement, between `<!-- craft:begin -->` and `<!-- craft:end -->`, and a test
holds it to the same bytes in both; everything outside it belongs to one file. The block prints no command, because
how the work reaches the role and how its answer leaves are exactly what differ between the two, and each file says
its own status outside it. An edit to the block changes the text of both files, so both are drafts again until a
host has read each of them.

**The block is judgement, never a record of results.** What happened on a campaign is in Jason, where every role
can read it. What a role learns of its craft on one campaign and carries to the next is its own note at the
client's level, or at a project's for that project's campaigns: its working memory, written by the role for itself
in its own words. Learned practice, which this version does not have, would be the installation's own knowledge,
distilled from what happened across campaigns and written by no role for itself. A block that began to collect
either would teach every campaign what one campaign happened to find.

## What `verified` means

Every skill here carries `metadata.status`, and it has exactly two values.

- **`draft`** — no real agent host has read this text in the state it ships in. Its commands still parse
  against this build, because every skill's do.
- **`verified`** — a real agent host read *this* text and did the job it teaches. The row below records which
  host, which Jason build, and the SHA-256 of the text that was read — the description a host chose it by and
  the body it followed — so that an edit to either without a new reading is a failing test rather than a claim
  nobody can check.

| skill | host that read it | Jason | the text it read |
|---|---|---|---|
| `approvals-and-questions` | Claude Code 2.1.293 (claude-opus-5) | 0.5.0 | `sha256:b0e638d1d1674a3429ce58cc52fd23b134780222a30de79a04bc011ec49c8342` |
| `reporting-outside-effects` | Claude Code 2.1.278 | 0.1.0 | `sha256:7b25a5f8ab091f38607e9b8a891d6fdfd56673e0935c4a47d972eec957ee4d45` |
