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
| `args` | what this profile adds after the arguments the runtime composes. A host's parser reads a short flag with more letters or a value after it as several flags, or as a flag and its value, and a lone dash as the prompt, so a short flag stands alone: a short flag with more letters or a value after it, a lone dash and a double dash are refused when the profile is written, naming the host, on every host; so is an entry holding a control character, since a NUL ends a command line and another splits it |
| `deny` | what the launched agent may not do, in the host's own vocabulary |
| `tools` | the tools the person grants the launched agent, by name: `web_search`, `web_fetch`, each at most once. A closed vocabulary — any other word is refused when the profile is written, and so is a word named twice, or one the revision's `deny` takes away by the host's name for it. Empty unless somebody names one (§7) |
| `cli_command` | the bare command word the agent calls home with, of letters, digits, dots, dashes and underscores and starting with a letter or a digit; defaults to the runtime's own executable name |
| `host_version_verified` | the host version this profile was proven against, as a version such as `2.1.289`: three numbers, and a pre-release after `-` if any. Recorded and reported; not enforced. A profile stored before the version shape was required keeps what it has; a write that names the field is held to it |

**A profile cannot hold a credential, because there is nowhere to put one.** No environment field, no
secret field, no free-form bag. The host a profile names was installed and authenticated by a person,
and the profile only says which one to start. A launched child's environment is the runtime's own plus
the runtime's four and the constants a host's row of §12 declares, never a secret and never a value
taken from a person. The four are the data directory, the attempt, the work item, and this build's own
directory at the front of `PATH`; every one of them is non-secret and already in the launch envelope or
known to the process, and a host's constants are fixed in its own row. The capability token is in none of them, and in nothing else the child can
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

**On Claude Code, only one half of a policy can travel there.** A directory the runtime creates is not a workspace
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
| Codex CLI | `codex` | `~/.agents/skills` | `.agents/skills/<role>/` | — | yes | On Windows, first: Codex's elevated sandbox must be set up once by an administrator ([setup](https://learn.chatgpt.com/docs/windows/windows-sandbox)); until then every command a role runs is refused, so the role cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal. Commands are not confined by name: any program a role can read runs, inside the sandbox. Holding the network to loopback rests on Codex's network proxy, an experimental feature in 0.166.0. On Windows, the elevated sandbox may let a role read more than the list. The person's global `AGENTS.md` and their skills reach every Codex role. Whether `jason` reaches the runtime from inside the sandbox is not yet checked on Linux, macOS or Windows. Built from the host's documentation and source for 0.166.0, npm's current release; measured on Windows with the CLI the Codex app bundles, 0.166.0-alpha.5.1; not yet run on an account ([Codex CLI](#codex-cli)) | `web_search` as `WebSearch`: granted as a live search, denied by that word; `web_fetch`: not on this host, so a grant is refused and a deny holds anyway | one `"<path>"="read"` entry per file in the permissions profile on its command line (`-c permissions.jason.filesystem=…`), 0.166.0 | as its prompt, on standard input, with no prompt word on the command line (`codex exec`: instructions are read from stdin when none is given, 0.166.0) |
| Gemini CLI: `gemini_cli` runs on a Code Assist Standard or Enterprise sign-in; individual Google plans run `antigravity_cli` ([Google's notice](https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/)) | `gemini_cli` | `~/.agents/skills` | `.agents/skills/<role>/` | `GEMINI.md` (`@AGENTS.md`) | yes | built from the host's documentation and source for version 0.66.0; not yet run on an account; a signed-out run ends with exit 41, which the notes read as "could not authenticate" (notes below) | `web_search` as `google_web_search`, `web_fetch` as `web_fetch`: each granted by an allow rule of the policy file and denied by a deny rule of it (notes below) | one `--include-directories` folder for each level the launch names, the host reads that folder whole, and the client level's folder is the harness root (notes below) | as its prompt, on standard input, with no prompt flag, in a non-interactive run |
| Antigravity CLI: `gemini_cli` runs on a Code Assist Standard or Enterprise sign-in; individual Google plans run `antigravity_cli` ([Google's notice](https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/)) | `antigravity_cli` | `~/.gemini/antigravity-cli/skills` | `.agents/skills/<role>/` | — | yes | built from the host's documentation for version 1.4.10 (2026-10-29); not yet run on an account; the host's parser is not read, so a profile's arguments are only `--model=<name>` and `--effort=<level>`, the options its pages name for the person, read at 1.4.10, and the effort's range is not checked because its two pages disagree on it. No undocumented flag is taken in this release, harmless or not. A run that is not signed in is a non-zero exit whose last words of standard error hold "authentication required" or "Authentication expired" (the section says where each is read) | `web_fetch` as `read_url(*)`, a rule Jason can neither grant nor deny: a grant of either tool and every deny word are refused, and the person's own settings decide what a session may do (the section names the file, the key and the Windows form of the rule), and per-run permissions are not built yet; the host documents no rule for a search | the person's own `read_file(<harness folder>)` rule in their `~/.gemini/antigravity-cli/settings.json`, which Jason cannot write; the launch names each memory file unreadable at every claim, since it cannot see that rule | as one stream-json user line on standard input |
| GitHub Copilot CLI | `copilot_cli` | `~/.agents/skills` | `.agents/skills/<role>/` | — | yes | built from the host's documentation for version 1.0.105, the version the allowlist was read at; parser not read, so a profile's arguments are only the documented options the person owns: `--model=`, `--effort=` (or `--reasoning-effort=`), `--max-ai-credits=`, `--no-color` and `--disable-client-telemetry`. No undocumented flag, however harmless, is taken this release. Not signed in is a non-zero exit whose stderr names `copilot login`. Not yet run on an account | `web_fetch` as `url`; the host documents no search tool, so a grant of `web_search` is refused and a deny of it is kept | one `read(<absolute path>)` rule per file in its `--allow-tool` list, on its command line (documented for version 1.0.105; not yet measured to reach outside the work directory) | as its prompt, on standard input, with no `-p`; nothing of the envelope on its command line |
| Cursor CLI | `cursor_cli` | `~/.agents/skills` | `.agents/skills/<role>/` | — | yes | built from the host's documentation and the installed bundle for version 2026.10.22-27dcf5f; not yet run on an account | `web_fetch` as `WebFetch(*)`, granted and denied by that rule; `web_search` has no rule, and is neither granted nor denied (the person's own `autoAcceptWebSearch` setting decides it) | one `Read(<absolute path>)` rule per file, in `.cursor/cli.json` in the work directory and never on its command line (version 2026.10.22-27dcf5f, from the bundle; not yet run on an account) | as its prompt, on standard input, in print mode (`-p`), when no word for a prompt stands on its command line (version 2026.10.22-27dcf5f, from the bundle; not yet run on an account) |

A host's skills root is found by a marker under the home directory — a folder or a file the host keeps there, and only
a path of that kind detects it — and `jason skills install --host <host>` deploys into that host's root alone. Each harness folder carries, beside its
`AGENTS.md`, a one-line import file for every host that reads a file of another name. Jason holds no key for any host:
each runs under the person's own plan, signed in by them. A host whose program is not on this machine ends the attempt
with `host_not_available`; so does a host that stops because it is not signed in, where that host's row names the
signal Jason reads for it; a host whose row names none ends such an attempt as any exit of it ends. A
`host_not_available` carries one sentence of Jason's own, never the host's words.

One rule holds every host's tools, where a profile is written: a grant of a tool the host does not have is refused,
naming the host and the tool; a deny of a tool the host does not have is kept as the person's word, since it holds
anyway; a deny of a tool the host has and cannot take away is refused, naming the host; and a grant of a tool
Jason cannot make true on the host, one the host has but documents no rule for included, is refused, naming the host. The `Tools` column is each host's table of the
`tools` vocabulary, so the reason for a refusal can be read there. A host may also refuse arguments its own parser reads
otherwise than the runtime composed them, naming the host, beside the flags it reserves.

A host that reads its allow rules from the work directory rather than its command line is given them there, beside its
deny rules — the command word that calls the runtime back and the memory files' paths, never the capability token or
the envelope — and the attempt's launch records them beside the deny. A host that mints its own session id is told
none, and the attempt records none.

A proxy flag or a proxy variable, such as Gemini CLI's `--proxy`, is the person's environment's to set: Jason sets none of
them and reserves none of them. A flag that names the host's own server, its credentials or its certificate check is
reserved where the host has one, as Cursor CLI's section says, and a host whose arguments are an allowlist takes no flag
outside it, as its section says. A variable the runtime's own environment holds reaches a launched child like any other.

### Codex CLI

Install it from [its own page](https://github.com/openai/codex), where its commands are kept current, sign in to it yourself
with `codex login`, then name it in a profile:
`jason profile create local-codex --host codex --program codex`. Jason never signs in for you, and never looks for the
copy of the CLI the Codex app carries.

A launched role runs `codex exec` and reads its brief on stdin. None of the person's own configuration reaches it
(`--ignore-user-config`), nor their command rules (`--ignore-rules`), hooks, plugins, apps, memories or MCP servers, and
a setting the host does not know fails the start (`--strict-config`). Codex confines a role by a sandbox rather than by
a list of commands: the runtime names a permissions profile of its own on the command line, and every command the role
runs is held by it, whatever its name.

**On Windows, first:** Codex's elevated sandbox must be set up once by an administrator
([Codex's Windows sandbox setup](https://learn.chatgpt.com/docs/windows/windows-sandbox)); until then every command a
role runs is refused, so the role cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal. Every Windows run names that sandbox
(`windows.sandbox="elevated"`) rather than leaving it to the person's configuration: `--ignore-user-config` drops the
person's own choice, and with none named Codex runs a command with no sandbox at all. Measured with 0.166.0-alpha.5.1,
the sandbox that needs no administrator refused every command of this profile, since it reads the whole disk or nothing
and cannot hold the network to loopback.

**What a role may read.** The platform's libraries, the work directory, the `jason` executable — the one file a
single-file build is, as a release is, or the folder of any other build — the folder that build unpacked its libraries
into (where it names none, the base a build unpacks under: `.net` in the home, and on Windows in the temporary folder),
`<data>/run`, where the CLI finds the runtime, and each memory file the launch names. A memory file whose path holds a
character the sandbox reads as a pattern (`* ? [ ] { }`) is left unreadable. Where `<data>/run`, the executable or its
unpack folder holds one, a role could not call the runtime back, so the claim refuses the work with
`host_not_available`, naming the path, before a child exists. A path the profile denies reading is denied, even one
this list names: a deny wins over a read. On Windows every path of the list is written as a drive and forward slashes, as a
deny's is, so a deny of a file the list names is the same key and replaces the read. Whether the elevated
sandbox reads only this list is not yet checked; Codex documents the paths it denies there rather than a list of the
paths it reads, so a role may read more than this list. What no flag reaches: Codex reads its own global `AGENTS.md`
(in `~/.codex`, or `$CODEX_HOME`) for every run, so that file reaches every Codex role and should carry nothing a role
must not read. Codex offers every role the skills in `~/.agents/skills`, in `$CODEX_HOME/skills` and in its own system
cache too, beside the role's own, even with `--ignore-user-config`: `jason skills install` deploys the runtime and
business skills to `~/.agents/skills`, so every Codex role is offered those and any skill the person keeps there.

**Calling home.** The role runs `jason` in its shell, and the CLI reaches the runtime on loopback. The sandbox's network
is on with loopback allowed, and every other address is kept out by Codex's network proxy, an experimental feature in
0.166.0. On Linux, whether `jason` reaches the runtime from inside the sandbox is not yet checked; the sandbox may route
loopback through its proxy, which the CLI does not use. On macOS, whether `jason` reaches the runtime from inside the
sandbox is not yet checked. On Windows, whether `jason` reaches the runtime from inside the elevated sandbox is not yet
checked.

**What a deny means.** Nothing is writable, so `Write`, `Edit`, `MultiEdit` and `NotebookEdit` hold already. `WebSearch`
turns the search off, even where `tools` grants it. `Read(//…)`, `Read(//d/…)` on Windows and `Read(~/…)` deny that
path in the sandbox; a `Read` of any other form names no path the sandbox can hold, and is refused. `WebFetch` holds
already, since Codex has no tool that fetches a page and the network reaches loopback alone, and is kept as written.
Codex reads no rule by any other word: it confines a run by its sandbox, not by a list of tools. So a deny of a command
by its name (`Bash(…)`, `PowerShell(…)`, `Shell(…)`), of a tool Codex does not have (`Glob`, `Grep`, `Task`), of an MCP
tool or of a fetch of one domain (`WebFetch(domain:…)`) is refused when the profile is written or moved to Codex, naming
the host and the word, rather than dropped at a launch. Commands are not confined by name either: any program the role
can read runs, inside the same sandbox. A grant of `web_fetch` is refused because Codex has no tool that fetches a page.

**Arguments.** A profile's `args` are flags, each value joined to its flag by `=` (`--model=<name>`): Codex reads a bare
word as the prompt, which would replace the brief, or as a subcommand, and takes a short flag one letter at a time, so
letters run together or a value glued on without `=` (`-ms`, `-mo3`) are refused. `-c` is the runtime's alone, since one
setting after the runtime's could replace any of them; the model is the person's. A flag the runtime composes is
refused by every name Codex's parser gives it, including the ones its help never prints, such as `--yolo`.

**What an attempt records.** Codex mints the id of its own run and takes none, so the attempt records none; the id Codex
printed is in the attempt's `stdout.log`. An end without a report is read as any other end: Codex says nothing stable
about a missing sign-in.

### GitHub Copilot CLI

`copilot_cli` is built from the host's documentation for version 1.0.105 and **has not yet been run on an account**:
every form below is the documented one, the tests prove the command Jason composes and the launch around it against a
stand-in under the host's command word, and a sentence that says "not measured" is one the documentation leaves open.
The host is installed by a person from [GitHub's installation page](https://docs.github.com/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli),
where its commands are kept current, and signed in by them with `copilot login`;
Jason holds no key for it, and a token the person's own environment carries (`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`,
`GITHUB_TOKEN`) is theirs and is not read or set. Then:

```
jason profile create copilot --host copilot_cli --program copilot
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
`--no-custom-instructions`, `--disable-builtin-mcps`, a fresh `--session-id` minted for the attempt, four flags that
close what the person's own settings would otherwise decide — `--disallow-temp-dir` (the system temporary folder is not
opened to the role), `--sync-session=off` (the host's history of the run is not synced), `--mode=interactive` (a setting
cannot make the run autopilot) and `--no-bash-env` (no startup file named by `BASH_ENV` is read by the shell) — and then
three lists — `--available-tools`, `--allow-tool` and `--deny-tool` — and last the profile's own arguments. No configuration is
written into the work directory and nothing of the person's own configuration is touched. No flag that trusts the folder
or allows every tool, path or address is composed, and a profile may not carry one: every flag above is reserved, with the
other documented flags that load, reach or say more. The host's parser is closed and was not read
(the reserved list is from its documentation), so a hidden alias of a composed flag, a cluster of short options or a value
glued to one cannot be ruled out; a profile's arguments are therefore an allowlist of the documented options a person
owns, each as one argument: `--model=`, `--effort=` (or `--reasoning-effort=`), `--max-ai-credits=`, and the
bare `--no-color` and `--disable-client-telemetry`. Anything else is refused, harmless or not: the cost of a closed parser;
so is a value holding a control character, which a space does not.
The log level and the log directory are not among them: they change what the host writes outside the work directory.

| Tool the host offers a role | Why |
|---|---|
| `view`, `glob`, `grep` | reading, inside the work directory |
| `skill` | loading a skill: every skill the host discovers, the person's and plugins' included, not the role's own alone |
| `bash` and its `read_`, `list_`, `stop_` companions; on Windows `powershell` and its three | the callback to the runtime, and reading a long one back |
| `web_fetch` | only where the profile grants `web_fetch` |

The host offers no tool that writes or patches a file, asks a person, starts another agent or keeps a memory, and
documents no search tool. A tool the host is not offered is unavailable even where a rule allows it, which is why the
list is the confinement and the rules below are what the role may then do.

**The three forms, as the host documents them for 1.0.105.**

- **What a role may read:** `read(<absolute path>)` for each memory file the launch names, in the order client, project,
  campaign, once for a file named twice, forward slashes on Windows. A path holding a comma, a parenthesis or a pattern
  character gets no rule, because the list is comma-separated and the host documents no pattern rule for `read`; the
  level is reported as unreadable and the role reports the refusal. **Not measured:** whether a rule for one file lets the
  role read it outside the work directory, which the host's pages do not say.
- **How it calls back:** `shell(jason:*)`, the runtime's command word followed by anything, which the host reads as every
  subcommand of it; one rule of the `shell` kind covers both shell tools. The bare word with no argument is not matched.
- **What deny means:** a deny wins over an allow in the host, even over an allow-everything, so a web fetch not granted is
  denied (`url`) and left out of the tool set, and every `write` is denied beside the profile's own `deny` words, which are
  the host's `kind(argument)` rules as the person wrote them, one per entry. The host reads a rule's head as one of
  `memory`, `read`, `shell`, `url` or `write`, in that case, with or without a part in parentheses, and any other head as an
  MCP server's name, which denies nothing the role is offered; so a deny with another head is refused when the profile is
  written, naming the host, and so is a deny by the name of the fetch tool (`web_fetch`), whose word is `url`. A deny of
  `web_search` is kept as the person's word and holds anyway, since the host has no such tool.

**When no rule allows a call.** In a run, a call no rule allows is denied, and the reason goes back to the role; the
reference says so, no run has shown it. A run has no terminal to ask, and `--no-ask-user` takes the question tool away as
well.

**What is not confined.** Jason composes what a flag can close, and a flag cannot close the person's own setup. The
history of the run stays under the person's `~/.copilot` only because the launch turns the host's session sync off; the
host keeps it there whatever the work directory says. Beyond that, the host's own documentation puts these outside any
flag, and each is the person's, not Jason's, to change:

| What the person's setup can do | Where it lives |
|---|---|
| Run user-level hooks: the run loads no repository hooks, but the person's own apply to every session | `~/.copilot/hooks/` and the `hooks` key of `~/.copilot/settings.json` |
| Start MCP servers of their own, which the tool set keeps out of the role's reach but which still start; the host has a flag to disable one server by name and none to disable all | the person's MCP configuration |
| Turn on assisted approval for a run with no terminal | `COPILOT_ASSISTED_APPROVAL=true` in the environment |
| Make allow-all the default permission mode | `defaultPermissionMode` set to `"allow-all"` in the user settings |
| Add skill folders, which the role can then load | `skillDirectories` in the user settings and `COPILOT_SKILLS_DIRS` in the environment |
| Have a saved approval apply, keyed by the git root of the folder the run is in; a deny still wins over it | `~/.copilot/permissions-config.json` |
| Allow everything: `COPILOT_ALLOW_ALL` is equal to `--allow-all`, which is every tool, every path and every URL, minus what the deny list names; only the exact value `true` also trusts the folder | the environment |

Jason does not read or clear any of these. A person who wants the `skill` tool to reach less than every skill the host
discovers, the person's and plugins' included, excludes folders with `ignoredSkillsLocations` or turns a skill off with
`disabledSkills`, both in the user settings; a skill's own `allowed-tools` can open the shell to every command with `"*"`,
and the allow rule that holds the shell to the runtime's command word does not stop a skill that names it.

The file-writing tools are denied and not offered; the shell is held to the command word by the allow rule. What shell
redirection by the callback can write is not measured. The run is in a folder the host does not trust, and the host
documents that such a run loads no hooks, workspace servers, repository plugin settings or project extensions.

**Not measured.** The documentation leaves these open, and no run has answered them:

- whether a rule naming one file lets the role read it outside the work directory;
- whether a run in a folder the host does not trust stops to ask;
- whether the work directory's skills load there;
- whether shell redirection by the callback can write a file the deny list names;
- whether `taskbarPresence`, a Windows setting, does anything in a run with no desktop;
- whether version 1.0.105 accepts every flag above: the host's changelog does not say when `--disallow-temp-dir` and
  `--sync-session` appeared, so a launch is composed from the reference and not from a run.

**Not signed in.** An end without a report whose exit is not zero and whose stderr names `copilot login`, the command the
host's startup errors recommend, is final and says `host_not_available` with one sentence of Jason's own, never the
host's words. That signal is the documented recommendation and **not a measured text**: the host's pages state no exit code
and no message for a signed-out run, so a signed-out host that says it differently ends as an ordinary attempt that
reported nothing.

### Cursor CLI

The row above was built from Cursor's documentation and from the bundle of the installed version, 2026.10.22-27dcf5f,
read as text. It has not been run on an account: Jason holds no key, and the sign-in is the person's own, made as
themselves with `agent login`. Install it from [Cursor's installation page](https://cursor.com/docs/cli/installation.md), where its
commands are kept current. Each note says what a person relying on this host should know, and the first is the one
thing that decides whether the host works at all.

1. **Not yet measured: whether an allowed callback runs unattended.** A launch writes `Shell(<cli word>)` into the
   allow list, which is the command that calls the runtime back. Whether the host runs a command that is allow-listed
   without asking, in print mode and without `--force`, has not been seen on a signed-in run. Jason passes none of
   `--force`, `--yolo`, `--auto-review`, `--sandbox` or `--approve-mcps`, and none of them is the answer if the callback
   is not run: a profile's arguments may not carry them, and a session that cannot call back cannot report, so the attempt ends as `executor_exited` and is retried against the same refusal.
2. **Reads are the widest of the hosts of the matrix.** Cursor takes the union of the allow and deny lists of every
   configuration layer it finds, the person's user level and the work directory's own, a deny winning over an allow, and
   it reads user-level skills. Note that the union holds for every kind of allow rule, not for reads only: what the person's own
   configuration allows, a shell command or a write among it, stands beside what Jason writes. The person's own
   `approvalMode` at the user level (an allowlist, auto-review, or unrestricted "Run Everything") is theirs too, and
   Jason does not set it. Jason confines nothing it reads: the `Read(<file>)` rules a launch writes
   name the memory files the session may read, and do not narrow what else the signed-in person's account can read. Of
   the hosts a profile can name, this is the widest read form of the matrix, so a profile for it belongs on an account
   whose files a role may see.
3. **The three forms.** Reads: as in the note above. Call-home: the one shell command Jason allows, the runtime's own,
   as in the first note; the person's own user-level configuration may allow more, and Jason cannot narrow it, so
   `Write(**)` does not stop a shell command that writes. Deny: `Write(**)` always, and `WebFetch(*)` unless the revision
   grants a fetch, then the profile's own deny words, written to `.cursor/cli.json` in the attempt's work directory, a
   file that carries permissions, the file's version key and the editor mode off, nothing else. A deny word is one of
   `Shell(...)`, `Read(...)`, `Write(...)`, `WebFetch(...)` or `Mcp(...)`, written as given; whether a head without
   parentheses, such as `Write`, is read as a rule has not been seen.
4. **Web search is neither granted nor denied.** The host has no rule for it in either list, and whether it runs is the
   person's own `autoAcceptWebSearch` setting, so a profile that grants `web_search` or denies it, in either spelling, is
   refused when it is written, naming the host.
5. **The trust marker.** The host reads a project's permission file only in a directory it trusts, and `--trust` makes
   an attempt's work directory one. It leaves one marker for each attempt under the person's own `~/.cursor`, which
   Jason does not remove.
6. **On Windows the program is a launcher script.** `agent.cmd` and `cursor-agent.cmd` start a PowerShell script, which
   starts the newest version's `node.exe` with its `index.js`; a script is never started through a shell, so Jason starts
   those two itself, choosing the version directory as that script does: a flat installation root is read first, where a
   `node.exe` and an `index.js` stand beside the launcher, and otherwise the newest date wins, and within one day Jason
   breaks a tie by name, which the script leaves unordered. Jason never runs the staging script beside them, and the
   person's own run of the launcher promotes a staged update, so the version Jason starts can change after one.
   `--program agent` is looked up on the search path, and an autostarted runtime's search
   path can differ from a terminal's, so where it is not found the profile names the launcher by its full path:
   `jason profile create <name> --host cursor_cli --program C:\Users\<you>\AppData\Local\cursor-agent\agent.cmd`.
   This holds for those two file names under this host, and for no other script under any host.
7. **One sentence when the host is not signed in.** The signal Jason reads is an exit that is not zero whose last
   words of standard error hold `agent login`, `cursor agent login` or `agent-local login` (no sign-in) or
   `stored authentication is invalid` (one the server refused), all read from the bundle. Such an attempt ends with
   `host_not_available` and a fixed sentence: run `agent login` as yourself. Nothing of what the host wrote is carried.
8. **A session id is told to the host.** `--new-session-id` is not in the host's help text; the bundle validates it as a
   version-4 UUID, and the attempt records the one Jason minted. A profile may not name it, nor `--resume`.
9. **The brief goes on stdin.** In print mode the host takes its prompt from the whole of stdin when no word for one is
   on its command line, and ignores stdin when one is, so no composed argument is a bare word and a profile's arguments
   take a value joined to its flag by an equals sign (`--model=name`), never a bare word and never a double dash. The
   host reads a short flag with more letters or a value after it as other flags or as a flag with a value, and a lone
   dash as the prompt, so a profile's arguments carry no such word either; a short flag stands alone. A flag that names the
   host's own server, its credentials or its certificate check (`--endpoint`, `--base-url`, `--header`, `--api-key` and
   `--insecure` among them) is reserved, as is one that makes a session act without asking or changes the tool set Jason
   composed.

### Gemini CLI

Install it from [the host's own page](https://github.com/google-gemini/gemini-cli), where its commands are kept
current, sign in to it yourself, then name it: `jason profile create <name> --host gemini_cli --program gemini`.

`gemini_cli` is written from Gemini CLI's documentation and source at version 0.66.0. No run on an account backs it:
Jason holds no key and the sign-in is the person's own, so the row says so and a profile may record the version it was
proven against in `host_version_verified`. Google has moved individual plans to Antigravity CLI; `gemini_cli` runs on a
Code Assist Standard or Enterprise sign-in, and the row's first cell links Google's notice. What follows is what this
build does, and what it has not measured.

- **Command.** The brief is the whole of standard input and the command carries no prompt. The flags composed are
  `--output-format=stream-json`, `--approval-mode=default`, `--skip-trust`, `--session-id=<id>`, a policy
  (`--admin-policy=.jason-policy`), a list of MCP servers that names none, no extensions, and one
  `--include-directories` for each folder holding a memory file the launch names. A profile's own `args` come last and
  may not name any of these, nor the prompt, the sandbox, the worktree or the resume flags, in any spelling the host's
  parser reads as the same option (the camel-case twin of a kebab-case name, the `--no-` form of either, and a short
  option's letter written with two dashes, `--y` being `-y`). They are
  long flags with their value joined by `=`, since a bare word is read as the prompt; a short option may stand only
  alone, since the parser reads `-yo` as two options and `-pfoo` as an option and its value; and a long flag's name is
  letters and digits with single dashes between them, since the parser reads a dot as a path into the flag's own value
  and drops or folds a leading, trailing or doubled dash and an underscore before it matches a name, so `---acp` or
  `--prompt-=x` would be a reserved option in a spelling no list holds. A value may hold a space and no control
  character, which would not reach the host as written.
  The host's parser declares no telemetry flag at 0.66.0 and refuses an unknown one before the run starts, so a profile
  has none to name. A role's run sends no telemetry whatever the person's own setting says, since the host would log
  prompts by default and the brief would go wherever the person points it: the constant below turns it off. A person who
  wants a role's run traced runs the host themselves; making it a profile's choice is for a later release.
- **Environment.** The constants this host declares are `NO_BROWSER=true`, `GEMINI_CLI_IDE_WORKSPACE_PATH=` and
  `GEMINI_TELEMETRY_ENABLED=false`, the last of which outranks the person's own telemetry setting. Without
  the first a signed-out run opens a browser or waits for a line on standard input, and with it the run fails at once
  with the host's authentication exit. That exit, 41, ends the attempt as `host_not_available` with Jason's own
  sentence, which says the host "could not authenticate" and no more: the exit is also an enforced sign-in type that
  does not match, a sign-in missing a setting of its own and a refresh that failed, so what a person tries is to sign in
  as themselves or to use `antigravity_cli`. No byte of what the host wrote is carried. The second is empty: an editor's
  companion names its workspace's folders in that variable and the host adds them to the run's own, and an empty value
  adds none, so a value the person's environment holds reaches no run.
- **Tools.** The policy file is `.jason-policy/jason.toml` in the work directory, read at the host's highest tier, so a
  person's own rules cannot widen it. Everything is denied first at the lowest priority; then the read tools, the
  role's own to-do list and `activate_skill` are allowed; then the runtime's command word is allowed as a shell prefix;
  then each granted tool is allowed; then what the role may not do is denied above every allow: `write_file`,
  `replace` and `ask_user` always, and the revision's `deny` words, where `run_shell_command(<prefix>)` is a prefix
  deny. A role writes through `jason`, never to disk. The policy holds the command word and the tool names only, never
  the capability token or the envelope.
- **System policies.** The host ignores the policy given to a run when its system policy folder holds any entry whose
  name ends in `.toml`, and says so in a warning only: `C:\ProgramData\gemini-cli\policies` on Windows,
  `/Library/Application Support/GeminiCli/policies` on macOS and `/etc/gemini-cli/policies` elsewhere. The run would then
  be held by the host's default rules and the person's own, which is not what the profile asked for. So where such an
  entry is, the attempt is refused before a child exists, as `host_not_available`, with a sentence that names the
  folder. A folder that is missing or cannot be read does not refuse a run, since the host goes on without it too.
- **Memory files.** The host's file tools are confined to the workspace, so a memory file outside it is readable only
  where its folder is included. The included folders are exactly the folders of the levels the launch names, and none
  when it names none. The host reads an included folder whole, which is wider than the one file; and the client level
  is the harness root, so a role reads every project's and campaign's memory file under it. A folder whose path ends in
  whitespace, holds a comma, or is not fully qualified cannot be named to the host exactly, and its level is reported
  unreadable.
  `GEMINI.md` imports `AGENTS.md`, and the work directory's `.gemini/settings.json` pins that file name and turns
  the person's hooks off. Antigravity CLI loads both files of a folder and turns a bare `@AGENTS.md` into a pointer to
  the file without inlining it, so an Antigravity CLI opened in a harness folder reads the memory file once and finds
  one extra line pointing at it.
- **Not measured.** The shell on Windows for the command prefix; whether `activate_skill` asks a person even where it is
  allowed; `--session-id` and `--admin-policy`, which are in the 0.66.0 source but not in its options table; the check
  of the system policy folder, which was written from the source and not run on a machine that has such a folder; the
  person's own `~/.gemini/GEMINI.md` and `includeDirectories`, and the person's own skills in `~/.gemini/skills`
  and `~/.agents/skills`, which a role may load through `activate_skill`, all of which reach every role; a leftover
  `~/.gemini/settings.json` from a person who has moved to Antigravity CLI, which still detects this host; and
  `GEMINI_CLI_HOME`, which moves `~/.gemini` with its settings and sign-in and which detection does not read, so a
  person who sets it is reported as not having the host.

### Antigravity CLI

`antigravity_cli` (the `agy` command) is built from the host's documentation for version 1.4.10 (2026-10-29) and **has
not yet been run on an account**: every form below is the documented one, the tests prove the command Jason composes
and the launch around it against a stand-in under the host's command word, and a sentence that says "not measured" is
one the documentation leaves open. Google splits its two command-line hosts by sign-in, as the matrix's first cell
says: individual Google plans run this host, and `gemini_cli` runs on a Code Assist Standard or Enterprise sign-in
([Google's notice](https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/)).
**Read this before connecting it.** Google's FAQ ([antigravity.google/docs/faq](https://antigravity.google/docs/faq/)) says: “Antigravity CLI headless mode (`agy -p`) is intended for direct scripting and CI workflows. Wrapping `agy` inside third-party coding agents or applications, or automating account rotation, is not supported under consumer plans.” Jason runs `agy` headless for each
background role; whether that use is covered on your plan is for you to judge, and the sentence is quoted here so that
you judge it before the host is connected.
The host is installed from the CLI tab of Google's quickstart ([antigravity.google/docs/getting-started?tab=cli](https://antigravity.google/docs/getting-started?tab=cli)),
which holds the install commands for macOS, Linux and Windows and is where they are kept current; the command line is
described under [antigravity.google/docs/cli/overview](https://antigravity.google/docs/cli/overview/). It is signed in
by the person, as [Google's page on signing in](https://antigravity.google/docs/install/) says: run `agy` once and sign in as yourself.
Jason holds no key for it and passes none: a `GEMINI_API_KEY` or any other credential in the person's own environment is
theirs, and is neither read nor set. Then:

```
jason profile create antigravity --host antigravity_cli --program agy
```

`jason skills install --host antigravity_cli` deploys into `~/.gemini/antigravity-cli/skills`, one of the three folders
the host reads personal skills from; the role's own skill is placed in the work directory under `.agents/skills/<role>/`.
The host is found by the folder `~/.gemini/antigravity-cli`, never by `~/.gemini` itself, which Gemini CLI keeps as well;
a person who moved the host's folder is not detected and deploys with `--root`. Instructions: the host reads `AGENTS.md`
and `GEMINI.md` itself, so the harness folder carries no import file for it. It also reads the person's own global
`AGENTS.md` and `GEMINI.md` under `~/.gemini`, and **those reach every launched role of this host**; nothing in the launch
can turn that off, so a person who keeps standing instructions there should know a background role will read them. On
Windows 11 24H2 the host's own sandbox has requirements the host's pages give; a launched role does not rely on it, and
a profile may not turn it on or off (`--sandbox` is reserved). Where the runtime's search path does not find `agy`, name
it by its full path with `--program`.

**What a launch is.** The brief goes to the host as one stream-json line on standard input, and the stream ends when
standard input closes: `{"event":"user","message":{"content":"<the envelope>"}}` and a line feed, the envelope being the
whole launch envelope as one string and nothing else being written. The host's other prompt forms take the prompt on its
command line, which the launch never does. The command is `--input-format stream-json --output-format stream-json
--print-timeout=0s` and then the profile's own arguments, last. The transcript is the host's stream of `init`,
`step_update` and `result` events, which the runtime keeps and never parses: the outcome comes back through the CLI. The
host's own print timeout would end a run with a success and partial output (so [the host's changelog](https://antigravity.google/docs/changelog/) says of its
expiry), and its pages disagree on the default, so Jason turns it off and the attempt's limit is the only limit. The host mints its own conversation id and takes none, so
the attempt records no session. No configuration is written into the work directory, nothing of the person's own configuration is
touched, and no flag that acts without asking, widens the workspace, takes another project or resumes a conversation is
composed or allowed in a profile. **The host's parser is not read**, because it is closed source and no copy of it was
available to inspect, so no argument is taken on the strength of what a parser might do with it. A profile's arguments are
the documented options the person owns, in long form with the value joined by an equals sign: `--model=<name>` and
`--effort=<level>`, read from the host's command-line reference and headless pages at version 1.4.10. The effort's range is
not checked, since the two pages disagree on it (low through max, and low, medium or high); a value must be there. Every
other word is refused where the profile is written, naming the host and the reason: an option the host documents that is
not the person's (such as `--disable-slash-commands`), one it does not document, a word shaped like a switch that skips
permissions (`--yolo`), a bare word, a double dash, a short flag, a cluster (`-pf`), a glued value (`-phello`) and the bare
dash, and a value that holds a control character. No undocumented flag is taken in this release, harmless or not.

A flag the runtime composes, or one that changes the session, is also on the reserved list, which keeps its own
refusal under the allowlist and stays as the record of what the documentation calls dangerous: the list is the reference
table's flags that the runtime composes or that change the session. `--permission-mode` is on it as a guard: the host's
modes page lists passing it as a common mistake and says the host uses `--mode`, so it is reserved beside `--mode`, and
what the host does with it is not stated.

**What a session may do is the person's to say.** The host takes its permission rules, of the form `action(target)`
(`command`, `read_file`, `write_file`, `read_url` and others), from the person's own settings file,
`~/.gemini/antigravity-cli/settings.json`, in its `permissions.allow` array, and from nowhere a run can carry them: there
is no flag for them and no file of a work directory. (Not `~/.gemini/settings.json`, which Gemini CLI reads. The host's
pages give the order "Deny > Ask > Allow", so a rule of the person's in either of the other two lists wins over the allow.)
The runtime never writes that folder. So on this host a grant of `web_fetch` or `web_search` and every word of a
profile's `deny` are refused where the profile is written, naming the host, and Jason composes no allow and no deny. A
headless run that no rule allows is denied without stopping and ends with an exit of 0 and the denied actions listed, so
what the person's rules leave out shows up as a role that ends without reporting. This is how the host is supported in
this release: Jason does not give a launched session its permissions run by run on this host, which is not built yet, and
the person's own two rules stand in for it. The person adds them once, to the file's `permissions.allow` array, beside what
is already there:

```json
{ "permissions": { "allow": [
  "command(jason)", "read_file(<harness folder>)" ] } }
```

- **`command(<cli word>)`**, with the word the runtime calls back with (`jason`, unless the installation names another),
  so the role can report what it did. Without it the role cannot call the runtime back. **On Windows shells** (PowerShell
  or Command Prompt) the host's permissions page says a command it cannot split cleanly into words needs an exact match,
  and that a command and its subcommands are matched with the `regex:` prefix, so write `command(regex:jason .*)`; the
  plain form is the one for other systems. Whether the plain form covers the report call on Windows is not measured.
- **`read_file(<harness folder>)`**, the harness root and so every memory file under it, so the role can read the
  person's `AGENTS.md` at each level. The launch names each memory file as unreadable at every claim, because Jason writes
  no rule for it and cannot see the person's; with the person's rule the role reads them anyway, and without it the role
  reports the refusal.

**The three forms.** What a role may read is the person's `read_file(<harness folder>)` rule. How a role calls back is the
person's `command(<cli word>)` rule. What a deny means here is nothing, because every deny word is refused where the
profile is written.

**Not measured, because the pages do not say.** Whether an allowed `command(<cli word>)` runs unattended in a headless run;
whether the plain `command(jason)` covers the report call on Windows, where the pages ask for the `regex:` form; whether a
run in a folder the host does not know stops to ask; whether the work directory's skills load there; whether the
workspace hook file `.agents/hooks.json` could carry a rule (the pages describe hooks that run before a tool and say
nothing of a launched session, so it is not used); whether the host copies a tool's standard error into its own, so that
a role's command printing "authentication required" before a non-zero end would be read as not signed in and end final;
whether a person's persistent default mode of `plan` stops a headless role from running the report command; what the
Windows install names the executable, and whether the program lookup takes it, which is why a full path with `--program`
is the way where the name is not found; and which exit code and words every run that is not signed in ends with. The two
phrases Jason reads are "authentication required", which [the headless page](https://antigravity.google/docs/cli/headless/)
quotes for a run with no credentials, and "Authentication expired", which [the host's changelog](https://antigravity.google/docs/changelog/) gives for an expired or revoked
sign-in. An end that is not zero whose last words of standard error hold one of those two is read as not signed in and
becomes the one sentence Jason keeps for it, `host_not_available`. A headless run that ends on a model or agent error
exits with code 3 and prints a line beginning `AGY_ERROR` on standard error, which [the host's changelog](https://antigravity.google/docs/changelog/) also gives; that is an
ordinary end, not a sign-in one.
