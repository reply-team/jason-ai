# Execution profiles

Which agent host runs a piece of AI work, and how that is decided, recorded and repaired.

This document is for whoever operates an installation. How work items are claimed, launched and
reported on is [docs/work-execution.md](work-execution.md); which plugin performs a provider
operation and in which account is [docs/routing.md](routing.md); what a canonical operation means is
[docs/contracts/](contracts/README.md).

## 1. What a profile is

Jason ships no AI client, no model account and no credentials. Background AI work runs on an agent
host the user installed and authenticated themselves. An **execution profile** is Jason's non-secret
description of one such host: which program to start, what the launched agent may not do, the tools
the person grants it by name, and the word it calls home by.

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
| `host` | which host this describes. A closed vocabulary — the `host` column of [§12](#12-the-hosts). An unknown value is refused when the profile is written, never discovered at launch |
| `program` | what to start: an absolute path, or a name resolved on `PATH`. A `.cmd`, `.bat` or `.ps1`, in any case, is a script a shell would have to interpret and is refused when the profile is written with `shell_script`: Jason starts programs, never shells. The path is read as the operating system reads it, so a name Windows trims back to a script, such as `claude.cmd.`, is refused too. A profile stored before the rule keeps its program, and the claim refuses such a program with `host_not_available` before a child exists. A bare name without the extension is looked up on the search path (on Windows as an `.exe`, a `.com` or npm's own shim) |
| `args` | what this profile adds after the arguments the runtime composes |
| `deny` | what the launched agent may not do, in the host's own vocabulary |
| `tools` | the tools the person grants the launched agent, by name: `web_search`, `web_fetch`, each at most once. A closed vocabulary — any other word is refused when the profile is written, and so is a word named twice, or one the revision's `deny` takes away by the host's name for it. Empty unless somebody names one (§7) |
| `cli_command` | the bare command word the agent calls home with, of letters, digits, dots, dashes and underscores and starting with a letter or a digit; defaults to the runtime's own executable name |
| `host_version_verified` | the host version this profile was proven against, as a version such as `2.1.289`: three numbers, and a pre-release after `-` if any. Recorded and reported; not enforced. A profile stored before the version shape was required keeps what it has; a write that names the field is held to it |

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
jason profile update local-claude --deny Write --deny "Bash(curl *)" --reason "no fetching through the shell"
jason profile update local-claude --deny Write --deny "Bash(curl *)" --tool web_search --tool web_fetch \
  --reason "the person said the roles may search the web"
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
against, the session id minted for that attempt alone where the host takes one from the runtime (a host that mints
its own is told none, and none is recorded; §12), and **the tools the revision granted** — in the
API's words, `web_search` and `web_fetch`, and empty where it granted none; where a launch was made, the
host's names for them are in the argument list. A record whose claim never read a revision — a role's own
entry command, a refusal made before the revision was read, an attempt from before grants — has no `tools`.

An edit to the profile after the launch leaves all of it exactly as it stands. Inherited work runs
the profile as it stands **now** rather than as it stood when the ancestor ran — otherwise a repaired
profile could never reach the work that inherited it — and both numbers are kept, so the difference
can be read rather than guessed.

## 6. What the agent finds where it runs

The per-attempt work directory is the per-item half of the launch. Each host's folders are in [§12](#12-the-hosts).
For Claude Code, the runtime writes, before the child starts:

- `.claude/settings.json` holding the revision's **deny** rules, a deny of each web tool the revision does not
  grant (§7), each rule once, and three exclusions,
  `"claudeMdExcludes": ["**/CLAUDE.md", "**/.claude/rules/**", "**/AGENTS.md"]` (§7), and nothing else;
- `.claude/skills/<role>/` copied from `<data>/skills/roles/<role>/`, files only, no link followed.

**Only one half of a policy can travel there.** A directory the runtime creates is not a workspace
the host trusts, and an allow rule in an untrusted directory is ignored without being reported — so
what the agent *may* do goes on the command line, where it is read, and what it *may not* do goes in
the directory, where a deny rule is honoured either way. Verified against Claude Code 2.1.275.

The callback rule is composed from a **bare command word** and never a path — `Bash(jason *)`, and beside it
`PowerShell(jason *)`. The runtime therefore puts its own directory at the front of the child's `PATH` and tells
the agent the word in the launch envelope, so the skill can teach exactly what the rule permits. The only paths
in an allow rule are the person's memory files, one file each (§7). In a published installation that directory holds the one executable; in a development
layout it holds everything built beside it, which is worth knowing before concluding that a child reached
something by accident. An allow rule matches the command the agent actually issues, so a skill teaches the
plain form and nothing built around it. What the rule does **not** do is bound the session: the host decides
some commands on its own, and §7 says what a launched session really can and cannot reach.

A role with no skill directory launches normally: nothing was configured, so nothing is missing. A
skill that *was* configured and could not be given to the role — misnamed, or past
`Roles:MaxSkillBytes` — refuses the attempt instead, because a role doing the job untaught costs a
real launch and leaves only a log line behind.

This repository's runtime pack now contributes a `SKILL.md` for **every one of the nine roles the
runtime seeds**, so a role launching with no skill directory is a directory an operator has not
composed rather than a role nobody wrote for.

**Who contributes what to that directory.** An operator composes `<data>/skills/roles/<role>/` from the
skill packs, and the packs contribute **different files** so that installing one never overwrites the
other. This repository's runtime pack contributes `SKILL.md`: the launch contract every role is held to
and what that role is for. A business pack contributes `METHOD.md` beside it — the profession's method
for that role — which the runtime copies across with everything else in the directory. A role whose
directory holds only `SKILL.md` is taught the contract and nothing else, and that is complete on its
own: nothing in the launch depends on a second file being there, or on the role reading one.

## 7. What a launched session is, and is not, given

An agent host is installed and configured by a person, for their own work. Starting one for a background work
item borrows that installation, and unless the launch says otherwise it borrows the whole of it. The launch says
otherwise, and this section is what that comes to. What follows was **measured** by running the composed command
against Claude Code 2.1.275 — and what a role may read, how it calls back and the exclusion of `CLAUDE.md` against
Claude Code 2.1.309 on Windows; where a sentence states the mode's own rule rather than something measured, it
says so, because the two are not equally strong.

**Settings come from the work directory and nowhere else.** The command carries `--setting-sources project`, so
the only settings the session reads are the ones the launcher wrote into `.claude/`. The operator's own
settings, plugins and hooks do not arrive — a hook is somebody's standing instruction to their own sessions, and
it has no business running inside a work item's attempt. `--strict-mcp-config` does the same for MCP servers: a
launched session has none. Measured on one machine, the same brief ran with 79 tools, 87 skills, 123 slash
commands and three MCP servers when those flags were absent, and with 27 built-in tools, 19 skills — the
built-ins plus the role's own, which still loads — 54 commands and no MCP server when they were present.

Settings, but not the operator's own memory: their user-level `~/.claude/CLAUDE.md` reached every launched
session in spite of `--setting-sources project`, and the roles answered in the language it asks for. So the work
directory's settings carry `"claudeMdExcludes": ["**/CLAUDE.md", "**/.claude/rules/**", "**/AGENTS.md"]`,
patterns the host matches against each file's absolute path, and **a launched role loads no `CLAUDE.md` at all** —
the operator's, and any above the work directory. The second pattern names the files of a `.claude/rules` folder,
which the host loads beside a `CLAUDE.md`: the operator's own `~/.claude/rules/`, and the one in each folder above
the work directory. A rules file in a folder above the work directory was measured not to reach the role; the
operator's own folder was **not measured**, because a measurement here never writes into a real home, and the host
decides both by the same list. The third names every `AGENTS.md`. Where the host finds no `CLAUDE.md` above a
session it loads each `AGENTS.md` above it instead, and with every `CLAUDE.md` excluded it finds none: an
`AGENTS.md` and a `.claude/AGENTS.md` in a folder above the work directory, standing for the operator's home, both
reached the role under the first two patterns, and neither did once the third was added, in a second session over
the same folders. The person's guidance for the work reaches the role as the memory files the envelope names
instead, which the role reads through its read rules and never through this list. An organisation's managed
`CLAUDE.md` cannot be excluded, by the host's own rule.

**The tools are all still listed; the mode and the allow rules decide what runs.** Every built-in tool the host
has appears to the session, and the allow rules name two things: the callback, and the memory files — and a third
only where the person granted it, the tools below. Of the tools
a role reached for, the host let `Read` through on its own inside the work directory, while `Glob`, `Write`,
`Edit` and a state-changing PowerShell command were **refused by the mode**, and so was a `Read` anywhere outside
the work directory that no rule names. Through either shell the rules grant the callback and nothing else; any
other command falls to the host's own read-only floor, which lets harmless output through. One chained read-only
shell command ran and one piped one was refused, so the host's own reading of what is harmless is a floor under
the rules, and how far that floor extends was not isolated: a read outside the work directory through a shell,
rather than through the `Read` tool, was not tried.

**What a role may read, and how it calls back.** Three forms, each measured against Claude Code 2.1.309 on
Windows, so that a later host has a line to be checked against:

- **One read rule per memory file**, `Read(//c/Users/…/AGENTS.md)`: two slashes for "from the root", the drive
  letter lower-cased and every separator a forward slash. It makes exactly that file readable — a space or a
  comma in the path included, and by the path the envelope gives, drive letter upper-cased — while the
  `CLAUDE.md` beside it and another campaign's `AGENTS.md` stay refused. A level the envelope gives as null gets no rule, and no rule
  names a folder or a pattern: no directory is added either, because the levels nest and a folder would reach
  every project and campaign below it. Off Windows the rule is `Read(//home/…/AGENTS.md)`, the form the host's
  documentation gives for an absolute path; it was **not measured** on Linux or macOS.
- **The callback through either shell**, `PowerShell(jason *)` beside `Bash(jason *)`, exactly as narrow. On
  Windows the host offers its PowerShell tool beside Bash, and a role that called back through it under the Bash
  rule alone was refused, so its attempt ended without its result. What `PowerShell(jason *)` grants is the
  callback and nothing else. Any other PowerShell command falls to the host's own read-only floor, the same as for
  Bash, which lets harmless output through — `Write-Output` ran with no rule — and refused the one state-changing
  command tried. It is composed on every platform: where the host offers no PowerShell tool it approves nothing.
- **No `CLAUDE.md`, rules file or `AGENTS.md`**,
  `"claudeMdExcludes": ["**/CLAUDE.md", "**/.claude/rules/**", "**/AGENTS.md"]` in the work directory's
  settings, as above. The patterns were **not measured** on Linux or macOS.

In this version a memory file whose path holds `[`, `]`, `*`, `?`, `(` or `)` — or, off Windows, a backslash, and
on Windows a path that does not start at a drive letter — gets no rule. The host does not escape a rule written by
hand, so an escaped guess could grant more than the one file; and it splits its allow list at a space or a comma
that follows a closing parenthesis, so a folder named `Acme (EU) Ltd` would be read as a rule for part of its name,
and one whose name starts with `) ` as a rule for the whole folder above it. The envelope still names the file; the
role's read is refused in the host's usual words, and the role reports the refusal as it reports any refused tool.
Nothing the role sees says that the folder's name is the cause: the claim's warning does, one line naming the
attempt and the level — `client`, `project` or `campaign` — and never the path. Renaming the folder fixes it, once
Jason has the new path (`jason project link` records a project's and its campaigns').

**What a grant of tools adds.** A revision's `tools` are the person's word that the roles it launches may search
and read the web, and the web is closed without it: **without the grant the tool is not there; with it, the role
reads the web within what the person's own deny rules leave.** Each tool granted is one more allow rule, in the
host's own name for it and nothing around it — `web_search` becomes `WebSearch` and `web_fetch` becomes `WebFetch`,
after the callback and the read rules — and each tool not granted is a deny of that same bare name, written into the
work directory beside the person's own deny rules. The two never name the same tool. A profile's own `args` cannot
add either, because `--allowed-tools` is a flag a profile may not carry, nor take one away: `--disallowed-tools` is
one too. An ungranted tool is denied rather than left unlisted because, run with neither an allow rule nor a deny,
the same host fetched a page of its own documentation list without asking, while it refused an ordinary page. Run
against Claude Code 2.1.310 on Windows, in a work directory prepared as the runtime prepares one: with the two allow
rules a role searched the web and fetched an ordinary page; with the two denies it was not offered either tool, and
asked for a page of that documentation list it fetched nothing, the run still ending as a success. Both tools are
loaded on demand, and the role was seen to reach them through the host's tool search, which no rule named.

What a grant does not do:

- **It grants nothing else.** No other tool, no file-writing tool, and no network through a shell: a command that
  fetches a page is any other shell command, and falls to the host's own floor.
- **It names no site.** A grant of `web_fetch` reaches every address the host will fetch. A restriction to some
  domains would be a field of its own, and this version has none.
- **It does not outrank a deny.** The profile's `deny` is written into the work directory, and the host's deny wins
  over the grant. Run against the same host: with `WebFetch` granted and denied by that bare name, the role was not
  offered the tool at all; with it granted and `WebFetch(domain:example.com)` denied, the tool was offered, a fetch
  of that one site was refused, and a page of another site was fetched. So a profile that grants a tool and denies it
  by the host's own name for it, spelled as the host spells it, is refused when it is written — the refusal is on the
  grant, `tools[i]` with the code `denied`, and names the deny it meets — while a deny that is a pattern is not
  refused and is written beside the grant; how much of the grant it leaves is the host's to decide, and that one
  pattern is the only one run. A patch is held to the pair as it would be launched: one that names only `tools`
  meets the profile's `deny` as it stands, and the other way round, and the refusal says which index is the
  profile's. A grant is withdrawn by an update that names an empty `tools` (the CLI's `--file`), never by a deny of
  the tool.

A grant is part of the revision like every other field, so changing it appends a revision, an attempt keeps the
grant it ran under, and an edit made while it runs changes nothing it says.

The mode's rule — as the mode states it, not as a measurement here — is that anything needing permission is
refused. What was seen is narrower: nothing outside the callback was seen to change anything, and nothing
state-changing outside it was tried but the one PowerShell command above. So this is not a sandbox, the allow rule is not a ceiling, and an
installation that needs one should not read this section as providing it.

**The role has no file-writing tool.** A rule naming the file-editing tools, in the host's own documented form,
was tried and granted neither writing nor editing; no form that grants them is known. Under the composed shape,
then, a role cannot create a file even in its own work directory. Anything it wants to keep travels inside a
command: `rolenote.set` with `--note`, `workitem.set_result`, and `workitem.complete` with `--result`. A role
skill that says "write the file first" is teaching a role to fail in the middle of a real attempt.

**The transcript is larger than the work.** A session that streams partial messages and runs verbose writes a
great deal for very little: a nine-turn errand measured 129 KB and a twelve-turn one 203 KB, against the 1 MiB
`Roles:MaxStdoutBytes` allows. Nothing is read from that stream — a result arrives through the API — but an
installation running longer roles should know the bound is nearer than it sounds.

**One thing is not confined, and it is larger than it sounds.** The host writes its own per-session
**auto-memory** directory under the operator's user configuration, whatever the setting sources say — and what
lands there is not a directory of notes. It is **the whole session**: one transcript file, about 222 KB for a
thirteen-turn errand, holding **the launch envelope** — the runtime's descriptor path among it — the brief, the
campaign context and anything the role read or wrote, including its note. There is **no capability token** in
it and no key; that much of the redaction holds wherever the session is written. Jason neither reads nor writes
the file and nothing in the work directory points at it, but the runtime's own record of the same attempt keeps
a byte count and a hash where this keeps the words. So an installation whose reason for running work through
Jason is that its logs hold no prompts should know that a launched session's conversation is kept in full, in
the operator's home, by the host — and that a machine launching background roles accumulates one of these per
attempt. None of the flags composed here prevents it, and no other flag was tried.

## 8. Role notes

A launched role has no harness that survives its attempt. On somebody's own machine, a role's notes are
ordinary working files — a scratch document it keeps beside the work and reads again next time. A role
Jason launches gets a fresh work directory per attempt and loses it afterwards, so the same working
file has to live somewhere the next attempt can reach: here, in the runtime's own store. That is the
whole of why notes are a table. It does not make them true.

**A note is not authoritative** (INV-MEM-001). It is one role's own memory, in that role's words, from
whenever it was written. Where a note disagrees with campaign state, **the state is what is true** and the
note is out of date; and a role's notes never outrank what a person has said — a named preference, or what
they wrote for the work — however the note puts it. Nothing in the runtime reads a note, and no decision is
taken because of what one says.

It is also none of the other five knowledge concepts: a **skill** teaches the job, the person's **memory
files** are their own standing guidance at each level, read by the host and never by the runtime, a **named
preference** is a fact about the client a person states and only a person sets (both in
[projects](projects.md)), and **campaign context** is the campaign's shared knowledge. A client-level note is
still a role's own working memory, written by that role for itself, in its own words, and shared across
campaigns only because the role's craft is; learned practice, which does not exist in this version, would be
the installation's own knowledge, distilled from what happened across campaigns and written by no role for
itself.

**Three levels.** A role keeps one note at each of three levels: **the client's** — its general craft, for
all of this installation's work — **a project's**, and **a campaign's**. Every call names exactly one level:
`campaign_id` for a campaign's, `project_id` for a project's, and neither for the client's. Naming both is
refused as `validation_failed`, with both fields named: a campaign's project is the campaign's, and a note is
never at two levels. A blank id is refused by its field rather than read as the client's level, where a
write would replace the role's general craft. A project nobody made is `project_not_found`; a campaign keeps
its own refusals.

A reader composes the levels in one order — **the client's, then the project's, then the campaign's** — and
where two say different things **the lower wins**: the project's over the client's, the campaign's over
both. The runtime composes nothing: it answers each level on its own, and a level nobody wrote at reads
empty.

What a role keeps at which level follows one rule, for every role, the campaign manager included: guesses about
people and a campaign's state go in the campaign's note, the craft of a project in the project's, and general
craft in the client's, and **a note never carries what belongs to a level below it**. A project's note is read on
every campaign of that project and the client's on every campaign there is, so the rule is what keeps one
campaign's people out of another's. The launch contract in every role's skill teaches it, beside the order in
which a launched role reads the person's preferences, their memory files and the brief.

```sh
jason rolenote get --role researcher
jason rolenote get --role researcher --project <project-id>
jason rolenote get --role researcher --campaign <campaign-id>
jason rolenote set --role researcher --campaign <campaign-id> --note '{"gatekeeper":"the switchboard hangs up after six"}'
jason rolenote set --role copywriter --project <project-id> --note '{"voice":"short sentences; the product named once"}'
jason rolenote list --human
jason rolenote list --campaign <campaign-id> --human
```

The older form, `jason rolenote get <campaign-id> researcher`, which skills already written print, is still
accepted as the campaign's note — but only whole: exactly the campaign and the role, and none of `--role`,
`--campaign` or `--project`. One argument alone is refused, because it is what the older form becomes when the
variable that held the campaign is empty, and read as the role it would reach the client's note. **A note is read
or written at the client's level — `get` and `set` — only by `--role` with neither scope option**, so a script that
meant a campaign never reads or writes the client's note by accident. `list` with neither option lists the client's
level: which roles have written there, how much and when, never what they wrote.

- **Written in one command.** A launched role has no file-writing tool (§7), so its note travels inside the
  call as one compact JSON object; a person at a keyboard may use `--note-file` instead.
- **Replaced whole.** There is no patch verb: a role merging into its own memory would have to reason
  about what an earlier session of itself meant by a key, and the rule that needs no reasoning is that
  the last writer owns the document. Clearing a note is writing `{}`.
- **A JSON object, at most 64 KiB** of its canonical form, at every level. A role that needs more than that is keeping
  a record rather than a note. The measurement is the **UTF-8 bytes of the canonical form**, with
  nothing escaped, so a character costs what UTF-8 spells it in: a note in Cyrillic, Greek or Hebrew
  holds about half the characters the figure suggests and one in Japanese about a third, as it would
  as a file. The receipt's `note_bytes` is the number to steer by: it is the number the cap compares,
  and a refusal names the note's size beside the cap, once the note has been written by this version. A size
  written by an earlier version — in the chronicle, or in the `note_bytes` of a note not written since — counted the
  escaped form, in which every character outside ASCII, DEL, the controls and seven ASCII marks takes six bytes and one
  outside the Basic Multilingual Plane twelve; a chronicle line written now can so carry an `old` size in that measure
  beside a `new` one in UTF-8. The note's hash is still taken over the escaped form,
  the one this runtime names everything by — a report's assertion and a route's binding are hashed in
  the same one.
- **A role that has never written reads an empty note**, with `updated_at: null`, rather than a 404, at
  every level. Reading its memory is the first thing a launched role does, and every role would otherwise
  carry the code that tells "nothing yet" from "something went wrong".
- **A listing is one level's**: which roles have written there, how much and when — never what they wrote.
  Naming neither id lists the client's notes, never every note in the installation.
- **The chronicle records that a note was set** — its level, the role, its size, its content hash, the
  actor, and the size and hash it replaced — and **never what it said**. A note holds half-formed guesses
  about people, and the journal is the one table nobody can edit afterwards. A campaign's note is a line of
  that campaign; a project's note is a line of the installation that names the project, and the client's a
  line of the installation that names neither.
- An archived campaign takes no more notes, like every other write against one. Reading stays open.

The launch envelope carries `role_memory: {campaign_id, role, project_id}` — the address of the role's
note at each of the three levels — and never the content. A document copied in at launch would be what
the role believed then, arriving beside the brief as though it were current; read through the API at the
moment it is wanted, it is plainly a document with an age — and current runtime state is read the same
way, through the same CLI, so the two are never confused. Beside it, `memory_files` gives the paths of
the person's memory files — the `AGENTS.md` of the client, of the campaign's project and of the campaign —
and never their words. They are the person's guidance rather than the role's memory, so a note never
outranks them, and current state outranks both; which paths are given, and when one is null, is in the
launch envelope's section of [work-execution.md](work-execution.md).

## 9. When AI work cannot run

Every one of these is decided before a child process exists, except `host_not_available` where a host that ran
answered that it is not signed in: that ends the attempt after the child ran, with the host's fixed sentence and no
byte of what the host wrote, where the host's row in §12 names the signal Jason reads. Each keeps its attempt so the
refusal can be read, and is **not retried** — nothing about the work changes between two scans. The first four name the level that chose
the profile, and the attempt records how far the choice got; the last two are about the role rather than the
profile, so there is no level to name.

| Code | Meaning |
|---|---|
| `lineage_resolution_unsupported` | a run caused this work and no profile can be inherited (§4) |
| `profile_not_found` | the profile named does not exist |
| `profile_disabled` | it exists and is out of service |
| `host_not_available` | its program is not on this machine, or is a script a shell would have to interpret; or the host ran and answered that it is not signed in (§12) |
| `role_skill_invalid` | the role's skill could not be given to it |
| `role_skill_unreadable` | the role's skill was being replaced while this launch tried to read it |
| `role_not_launchable` | no profile at any level, and the role has no entry command either |

None of them is classified, and an attempt error the failure rules do not name is final. That is the right
answer for every one of these: a profile that does not exist will not exist on the next scan either.

The repair is to name a profile on the item, its campaign or its role — or to fix the configuration —
**before the work is claimed**; for a host that is not signed in, it is for the person to sign in to it on this
machine, under their own plan. A refused attempt fails the item, and a finished item is not changed,
so work already refused is asked for again rather than reopened. That is how every fail-closed
refusal in this runtime behaves.

Deterministic work is untouched by any of it. A machine with no agent host installed still runs
provider operations and everything else; only the AI work blocks, and it says which program it went
looking for.

## 10. Settings

| Setting | Default | What it does |
|---|---|---|
| `Roles:DefaultExecutionProfile` | none | the profile agent work falls back to when nothing more specific names one |
| `Roles:DefaultEntryCommand` | `[]` | the command used for a role without one of its own, where no profile is configured anywhere |
| `Roles:MaxStdoutBytes` | 1 MiB | how much of a child's transcript is kept before it is cut |
| `Roles:MaxSkillBytes` | 1 MiB | how large a role's skill may be |

## 11. What this version does not do

**Session resume.** A session id is minted per attempt and recorded where the host takes one, which is what a later nudge
needs, but nothing resumes one: a host that is interrupted is retried from the start.

**A general lineage resolver.** One hop, materialized at creation. Branch selection and ambiguity
across a graph are not attempted.

**Rate or spend limits.** Nothing bounds how many hosts a runtime launches per hour except
`Dispatcher:MaxParallel`. A launched agent session is not free, and at background volumes that is a
number worth watching rather than discovering.

## 12. The hosts

The interactive session runs from any agent with a shell: `jason` is a plain command, the skills are Markdown files,
and the harness root carries `AGENTS.md`. `jason skills install --root <dir>` deploys into a host this table does not
name. A launched role runs on a host a profile names, and a profile can name only a host this build knows: the
`host` column below is the vocabulary, and a profile naming any other word is refused when it is written. `jason
status` says which of these hosts it detected on this machine and whether every enabled profile names one of them
(`agent_hosts`).

| Host | `host` | Skills root `jason skills install` deploys to | Role's skill in the work directory | Reads in place of `AGENTS.md` | Background roles in this build | Checked against | Tools | Reads a memory file by | Takes the brief |
|---|---|---|---|---|---|---|---|---|---|
| Claude Code | `claude_code` | `~/.claude/skills` | `.claude/skills/<role>/` | `CLAUDE.md` (`@AGENTS.md`) | yes | measured on 2.1.309 and 2.1.310 on Windows (§7) | `web_search` as `WebSearch`, `web_fetch` as `WebFetch`: each granted and denied by that word (§7) | one `Read(//…/AGENTS.md)` rule per file, on its command line (§7) | as its prompt, on standard input, in print mode (`-p`) |
| Codex CLI | `codex` | — | — | — | not in this build yet: a profile naming it is refused | — | — | — | — |
| Gemini CLI: `gemini_cli` runs on a Code Assist Standard or Enterprise sign-in; individual Google plans run `antigravity_cli` ([Google's notice](https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/)) | `gemini_cli` | — | — | — | not in this build yet: a profile naming it is refused | — | — | — | — |
| Antigravity CLI: `gemini_cli` runs on a Code Assist Standard or Enterprise sign-in; individual Google plans run `antigravity_cli` ([Google's notice](https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/)) | `antigravity_cli` | — | — | — | not in this build yet: a profile naming it is refused | — | — | — | — |
| GitHub Copilot CLI | `copilot_cli` | `~/.agents/skills` | `.agents/skills/<role>/` | — | yes | built from the host's documentation for version 1.0.105, the version the allowlist was read at; parser not read, so a profile's arguments are only the documented options the person owns: `--model=`, `--effort=` (or `--reasoning-effort=`), `--max-ai-credits=`, `--no-color` and `--disable-client-telemetry`. No undocumented flag, however harmless, is taken this release. Not yet run on an account | `web_fetch` as `url`; the host documents no search tool, so a grant of `web_search` is refused and a deny of it is kept | one `read(<absolute path>)` rule per file in its `--allow-tool` list, on its command line (documented for version 1.0.105; not yet measured to reach outside the work directory) | as its prompt, on standard input, with no `-p`; nothing of the envelope on its command line |
| Cursor CLI | `cursor_cli` | — | — | — | not in this build yet: a profile naming it is refused | — | — | — | — |

A host's skills root is found by a marker under the home directory — a folder or a file the host keeps there, and only
a path of that kind detects it — and `jason skills install --host <host>` deploys into that host's root alone. Each harness folder carries, beside its
`AGENTS.md`, a one-line import file for every host that reads a file of another name. Jason holds no key for any host:
each runs under the person's own plan, signed in by them. A host whose program is not on this machine ends the attempt
with `host_not_available`; so does a host that stops because it is not signed in, where that host's row names the
signal Jason reads for it; a host whose row names none ends such an attempt as any exit of it ends. A
`host_not_available` carries one sentence of Jason's own, never the host's words.

One rule holds every host's tools, where a profile is written: a grant of a tool the host does not have is refused,
naming the host and the tool; a deny of a tool the host does not have is kept as the person's word, since it holds
anyway; a deny of a tool the host has and cannot take away is refused, naming the host; and a grant of a tool the host
has but Jason cannot make true on it is refused, naming the host. The `Tools` column is each host's table of the
`tools` vocabulary, so the reason for a refusal can be read there. A host may also refuse arguments its own parser reads
otherwise than the runtime composed them, naming the host, beside the flags it reserves.

A host that reads its allow rules from the work directory rather than its command line is given them there, beside its
deny rules — the command word that calls the runtime back and the memory files' paths, never the capability token or
the envelope — and the attempt's launch records them beside the deny. A host that mints its own session id is told
none, and the attempt records none.

### GitHub Copilot CLI

`copilot_cli` is built from the host's documentation for version 1.0.105 and **has not yet been run on an account**:
every form below is the documented one, the tests prove the command Jason composes and the launch around it against a
stand-in under the host's command word, and a sentence that says "not measured" is one the documentation leaves open.
The host is installed by a person — `npm install -g @github/copilot`, `winget install GitHub.Copilot`,
`brew install copilot-cli` or the install script the host's page gives — and signed in by them with `copilot login`;
Jason holds no key for it, and a token the person's own environment carries (`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`,
`GITHUB_TOKEN`) is theirs and is not read or set. Then:

```
jason profile create copilot --host copilot_cli --program copilot --host-version-verified 1.0.105
```

`jason skills install --host copilot_cli` deploys into `~/.agents/skills`, which the host reads as personal skills; a
user-level skill there reaches every launched role of this host as well as the role's own, which is placed in the work
directory under `.agents/skills/<role>/`. The host is found by `~/.copilot/config.json`, which is also what Visual Studio
Code's own download of a `copilot` command leaves, so a machine can be reported as having the host when it has only
that; a person who moved the host's folder with `COPILOT_HOME` is not detected, and deploys with `--root`. Instructions:
the host reads `AGENTS.md` itself, so the harness folder carries no import file for it, and a launched role is told not to
load the person's own instruction files (`--no-custom-instructions`), reading the memory files its launch names by rule.

**What a launch is.** The brief goes to the host on stdin, which it reads when it is given no prompt of its own, so the
command carries no print flag and nothing of the envelope. It is `--output-format json` (a transcript, which the runtime
keeps and never parses: the outcome comes back through the CLI), `--no-ask-user`, `--no-auto-update`,
`--no-custom-instructions`, `--disable-builtin-mcps`, a fresh `--session-id` minted for the attempt, and then three lists
— `--available-tools`, `--allow-tool` and `--deny-tool` — and last the profile's own arguments. Nothing is written into
the work directory and nothing of the person's own configuration is touched. No flag that trusts the folder or allows
every tool, path or address is composed, and a profile may not carry one. The host's parser is closed and was not read
(the reserved list is from its documentation), so a hidden alias of a composed flag, a cluster of short options or a value
glued to one cannot be ruled out; a profile's arguments are therefore an allowlist of the documented options a person
owns, each as one argument: `--model=`, `--effort=` (or `--reasoning-effort=`), `--max-ai-credits=`, and the
bare `--no-color` and `--disable-client-telemetry`. Anything else is refused, harmless or not: the cost of a closed parser.
The log level and the log directory are not among them: they change what the host writes outside the work directory.

| Tool the host offers a role | Why |
|---|---|
| `view`, `glob`, `grep` | reading, inside the work directory |
| `skill` | the role's own skill |
| `bash` and its `read_`, `list_`, `stop_` companions; on Windows `powershell` and its three | the callback to the runtime, and reading a long one back |
| `web_fetch` | only where the profile grants `web_fetch` |

The host offers no tool that writes or patches a file, asks a person, starts another agent or keeps a memory, and
documents no search tool. A tool the host is not offered is unavailable even where a rule allows it, which is why the
list is the confinement and the rules below are what the role may then do.

**The three forms, as the host documents them for 1.0.105.**

- **What a role may read:** `read(<absolute path>)` for each memory file the launch names, in the order client, project,
  campaign, once for a file named twice, forward slashes on Windows. A path holding a comma, a parenthesis or a pattern
  character gets no rule, because the list is comma-separated and only the shell and url kinds take wildcards; the
  level is reported as unreadable and the role reports the refusal. **Not measured:** whether a rule for one file lets the
  role read it outside the work directory, which the host's pages do not say.
- **How it calls back:** `shell(jason:*)`, the runtime's command word followed by anything, which the host reads as every
  subcommand of it; one rule of the `shell` kind covers both shell tools. The bare word with no argument is not matched.
- **What deny means:** a deny wins over an allow in the host, even over an allow-everything, so a web fetch not granted is
  denied (`url`) and left out of the tool set, and every `write` is denied beside the profile's own `deny` words, which are
  the host's `kind(argument)` rules as the person wrote them, one per entry. A deny of `web_search` is kept as the person's
  word and holds anyway, since the host has no such tool.

**What is not confined.** The host keeps its own history of the run, and of the sessions it started, under the person's
`~/.copilot`, whatever the work directory says. The run is in a folder the host does not trust: the host documents that
such a run loads no hooks, workspace servers, repository plugin settings or project extensions, and **not measured** are
whether it stops to ask, and whether the work directory's skills load there. A person who sets `COPILOT_ALLOW_ALL` in their
own environment trusts the folder and allows everything except what the deny list names; Jason does not read or clear it.

**Not signed in.** An end without a report whose exit is not zero and whose stderr names `copilot login`, the command the
host's startup errors recommend, is final and says `host_not_available` with one sentence of Jason's own, never the
host's words. That signal is the documented recommendation and **not a measured text**: the host's pages state no exit code
and no message for a signed-out run, so a signed-out host that says it differently ends as an ordinary attempt that
reported nothing.
