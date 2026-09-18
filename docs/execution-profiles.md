# Execution profiles

Which agent host runs a piece of AI work, and how that is decided, recorded and repaired.

This document is for whoever operates an installation. How work items are claimed, launched and
reported on is [docs/work-execution.md](work-execution.md); which plugin performs a provider
operation and in which account is [docs/routing.md](routing.md); what a canonical operation means is
[docs/contracts/](contracts/README.md).

## 1. What a profile is

Jason ships no AI client, no model account and no credentials. Background AI work runs on an agent
host the user installed and authenticated themselves. An **execution profile** is Jason's non-secret
description of one such host: which program to start, what the launched agent may not do, and the
word it calls home by.

A profile has two halves, and the split is the point.

- The **profile** carries what is meant to change: its name, a description, which revision is
  current, and whether it is disabled.
- A **revision** carries everything behavioural, and never changes. Editing a profile appends
  another complete revision; it does not rewrite one.

Revisions are frozen because an attempt records the revision it ran under for ever. A revision that
could be edited would let a change made today rewrite what last week's attempt says it did. That
immutability is enforced three times over: no verb rewrites a revision, an interceptor refuses it
before it reaches the database, and database triggers refuse it there.

A revision holds:

| Field | Meaning |
|---|---|
| `host` | which host this describes. A closed vocabulary — `claude_code` in this version. An unknown value is refused when the profile is written, never discovered at launch |
| `program` | what to start: an absolute path, or a name resolved on `PATH` |
| `args` | what this profile adds after the arguments the runtime composes |
| `deny` | what the launched agent may not do, in the host's own vocabulary |
| `cli_command` | the bare command word the agent calls home with; defaults to the runtime's own executable name |
| `host_version_verified` | the host version this profile was proven against. Recorded and reported; not enforced |

**A profile cannot hold a credential, because there is nowhere to put one.** No environment field, no
secret field, no free-form bag. The host a profile names was installed and authenticated by a person,
and the profile only says which one to start. A launched child's environment is the runtime's own plus
four values and not one more — the data directory, the attempt, the work item, and this build's own
directory at the front of `PATH` — every one of them non-secret and already in the launch envelope or
known to the process. The capability token is in none of them, and in nothing else the child can
read: [docs/work-execution.md](work-execution.md) has the whole list.

There is **no model or effort field**. `--model` exists in Claude Code's flag surface at 2.1.275 but
no run here has exercised it, and shipping a mapping nothing has exercised is how a profile comes to
mean something other than what it says. A profile that wants a model says so in `args`, and the
attempt records the whole argument list as launched.

## 2. The verbs

```sh
jason profile create local-claude --host claude_code --program claude \
  --arg --model --arg sonnet --deny Write --host-version-verified 2.1.275
jason profile update local-claude --deny Write --deny WebFetch --reason "no fetching"
jason profile get local-claude --revision 1
jason profile list --human
jason profile disable local-claude --reason "host being upgraded"
jason profile enable local-claude
```

`profile.update` is a patch: a field the patch does not name is copied from the current revision, so
every revision is a whole statement rather than a delta. A patch that names only the description
moves the profile and appends no revision — a revision number says which statement of the launch an
attempt ran under, not how many times somebody edited the sentence describing it.

**There is no delete.** An attempt names its revision for ever, so a profile is disabled rather than
removed. **There is no rename** either: work items, campaigns, roles and the settings all point at a
profile by name, and a rename would quietly break every one of them.

## 3. Which profile runs a piece of work

Resolution happens when the work is claimed, before any child process exists, in this order:

1. the work item's own `execution_profile`;
2. its campaign's policy;
3. its role's policy;
4. what it inherited from the run that created it (§4);
5. the configured default, `Roles:DefaultExecutionProfile`.

**A campaign beats a role.** A campaign is this runtime's unit of isolation and the operator's most
specific standing statement about a body of work; a role policy is a statement about a kind of worker
everywhere, and the narrower statement wins.

If no level names a profile at all, the role's own entry command runs the work exactly as it did
before profiles existed, and the attempt records that this is what happened.

Settings are read before the database is open, so `Roles:DefaultExecutionProfile` is held to the
shape of a name and nothing more. A well-formed name that no profile answers is not a refusal to
start — the profile may be created a minute later — it becomes a visible refusal on the work item
that needed it.

## 4. Causal lineage

Work inherits the execution profile of the run that caused it, so that a chain of work keeps running
where it started rather than drifting to the house default halfway along.

The record is written **once, when the item is created**, and never recomputed. Walking back through
the ancestry later would answer with whatever that history has since been edited into, and the
question is what caused *this* item.

It is read one hop and no further:

- the creating actor is not an attempt → **root**;
- the creating actor is an attempt that pinned a profile → **inherited** from it, with the profile,
  the revision and the attempt recorded;
- an attempt that pinned none → its own item's record is handed on unchanged.

That last line is what carries lineage across deterministic work: a provider operation created by an
agent attempt holds a record it will never use itself and gives it to whatever its own attempt
creates.

**Root and unresolved are different things and nothing collapses them.** Root is work nothing caused
that could have had a profile — a person, an interactive agent session, the runtime itself — and it
legitimately resolves to the configured default. Unresolved is work a run caused whose profile cannot
be read, and it **blocks**: using the default there would change which AI executor somebody's work
runs under without anybody saying so. In this version unresolved arises from work created before
profiles existed, which the migration marks honestly rather than guessing about.

An interactive agent session is root work by definition. The runtime never launched it, owns no
profile for it, and can resolve none; treating it as unresolvable ancestry would block the ordinary
interactive path over a run that never had a profile to change.

**A launched executor does not have to remember to say who it is.** The runtime puts the attempt's
id in the child's environment and the CLI reads it when no `--actor` was given, so work an agent
creates is its attempt's whether or not the agent thought about it. The claim is still verified
against an attempt the runtime knows.

## 5. What an attempt records

Written at the claim, completed at the end, never rewritten: the profile's id, name and **revision**,
which level chose it, the revision it inherited where lineage chose, the host, the program as this
machine resolved it, **the whole argument list as launched**, the version the profile was verified
against, and the session id minted for that attempt alone.

An edit to the profile after the launch leaves all of it exactly as it stands. Inherited work runs
the profile as it stands **now** rather than as it stood when the ancestor ran — otherwise a repaired
profile could never reach the work that inherited it — and both numbers are kept, so the difference
can be read rather than guessed.

## 6. What the agent finds where it runs

The per-attempt work directory is the per-item half of the launch. The runtime writes, before the
child starts:

- `.claude/settings.json` holding the revision's **deny** rules and nothing else;
- `.claude/skills/<role>/` copied from `<data>/skills/roles/<role>/`, files only, no link followed.

**Only one half of a policy can travel there.** A directory the runtime creates is not a workspace
the host trusts, and an allow rule in an untrusted directory is ignored without being reported — so
what the agent *may* do goes on the command line, where it is read, and what it *may not* do goes in
the directory, where a deny rule is honoured either way. Verified against Claude Code 2.1.275.

The allow rule is composed from a **bare command word** and never a path: bare words are what has been
verified, and a path inside an allow pattern has not been. The runtime therefore puts its own directory at the
front of the child's `PATH` and tells the agent the word in the launch envelope, so the skill can teach exactly
what the rule permits. In a published installation that directory holds the one executable; in a development
layout it holds everything built beside it, which is worth knowing before concluding that a child reached
something by accident. Allow rules match the command the agent actually issues, so a
skill that teaches it to redirect or chain its callback would break the rule silently.

A role with no skill directory launches normally: nothing was configured, so nothing is missing. A
skill that *was* configured and could not be given to the role — misnamed, or past
`Roles:MaxSkillBytes` — refuses the attempt instead, because a role doing the job untaught costs a
real launch and leaves only a log line behind.

## 7. Role notes

A launched role has no harness that survives its attempt. On somebody's own machine, a role's notes are
ordinary working files — a scratch document it keeps beside the work and reads again next time. A role
Jason launches gets a fresh work directory per attempt and loses it afterwards, so the same working
file has to live somewhere the next attempt can reach: here, in the runtime's own store. That is the
whole of why notes are a table. It does not make them true.

**A note is not authoritative** (INV-MEM-001). It is one role's own memory of one campaign, in that
role's words, from whenever it was written. Where a note disagrees with campaign state, **the state is
what is true** and the note is out of date. Nothing in the runtime reads a note, and no decision is
taken because of what one says.

It is also none of the other three knowledge concepts: a **skill** teaches the job, **campaign
context** is the campaign's shared knowledge, **learned practice** would be cross-campaign and does not
exist in this version.

One document per campaign and role:

```sh
jason rolenote get <campaign-id> researcher
jason rolenote set <campaign-id> researcher --note-file note.json
jason rolenote list <campaign-id> --human
```

- **Replaced whole.** There is no patch verb: a role merging into its own memory would have to reason
  about what an earlier session of itself meant by a key, and the rule that needs no reasoning is that
  the last writer owns the document. Clearing a note is writing `{}`.
- **A JSON object, at most 64 KiB** of its canonical form. A role that needs more than that is keeping
  a record rather than a note. The measurement is the canonical form, in which **characters outside
  ASCII are escaped** — six bytes for a character UTF-8 spells in two — so a note in Cyrillic, Greek
  or Japanese holds roughly a third of the characters the figure suggests, and is refused while it is
  still a quarter of 64 KiB as a file. The receipt's `note_bytes` is the number to steer by: it is the
  number the cap compares. That form is the one this runtime names everything by — a report's
  assertion and a route's binding are hashed in the same one — so it is a fact to know rather than a
  rule of notes.
- **A role that has never written reads an empty note**, with `updated_at: null`, rather than a 404.
  Reading its memory is the first thing a launched role does, and every role would otherwise carry the
  code that tells "nothing yet" from "something went wrong".
- **The chronicle records that a note was set** — the campaign, the role, its size, its content hash,
  the actor, and the size and hash it replaced — and **never what it said**. A note holds half-formed
  guesses about people, and the journal is the one table nobody can edit afterwards.
- An archived campaign takes no more notes, like every other write against one. Reading stays open.

The launch envelope carries `role_memory: {campaign_id, role}` and never the content. A document
copied in at launch would be what the role believed then, arriving beside the brief as though it were
current; read through the API at the moment it is wanted, it is plainly a document with an age — and
current runtime state is read the same way, through the same CLI, so the two are never confused.

## 8. When AI work cannot run

Every one of these is decided before a child process exists, keeps its attempt so the refusal can be read, and
is **not retried** — nothing about the work changes between two scans. The first four name the level that chose
the profile, and the attempt records how far the choice got; the last two are about the role rather than the
profile, so there is no level to name.

| Code | Meaning |
|---|---|
| `lineage_resolution_unsupported` | a run caused this work and no profile can be inherited (§4) |
| `profile_not_found` | the profile named does not exist |
| `profile_disabled` | it exists and is out of service |
| `host_not_available` | its program is not on this machine |
| `role_skill_invalid` | the role's skill could not be given to it |
| `role_not_launchable` | no profile at any level, and the role has no entry command either |

None of them is classified, and an attempt error the failure rules do not name is final. That is the right
answer for every one of these: a profile that does not exist will not exist on the next scan either.

The repair is to name a profile on the item, its campaign or its role — or to fix the configuration —
**before the work is claimed**. A refused attempt fails the item, and a finished item is not changed,
so work already refused is asked for again rather than reopened. That is how every fail-closed
refusal in this runtime behaves.

Deterministic work is untouched by any of it. A machine with no agent host installed still runs
provider operations and everything else; only the AI work blocks, and it says which program it went
looking for.

## 9. Settings

| Setting | Default | What it does |
|---|---|---|
| `Roles:DefaultExecutionProfile` | none | the profile agent work falls back to when nothing more specific names one |
| `Roles:DefaultEntryCommand` | `[]` | the command used for a role without one of its own, where no profile is configured anywhere |
| `Roles:MaxStdoutBytes` | 1 MiB | how much of a child's transcript is kept before it is cut |
| `Roles:MaxSkillBytes` | 1 MiB | how large a role's skill may be |

## 10. What this version does not do

**Session resume.** A session id is minted per attempt and recorded, which is what a later nudge
needs, but nothing resumes one: a host that is interrupted is retried from the start.

**A second host.** The vocabulary has one value and one implementation. Adding another is one more
implementation of the same seam plus one more value; nothing about the contract changes.

**A general lineage resolver.** One hop, materialized at creation. Branch selection and ambiguity
across a graph are not attempted.

**Rate or spend limits.** Nothing bounds how many hosts a runtime launches per hour except
`Dispatcher:MaxParallel`. A launched agent session is not free, and at background volumes that is a
number worth watching rather than discovering.
