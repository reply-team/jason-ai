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

- [`managed-campaign-work`](managed-campaign-work/SKILL.md) — **draft.** Turning an objective into durable
  campaign work and following it: the provider check, the campaign, the people it is about, provider
  operations and AI role work, and what the runtime does with them afterwards.
- [`approvals-and-questions`](approvals-and-questions/SKILL.md) — **draft.** What Jason is waiting for a
  person to decide: the approvals parked at a claim and the questions a running role raised, presented in the
  person's own words and recorded as their decision.
- [`operating-the-installation`](operating-the-installation/SKILL.md) — **draft.** The installation itself:
  whether the runtime is up, starting and stopping it, having it start at logon, the plugins and routes it
  has, the hosts it can launch, and whether a newer version exists.
- [`troubleshooting-jason`](troubleshooting-jason/SKILL.md) — **draft.** Why work stopped: the runtime, the
  work item's error code and the chronicle; the states a person needs told apart, and the repairs that are
  safe.
- [`reporting-outside-effects`](reporting-outside-effects/SKILL.md) — **draft.** Telling Jason about an effect
  produced somewhere else, before it acts on a view of the world that is wrong.

### Installing one by hand

**There is no install verb yet.** Until there is, a person installs an interactive skill by copying its
directory into wherever their own agent host looks for skills — for Claude Code that is
`~/.claude/skills/<name>/` for themselves or `.claude/skills/<name>/` inside a project, as its own
documentation describes. Other hosts have their own location. The directory name is the skill's name, and the
two must match: a skill whose front matter names anything else is loaded by nobody and reported by nobody.

## The skills a launched role reads

`roles/<role>/` holds one skill per role, named after its directory because that is how a host looks one up.

- [`roles/researcher`](roles/researcher/SKILL.md) — **draft.** The brief a role is given when the runtime
  launches it: how to call home in the one command form that is permitted, how to read and keep its campaign
  note, and how to answer in the shape the work item asked for.
- [`roles/manager`](roles/manager/SKILL.md) — **draft.** The review a manager is launched for: why it was
  woken, what it reads and in which order, what it may decide alone, how it asks a person the one question it
  cannot answer, and the line it leaves in the chronicle every time.

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
