# Projects

A project is one business goal of the client — in practice, usually one product — and the campaigns that work toward it.
Every campaign is in exactly one project, always. A project is Jason's own: it never reaches a plugin's input, is never
pinned to a provider, and no provider has to understand it.

There is no client entity. One installation serves one client, so the client's level is the installation itself, and
what a person states about the client as a whole is kept as its named preferences (§7). "Account" keeps the one
meaning it has in Jason: a provider account a route's binding names ([routing](routing.md)).

## 1. The default project

Every installation has one default project. The migration that brought projects in made it on every installation, fresh
or upgraded, named `Default` with the slug `default`, under an id made when the migration ran, so no two installations
share one. Every campaign that existed then was put in it, archived ones included, and every campaign created without
naming a project lands in it since.

The default can be renamed, like any project; its slug stays `default`, and it stays the default — no operation moves
that to another project. Neither the default nor a campaign's place in it has a line in the chronicle: nobody made
either.

Anything that keeps a reference to the default keeps its slug, `default`, and reads the project by it
(`project.get` with `slug`): its id is made on each installation by the migration, so an installation put back from a
backup taken before that migration has another default id once it upgrades again.

## 2. A project

| Field | What it is |
|---|---|
| `id` | `prj_` and a ULID, made when the project is. |
| `name` | At most 200 characters, required. A rename changes it and nothing else. |
| `slug` | Made once, unique, never changed; a handle a person types (`jason project get --slug acme-corp`), and the name of the project's folder (§8). |
| `default` | Whether this is the project a campaign that names none lands in. Exactly one is; the database holds it to one. |
| `created_at`, `updated_at` | ISO-8601 UTC. |
| `folder_path` | The full path of the project's folder (§8) — under the harness root, or one of the person's own that `project.link` recorded; null until one is made — when the project is, or, for a project that existed before folders did, at its first campaign or its link. |

A slug is lower-case ASCII letters and digits, in runs joined by single hyphens, two to forty characters, and never a
name Windows keeps for a device (`con`, `prn`, `aux`, `nul`, `com1`–`com9`, `lpt1`–`lpt9`), which no folder can have
there. Given by the caller, it must already have that shape, or it is refused by the field. Made from the name, the name
is folded the way a campaign search folds it — lower case, accents gone, the few letters that carry no accent spelled as
an English keyboard spells them (`ł` as `l`, `ß` as `ss`) — every run of anything but an ASCII letter or digit becomes
one hyphen, and the result is cut at forty characters, never ending on a hyphen: `Acme Corp` is `acme-corp`,
`Álvaro's Café` is `alvaro-s-cafe`. A name that leaves fewer than two characters — one written in another script, for
instance, or nothing but signs — is named `project-` and the first six hexadecimal characters of the SHA-256 of the
folded name, with each run of white space made one space: `C++` is `project-cedb1b`, and so is `c++`. The same name always makes the same slug, so a create sent again
finds the project the first one made (§3); a different name that lands on the same six characters is refused as
`project_exists` too, and is given a slug of its own. A readable folder name for such a project comes from `--slug`.
A device name has `-project` added.

A project is never deleted or archived.

## 3. The operations

| Operation | Request | Answer and refusals |
|---|---|---|
| `project.create` | `name`, `slug?`, `actor?`, `reason?` | The project, with the folder it was made with (§8). `project_exists` (409) when the slug is taken; `validation_failed` for the name or the slug; `harness_folder_unavailable`, `harness_root_taken` or `harness_folder_taken` (409) when its folder cannot be made, and then no project is made. |
| `project.get` | exactly one of `project_id` and `slug` | The project. `project_not_found` (404); `validation_failed` when neither or both are given. |
| `project.list` | `query?`, `limit?`, `cursor?` | A page of projects, in the order they were made. `query` finds a name by any part of it, whatever its case or accents. |
| `project.update` | `project_id`, `name?`, `actor?`, `reason?` | The project. An absent `name` leaves it alone; the slug and the folder never change, and asking for the name a project already has writes nothing. `project_not_found` (404). |
| `project.link` | `project_id?`, `path?`, `actor?`, `reason?` | Where the project's folder is, and its campaigns' (§8, "Linking a folder"): the project, what became of its folder and of each of its campaigns', and what was found and left as it is. `validation_failed` when neither is given, or for a path that is not absolute, names no folder or is a place no level may be; `project_not_found` (404); `harness_folder_taken`, `harness_folder_present`, `harness_folder_missing`, `harness_folder_unavailable` or `harness_root_taken` (409), and then the database is as it was; §8 says what a disk refusal leaves. |

### Creating again: the slug, not a key

`campaign.create` takes a caller's `idempotency_key`, kept on the campaign it made, because a campaign's name is not
unique: only a key can tell the same create sent again from a second campaign with the same name
([work execution](work-execution.md)). `project.create` takes no key, on purpose: a project's slug is unique, so the
slug is what tells. A create sent again after its answer was lost is refused as `project_exists`, and the refusal names
the project the first one made, in its message and in its details:

```json
{"error":{"code":"project_exists","message":"Project 'prj_…' already has the slug 'acme-corp'; a slug names one project.","retryable":false,
  "details":[{"field":"project_id","code":"exists","message":"prj_…"}]}}
```

The detail's `message` is the project's id itself, the one value the caller acts on. A caller that meant the same
project reads that project (`project.get`), finds the name it asked for, and goes on with it. Two different names can
make the same slug — two long names that agree in their first forty characters, names that differ only in punctuation,
two names in another script whose hashes begin alike — so a caller that finds another name there has met a different
project, and gives its own a slug of its own.

## 4. A campaign's project

- **`campaign.create`** takes `project_id`. Absent, the campaign is in the default project; a project nobody made is
  `project_not_found`, and nothing is written. An installation whose default project is gone — only a hand on the
  database takes it away — answers a campaign that names none with `project_not_found` too, not to be retried, saying
  that there is no default project and to name one with `project_id`. The digest a creation key is held to names
  `project_id` only when the request did, so a key stored before campaigns had projects still matches the request that
  made it, and the same key sent with another project is another request: `idempotency_key_reused`. The campaign is
  made with its folder in its project's (§8), or not at all.
- **`campaign.update`** takes `project_id`, which moves the campaign. A campaign is always in a project, so `null` is
  refused (`validation_failed` on `project_id`); an archived campaign is refused as for every change
  (`campaign_archived`); a project nobody made is `project_not_found`. A move is said once in the campaign's chronicle as
  `campaign_updated` with the key `project_id`, the old and the new project's ids; moving a campaign to the project it is
  in says nothing. The campaign's folder moves with it, or the move is refused with nothing changed (§8).
- **`campaign.list`** takes `project_id` and keeps that project's campaigns, read through the index by project; a blank
  one names no project and is refused (`validation_failed` on `project_id`).
- **Every campaign answer** — a campaign and a listing's summary alike — carries `project_id` and `project_slug`, read
  with the campaign, so a listing shown by project needs no second request, and `folder_path`, the full path of the
  campaign's folder, null for a campaign made before folders were.

Nothing else is read by project yet: approvals, decisions, work items, reports and the chronicle are filtered by
campaign, and a notification names no project — the pointer it carries is counts and campaign ids, and a project's name
never travels for the reason a campaign's never does ([notifications](notifications.md)).

## 5. The chronicle

| Kind | What the line holds |
|---|---|
| `project_created` | Keyed by the project's id; the name and slug it was made with. |
| `project_updated` | Keyed by the project's id; the name before and after. |
| `project_linked` | Keyed by the project's id; the project's folder before and after, and for each campaign whose path a link recorded, its path before and after. A link that records nothing has no line. |

All three are lines of the installation, naming no campaign: a project is not a campaign. All three are the runtime's own, so
`journal.append` refuses them. A project's name is a value somebody gave, so it never reaches a log, and
`jason doctor --include-journal` names "project names as they changed" and "the folders a project link recorded" among
what it carries: a campaign's folder is named from the campaign's name.

## 6. From the command line

One verb per operation, under the noun `project`; `--project` takes a project's id wherever a campaign verb names one.

```sh
jason project create --name "Acme Corp"
jason project get --slug acme-corp --human
jason project list --query acme
jason project update prj_01K7QN0000000000000000ACME --name "Acme Inc" --reason "the client's new name"
jason campaign create --name "Q3 leads" --project prj_01K7QN0000000000000000ACME
jason campaign update cmp_01K7QN000000000000000000Q3 --project prj_01K7QN0000000000000000ACME
jason campaign list --project prj_01K7QN0000000000000000ACME --human
jason project link --path ~/Clients/acme-corp --reason "moved with the other clients"
jason project link prj_01K7QN0000000000000000ACME --path ~/work/acme
jason project link prj_01K7QN0000000000000000ACME
jason campaign list --all-projects
```

`jason campaign list --human` shows each campaign's project by its slug, from the listing it already has.
`jason project link --human` shows what a link did by ids and paths, never a name.

### The project from the folder a command runs in

A verb that takes a project and is given none takes it from the folder the command runs in, the way a person working in
a project's folder means it. The CLI walks up from that folder to the first `.jason/marker.json`: a project's marker
gives that project; a campaign's marker gives the campaign's project as the runtime records it, read once, since a
marker names no parent; the root's marker, or none up to the top of the file system, gives nothing, and the verb does
what it does anywhere else. Every fill is said once on stderr, in one line: the project by its slug and its id, that it
came from the working directory, the folder whose marker said so, and how that verb is given another project — `--project`,
and for `campaign list` also `--all-projects`, or for `project get` an id or `--slug`. `campaign list --human` shows a
listing scoped this way under that project's slug, so a person reading it knows why it holds one project's campaigns; in
JSON mode that line on stderr is what says so. The CLI reads a
marker with the runtime's own reader — one definition of the format for both — and never writes one. A role Jason
launches runs in its attempt's work directory, which carries no marker, so nothing is ever filled for it.

| Verb | Takes a project as | Filled from the folder |
|---|---|---|
| `campaign create` | `--project`, the project the campaign is in | yes; elsewhere it is the default project |
| `campaign list` | `--project`, a filter | yes; `--all-projects` lists every project's campaigns from anywhere |
| `project get` | the id, or `--slug` | yes, when neither is given |
| `campaign update` | `--project`, the project it moves to | never: a move addressed by where a shell stands is how the wrong campaign moves |
| `project update` | the id | never: a rename names its project |
| `project link` | the id | never: with `--path`, the project is the one the marker at that path names; with neither the id nor `--path`, the folder of the nearest project marker above the working directory is linked by its marker; never otherwise |
| `rolenote` | its level | never: a note that names neither a project nor a campaign is the client's, wherever it is written from |

A marker is used only when the folder it is in is the folder the runtime records for what it names, compared where the
paths lead and as the platform compares names. A copied folder, one moved and not yet linked, one naming an id the
runtime does not hold, and one that cannot be read are said on stderr and never used, and the verb goes on as if there
were no marker. A project given on the line or in the request's file is never replaced, and no marker is read for it.
A campaign is never taken from a folder: a write addressed by where a shell happens to stand is how the wrong campaign
gets changed. When the runtime cannot be asked what a marker names, the verb ends as any verb does that cannot reach
the runtime, with exit code 3, rather than going on without its project. A working directory the platform cannot name
— on Linux and macOS, a folder deleted under the shell standing in it — gives nothing, and every verb goes on as it
would outside a project's folder.

`jason project link --path` reads a relative path from the folder the command runs in, and sends it absolute; where
that folder cannot be named, a relative path is a usage error. An empty `--path`, or one of spaces only, names no
folder and is a usage error too, never the working directory.

## 7. The client's named preferences

A named preference is a structured fact about the client that a person states, with a schema of its own, so that a
skill can check that it is set before it writes for a person to send and a screen can show it. The catalog is a contract
family of its own, one document per key under `docs/contracts/preferences/`, embedded into the runtime and read by a
catalog of its own, never the operation catalog: no work item names a key, no route carries one and no plugin is
invoked for one ([the contracts README](contracts/README.md) describes the family). It holds two keys:

| Key | What it says |
|---|---|
| `sender.identity` | Who messages are written as: one or more senders, the first of whom writes whenever a campaign names no other — each with the name they sign with, the forms a message uses for them in a language that marks them, and what else a writer needs. |
| `tone_of_voice` | How the company sounds: the tone in words, how formal it is, how a first message opens and closes, and up to three messages the person likes. |

Each key's page under `docs/contracts/preferences/` gives its fields, and the fixtures beside them are run against the
schema.

- **At the client's level only.** One value per key for the whole installation; there is no project's or campaign's
  value. A project or a campaign that needs another sender or another voice says so in its own words, never as a
  second value here.
- **A key and its value.** A key is lower-case words of letters, digits and underscores, each starting with a letter,
  joined by dots, at most 64 characters. A value is a JSON object the key's schema accepts, of at most 64 KiB of UTF-8
  — measured as a role's note is, as the UTF-8 bytes of its canonical form with nothing escaped, so a character costs
  what UTF-8 spells it in, one byte in ASCII, two or three in most scripts and four outside the Basic Multilingual
  Plane, and the size the cap refuses is the size the chronicle reports. A size written by an earlier version — in the
  chronicle, or in the `value_bytes` of a value not changed since — counted the escaped form, in which every character
  outside ASCII takes six bytes and one outside the Basic Multilingual Plane twelve; a chronicle line written now can so
  carry an `old` size in that measure beside a `new` one in UTF-8. A key's schema changes only by
  gaining an optional field, which raises the document's version, so a skill reading the document knows which fields
  may be present; anything else is a new key.
- **A person's word.** Only a person sets or unsets a preference, and the request names them, as a decision on an
  approval does: an actor that is not a person is refused as `preference_not_human`, and one that names nobody as
  `actor_required`. In the planner's session, on the person's answer, the session skill runs
  `jason preference set <key> …` with the person's name as the actor, as approvals are given in a session. A role Jason
  launches reads the preferences and never writes one.

| Operation | Request | Answer and refusals |
|---|---|---|
| `preference.set` | `key`, `value`, `actor`, `reason?` | The preference. `validation_failed` on `key` when the catalog does not know it, naming the keys it does; on `value` when it is missing or not an object, at `value/<pointer>` for each problem the key's schema finds, and as `too_large` past the cap, naming the value's size and the cap; `preference_not_human`; `actor_required`. The value a key already has — in whatever order its members were written — writes nothing. |
| `preference.unset` | `key`, `actor`, `reason?` | The same answer with `value` null and nothing said about it: the row is deleted, so the value is gone. A key that is not set writes nothing. The same refusals for the key and the actor. |
| `preference.list` | `limit?`, `cursor?` | Every key the catalog knows, in key order, set or not, so a reader learns what is missing in the same call as what is there. |

A preference answers as `key`, `value`, `value_hash` (the `sha256:` of the value's canonical form as it is written, escaped as System.Text.Json's default
encoder writes it: everything outside ASCII, DEL, the control characters and `<`, `>`, `&`, `'`, `"`, `+` and the backtick,
each as `\u00XX`, twelve bytes for a character outside the Basic Multilingual Plane), `value_bytes` (its size in UTF-8,
the number the cap compares once the value has been changed by this version),
`created_at`, `updated_at` and `updated_by`; for a key that is not set, every field but the key is null. `unset` goes
beyond setting and listing on purpose: the sender's identity is personal data, and a person must be able to take it
back.

The chronicle says each change as a line of the installation's, naming no campaign:

| Kind | What the line holds |
|---|---|
| `preference_set` | Keyed by the preference's key; the size and hash of the value before — null where it was not set — and after. |
| `preference_unset` | Keyed by the preference's key; the size and hash of the value that was deleted. |

Never the value itself: the chronicle is append-only and could never forget it, and a size and a hash are enough to
tell a value that changed from one that did not. Both kinds are the runtime's own, so `journal.append` refuses them;
`Manager:Triggers` may name them, and, naming no campaign, they summon nobody. A value never reaches a log, and
`jason doctor --include-journal` carries nothing of one, because no line holds it.

```sh
jason preference list --human
jason preference set tone_of_voice --value '{"summary":"Plain and warm; no exclamation marks."}' --actor human:Marta
jason preference set sender.identity --value-file sender.json --actor human:Marta --reason "the person's answer"
jason preference unset sender.identity --actor human:Marta
```

The value is given on the line or from a file — `-` reads standard input — never both. `--human` renders each key
with its value from the answer it already has.

## 8. The folders

What a person tells Jason about their business in their own words is kept in plain files they can read and edit: a
memory file, `AGENTS.md`, for the client, for each project and for each campaign, each in a folder of its own, nested in
that order under the harness root. Each level carries, beside its `AGENTS.md`, a one-line import file for every host
that reads a file of another name — `CLAUDE.md` for Claude Code and `GEMINI.md` for Gemini CLI (the [host matrix](execution-profiles.md#12-the-hosts)
lists them). A folder made before a host's import file existed has the files it had: the root gains a missing import file at the next project or campaign create, and a project's or a campaign's folder keeps what it has until the person adds the file. Claude Code loads the `CLAUDE.md` of the folder a session starts in and of every folder above it; but in
print mode it drops, without asking, an import of a file outside the folder it started in, so a session started in a
campaign's folder is given the
campaign's file and neither the project's nor the client's (measured with Claude Code 2.1.309; its documentation says
an interactive session asks once whether to allow such imports). The session skills therefore read the root's and the project's files by path,
and a role the runtime launches reads all three by the paths its launch envelope gives. A memory file is the person's
guidance, never a fact the runtime holds: the runtime never reads one, and where one disagrees with what the runtime
records, the record wins.

```text
~/Jason/                                  the root: the client's level, Harness:Root
  AGENTS.md                               the client's memory file
  CLAUDE.md                               @AGENTS.md
  GEMINI.md                               @AGENTS.md
  .jason/marker.json                      {"marker_version":1,"kind":"root"}
  projects/
    acme-corp/                            a project's folder, named by its slug
      AGENTS.md
      CLAUDE.md
      GEMINI.md
      .jason/marker.json                  {"marker_version":1,"kind":"project","id":"prj_…"}
      campaigns/
        q3-marketing-leads/               a campaign's folder, named from its name
          AGENTS.md
          CLAUDE.md
          GEMINI.md
          .jason/marker.json              {"marker_version":1,"kind":"campaign","id":"cmp_…"}
```

### The root

| Setting | Default | What it does |
|---|---|---|
| `Harness:Root` | `Jason` in the home directory | The folder the levels are made in, as an absolute path. |

The root is named in `settings.json` (`{"Harness":{"Root":"…"}}`) or by `JASON_Harness__Root`, like every other setting.
It is an absolute path, starting at a drive, a share or `/`, since a relative one would mean wherever the runtime
happened to start; `~` is a shell's word that the runtime does not expand. A `CLAUDE.md` or `GEMINI.md` at the root is read by every
agent session of that host started anywhere below it, so the root is never the home directory itself, a folder that holds it, or the
top of a drive, a share or the file system; and the data directory is the runtime's own, so the root is never the data
directory, a folder inside it or a folder that holds it. Each is compared where it leads, not as it is spelled: a
junction, a symbolic link or a Windows device prefix such as `\\?\` does not make the home or the data directory
another folder, and Windows and macOS ignore case as their file systems do. A root refused for any of these stops the
start, naming `Harness:Root` and never the value, as any setting that is wrong does; an edit refused while the runtime
runs keeps the root it had and says so in its log. Left out, the root is `Jason` in the home directory, beside the data
directory `~/.jason`; a data directory moved by `JASON_DATA_DIR` onto that very folder, into it or around it has the
default refused the same way, saying to set `Harness:Root`.

The runtime says the root it uses, so nothing else works it out: `system.info` carries `harness`, with `root`, the root
in force — the last value of the setting that validated — and `root_state`, what stands there as the next create will
meet it: `absent` (made then), `marked`, `unmarked` (taken as the root then, with nothing in it changed), `taken` (a
marker of something else: refused as `harness_root_taken`), `not_a_folder` or `unreadable` (a folder this account
cannot list, or whose marker it cannot read; both refused as `harness_folder_unavailable`). `jason runtime status` shows
it, and `jason status` has a check of it, `harness_root`, which never decides readiness and fails where the runtime can
tell that the next create would be refused, and no more: what the disk refuses only once a write is tried — a folder
that can be read and not written, say — is said by that create. A runtime older than the report answers no `harness`,
and the CLI says the root is unknown.

The root is read at every create and every move, so an edit applies to the next one. It moves nothing that was made and
changes no path that was recorded: a project whose folder is recorded keeps having its campaigns' folders made there,
wherever that is, and only a project with none gets one under the root as it is then.

### Made at the first need

Nothing is asked before the first folder is made. The first `project.create` or `campaign.create` makes the root if it
is not there, and the folders the request needs:

- **The root.** Made with its marker, an `AGENTS.md` and an import file for every host that reads a file of another name.
  A folder already there with no marker is taken as the root by writing its marker, and `AGENTS.md` and each import file
  where it has none; nothing it holds is changed. A
  folder whose marker names anything but a root — a project's folder, a campaign's, a marker this runtime cannot read —
  is refused as `harness_root_taken`, and nothing is written into it.
- **A project's folder**, `projects/<slug>/`, made by `project.create`, and by `campaign.create` or a campaign's move for
  a project that has none recorded — the default project of an installation that had campaigns before it had folders,
  for instance. A project's folder made for a campaign, at its create or its move, is made and recorded in a step of its
  own that commits before the campaign's, so it stands, and is recorded, when the campaign's own folder is then refused.
- **A campaign's folder**, `campaigns/<leaf>/` in its project's folder, made by `campaign.create`. The leaf is made from
  the campaign's name the way a slug is made from a project's (§2): `Q3 Marketing Leads` is `q3-marketing-leads`; a name
  that leaves fewer than two characters is `campaign-` and six hexadecimal characters, and a device name has `-campaign`
  added. A campaign has no slug, and only the path is recorded: campaigns are addressed by id, and their names are not
  unique.

A folder already at a level's path is a fact the person must see, never one Jason walks around:

- **With no marker**, it becomes the level's as the root does: its marker is written, and `AGENTS.md` and each import file
  where it has none; not a word of what it holds changes.
- **Marked for this very project or campaign** — left by a create that was refused after it made it — it is used again.
- **Marked for another campaign this runtime holds**, at a campaign's leaf, the leaf is followed by `-2`, `-3` and so
  on, cut where needed so it stays within forty characters: campaign names are not unique. A project's folder is never
  numbered: its slug is.
- **Marked for anything else** — a level of another kind, another project, an id no project or campaign here holds, a
  marker this runtime cannot read — it is refused as `harness_folder_taken`, naming the path, and nothing is written.
  The person renames or moves it; Jason never deletes it.
- **A file** where a folder belongs is in the way, never taken for nothing: `harness_folder_unavailable`.

Inside the create's transaction the row is written first, then the folders — each level's marker first, naming the
row's id, so a folder whose path another marker reached first has nothing written in it, and its two files after it —
and then the transaction commits, so a folder step that fails — the account may not write there, a file stands where a
folder belongs — rolls the row and its line in the chronicle back. A caller that goes away once the folders stand does
not take the create back; it is finished, and the request sent again answers what it made. A step that fails after a
level's marker, and a commit that fails after the folders stand, leave a folder whose marker names an id no row holds,
and the next create that reaches its path names it as `harness_folder_taken`. A write the disk stops halfway leaves its
file as far as it got, and the next attempt meets that file rather than writing it again: a marker cut short reads as no
marker of Jason's, so its folder is refused as `harness_folder_taken` — at the root, `harness_root_taken` — naming it,
until the person removes that marker.

A campaign for a project whose recorded folder is no longer there, or no longer carries the project's marker, is
refused as `harness_folder_missing`, and its folder is not made again beside the moved one, which would leave the
person's words in one folder and the work in another. The refusal names the repair that works at once — move the folder
back to the recorded path — and then `jason project link`, which records where a moved folder is now. A create sent
again under its `idempotency_key` answers the campaign it made and makes no second folder.

The path of the folder made is recorded on the project or the campaign, and every answer that carries one carries it as
`folder_path`, null where none is recorded.

### The marker

A folder is Jason's by its marker, `.jason/marker.json`, and never by its name. The marker says the format's version, the
level and, below the root, the id of the project or campaign the folder belongs to — no name, since names change and the
runtime holds them, and no parent, since a copy of which project a campaign is in would drift from the record that says
so. The data directory `~/.jason` has no marker and is never taken for a level.

### What the runtime never does

It writes structure only: a directory, its marker, and, where none is there, an import file for every host that reads a file of another name, each holding exactly its one line (`@AGENTS.md`),
and an empty `AGENTS.md`. Then it leaves them to the person. The import files are `CLAUDE.md` for Claude Code and `GEMINI.md` for Gemini CLI.

- It never reads, writes or deletes a word of any `AGENTS.md`, and never writes over an import file that is there, the
  person's own included: whether one is there is asked of its name, and it is never opened.
- It never deletes a folder or a file. Archiving a campaign leaves its folder where it is.
- It never runs `git` and never writes a `.gitignore`: the root is a repository only if the person makes it one.
- A rename changes the name and nothing else; the folder keeps its name, since a session may have it open and a person's
  own shortcuts point at it.
- It writes nothing outside the three levels: `projects/` and `campaigns/` are plain directories, with no marker and no
  files of their own.

Nor does the CLI touch the root: `jason uninstall` removes nothing under it, `--purge-data` included, since the purge
empties the data directory and never the root; and `jason doctor` lists and opens nothing in it, so no memory file's
name or word reaches a bundle.

### A campaign changing project

`campaign.update` with `project_id` moves the campaign's folder into the new project's `campaigns/` by one rename, in the
same request, under the name it had there — `-2`, `-3` where another campaign this runtime holds has that name there.
The project it moves to gets its own folder first when it has none recorded. A project whose folder is one of the
person's own takes the move the same way: the campaign's folder is the policy's, the project's is theirs.

When the rename cannot be done, the whole update is refused: the campaign's folder and every row stay as they were. The
project it moves to keeps the folder made for it as its first need, recorded as any first need's is, and its
`campaigns/`: those are the project's own structure, not the campaign's.

- `harness_folder_unavailable` — a program holds a file in the folder open or is working in it, as an agent session
  started there is, the account may not write in one of the two folders, a file stands where `campaigns/` or the folder
  belongs, or the two are on different volumes. A folder is never copied and deleted instead.
- `harness_folder_taken` — any other folder stands at the name in the new project's `campaigns/`, marked or not: a
  rename never lands on a folder that is there.
- `harness_folder_missing` — the campaign's folder is not where it was recorded, or the new project's is not.

A campaign with no folder recorded — one made before folders were — moves in the database alone, and its folder is made
by `jason project link` on its project.

### Linking a folder

A folder the person moves or renames leaves a path recorded where it no longer is; one of their own they want a project
to use has none. `project.link` (`jason project link`) records where it is, the one act by which the filesystem changes
a fact the runtime holds, and that fact is only ever a recorded path. A rename never needs it: a folder keeps its name.

A path given is taken by the rule a folder already at a level's path meets (above), applied to that path:

- **With no marker** — a folder of the person's own — it is **adopted**: its marker is written, and an `AGENTS.md` and each import file
  where there are none; not a word of what it holds changes, and an import file of the person's own does not
  import `AGENTS.md` until they make it. `project_id` names the project it becomes.
- **Marked for this project** — the folder moved — it is **relinked**: the path is recorded for the project again.
  Without `project_id`, the project is the one the marker names; with it, the marker must name that project.
- **Marked for anything else** — another project, another level, an id this runtime does not hold, a marker that cannot
  be read — or the folder another project records, it is **taken**: refused as `harness_folder_taken`, and nothing is
  written.

Another path for a project whose recorded folder is still there with its marker — a copy, most often — is refused as
`harness_folder_present`, naming both: a link records where a folder went, and never moves, copies or deletes one. A
recorded folder that is there with no marker is missing, by the rule above: a link to another folder goes ahead, and
the old one stays the person's, untouched. The path that is recorded already is the project's folder looked through
again, never a refusal.

**A project alone**, with no path, is the one form that can answer **made**: where the project has no folder recorded —
the default project of an installation that had campaigns before it had folders — it gets its own under the root, as
its first need does at a create. Where one is recorded and is there with its marker, that folder is looked through
again; one that is gone is not made again beside it: `harness_folder_missing`, until a path says where it went.

A path is absolute, names a folder that is there — a link never makes the folder it is named — and is never one of the
places the root may not be: the home directory, a folder holding it, the top of a drive, the data directory, a folder
inside it or one holding it. It is recorded as the platform writes it, without a Windows device prefix such as `\\?\`,
which names the same folder. Nor is it the root, a folder holding the root or the root's `projects/`, refused by the field
as those are; and a folder at, inside or holding one the runtime records for another project or for any campaign, or
below a folder with a project's or a campaign's marker — decided by the recorded paths and a walk up from the path,
never by a search below it or by a folder's name — is refused as `harness_folder_taken`, naming that folder, since a
session started in one would read the other's memory files.

The link then reads two fixed levels, never a search of the disk: the project's folder, and each folder directly in its
`campaigns/`, by its marker. The database decides which project a campaign is in, never the folder it is found in.

| Below `campaigns/` | What the link does |
|---|---|
| A folder marked for a campaign of this project, somewhere other than recorded | Records that path for it. |
| The folder recorded for a campaign of this project | Nothing: it is where it is recorded. |
| Two or more folders marked for one campaign, none of them the recorded one | Nothing: the recorded path stays, and each folder is reported. |
| A folder marked for a campaign the runtime puts in another project | Nothing: it is reported, with both ids. |
| A folder marked for an id nobody holds, for another level, or with a marker that cannot be read | Nothing: it is reported. |
| A folder with no marker | Nothing, and it is not mentioned: it is the person's. |

A campaign of the project found nowhere there keeps the path it had and is reported as not found, a marker one level
deeper included. One with no folder recorded gets one made by the policy, unless it is archived: an archived campaign is
relinked like any other where its folder is found, and none is ever made for it. A campaign's folder that cannot be made
refuses the link whole, as a create's does.

The answer is the project as it is now, what became of its folder — `relinked`, `adopted`, `made` or `kept` — every
campaign of the project with the folder now recorded for it and what became of it — `relinked`, `kept`, `made`,
`not_found`, `duplicate` or `archived` — and the folders found and left as they are, each with its reason —
`another_project`, `unknown` or `duplicate` — by id and path, never a name. It lists every campaign the project has and
is not paged. The checks that refuse run before anything is written; the disk's acts then come inside the link's
transaction, each level's marker first, and every path and one `project_linked` line are saved together, so a disk
refusal takes the database back. A link taken back leaves the folders it marked marked for this very project or
campaign, and the next attempt uses them again. A link that records nothing writes no line.

A moved root is a change of `Harness:Root` and then a link of each project whose folder moved with it: nothing moves by
itself.

### The refusals

| Code | Status | When |
|---|---|---|
| `harness_folder_unavailable` | 409 | A folder could not be made or moved: the account may not write there, a file stands where a folder belongs, a file in it is held open or a program is working in it, or the two folders are on different volumes. The project or campaign the request is about, its row and its folder, is as it was; once what is in the way is repaired, the same request can be sent again. |
| `harness_root_taken` | 409 | The folder at the root is marked as something other than the root. Nothing was written into it; set `Harness:Root` to another folder, or move that one away. |
| `harness_folder_taken` | 409 | A folder already stands at the path a project's or a campaign's folder takes and is marked for something else — another level, another project, an id nobody here holds — or, for a move, any folder stands there; or, for a link, the folder named is marked for anything but the project, is the folder another project records, or is at, inside or holding another project's or a campaign's folder, or below a project's or a campaign's marker. Nothing was written into it, and the project or campaign the request is about is as it was; rename or move that folder away, or name another, then send the request again. |
| `harness_folder_missing` | 409 | The folder recorded for the project — at a create, a move, or a link of the project alone — or for the campaign being moved, is not there or no longer carries its marker. The project or campaign the request is about is as it was; move the folder back to the recorded path, or, for a project's, record where it is now with `jason project link`, and for a campaign's, relink its project, which finds a campaign's folder by its marker. |
| `harness_folder_present` | 409 | A link named a folder for a project whose recorded folder is still there and still carries its marker. Nothing was written; a link records where a folder went and never moves or copies one, so the person removes or renames one of the two, then links again. |

Each message names the folder, so the person who reads it knows where to look. No folder's path reaches the runtime's
log: a campaign's folder is named from the campaign's name.

## 9. A role's notes, by project

A role keeps its own notes at three levels, one per role at each: the client's, which is its general craft for all of
this installation's work and names neither a campaign nor a project; a project's; and a campaign's
([execution profiles](execution-profiles.md) describe the notes a role keeps). A note never names both a campaign and a
project: a campaign's project is the campaign's, and it is never stored twice. A reader composes them in one order — the
client's, then the project's, then the campaign's — and where two say different things the lower wins: the project's
over the client's, the campaign's over both. The runtime composes nothing; it answers each level on its own.

```sh
jason rolenote set --role copywriter --project prj_01K7QN0000000000000000ACME --note '{"voice":"short sentences; the product named once"}'
jason rolenote get --role copywriter --project prj_01K7QN0000000000000000ACME
jason rolenote list --project prj_01K7QN0000000000000000ACME --human
```

A project's note is said in the chronicle as a `role_note_set` line of the installation that names the project, and the
client's as one that names neither; like a campaign's, it says the note's size and hash and never a word of it. Naming no
campaign, neither summons a review, whatever `Manager:Triggers` names.
