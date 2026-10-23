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
| `folder_path` | The full path of the project's folder under the harness root (§8); null until one is made — when the project is, or, for a project that existed before folders did, at its first campaign. |

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

Both are lines of the installation, naming no campaign: a project is not a campaign. Both are the runtime's own, so
`journal.append` refuses them. A project's name is a value somebody gave, so it never reaches a log, and
`jason doctor --include-journal` names "project names as they changed" among what it carries.

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
```

`jason campaign list --human` shows each campaign's project by its slug, from the listing it already has.

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
that order under the harness root. Claude Code, started in a campaign's folder, reads the three by itself through each
level's `CLAUDE.md`, the client's first and the campaign's last. A memory file is the person's guidance, never a fact the
runtime holds: the runtime never reads one, and where one disagrees with what the runtime records, the record wins.

```text
~/Jason/                                  the root: the client's level, Harness:Root
  AGENTS.md                               the client's memory file
  CLAUDE.md                               @AGENTS.md
  .jason/marker.json                      {"marker_version":1,"kind":"root"}
  projects/
    acme-corp/                            a project's folder, named by its slug
      AGENTS.md
      CLAUDE.md
      .jason/marker.json                  {"marker_version":1,"kind":"project","id":"prj_…"}
      campaigns/
        q3-marketing-leads/               a campaign's folder, named from its name
          AGENTS.md
          CLAUDE.md
          .jason/marker.json              {"marker_version":1,"kind":"campaign","id":"cmp_…"}
```

### The root

| Setting | Default | What it does |
|---|---|---|
| `Harness:Root` | `Jason` in the home directory | The folder the levels are made in, as an absolute path. |

The root is named in `settings.json` (`{"Harness":{"Root":"…"}}`) or by `JASON_Harness__Root`, like every other setting.
It is an absolute path, starting at a drive, a share or `/`, since a relative one would mean wherever the runtime
happened to start; `~` is a shell's word that the runtime does not expand. A `CLAUDE.md` at the root is read by every
agent session started anywhere below it, so the root is never the home directory itself, a folder that holds it, or the
top of a drive, a share or the file system; and the data directory is the runtime's own, so the root is never the data
directory, a folder inside it or a folder that holds it. Each is compared where it leads, not as it is spelled: a
junction, a symbolic link or a Windows device prefix such as `\\?\` does not make the home or the data directory
another folder, and Windows and macOS ignore case as their file systems do. A root refused for any of these stops the
start, naming `Harness:Root` and never the value, as any setting that is wrong does; an edit refused while the runtime
runs keeps the root it had and says so in its log. Left out, the root is `Jason` in the home directory, beside the data
directory `~/.jason`; a data directory moved by `JASON_DATA_DIR` onto that very folder, into it or around it has the
default refused the same way, saying to set `Harness:Root`.

The root is read at every create and every move, so an edit applies to the next one. It moves nothing that was made and
changes no path that was recorded: a project whose folder is recorded keeps having its campaigns' folders made there,
wherever that is, and only a project with none gets one under the root as it is then.

### Made at the first need

Nothing is asked before the first folder is made. The first `project.create` or `campaign.create` makes the root if it
is not there, and the folders the request needs:

- **The root.** Made with its marker, a `CLAUDE.md` and an `AGENTS.md`. A folder already there with no marker is taken
  as the root by writing its marker, and `CLAUDE.md` and `AGENTS.md` where it has none; nothing it holds is changed. A
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

- **With no marker**, it becomes the level's as the root does: its marker is written, and `CLAUDE.md` and `AGENTS.md`
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
and the next create that reaches its path names it as `harness_folder_taken`.

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

It writes structure only: a directory, its marker, and, where none is there, a `CLAUDE.md` holding exactly `@AGENTS.md`
and an empty `AGENTS.md`. Then it leaves them to the person.

- It never reads, writes or deletes a word of any `AGENTS.md`, and never writes over a `CLAUDE.md` that is there, the
  person's own included: whether one is there is asked of its name, and it is never opened.
- It never deletes a folder or a file. Archiving a campaign leaves its folder where it is.
- It never runs `git` and never writes a `.gitignore`: the root is a repository only if the person makes it one.
- A rename changes the name and nothing else; the folder keeps its name, since a session may have it open and a person's
  own shortcuts point at it.
- It writes nothing outside the three levels: `projects/` and `campaigns/` are plain directories, with no marker and no
  files of their own.

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
when `jason project link` makes its project's.

### The refusals

| Code | Status | When |
|---|---|---|
| `harness_folder_unavailable` | 409 | A folder could not be made or moved: the account may not write there, a file stands where a folder belongs, a file in it is held open or a program is working in it, or the two folders are on different volumes. The project or campaign the request is about, its row and its folder, is as it was; once what is in the way is repaired, the same request can be sent again. |
| `harness_root_taken` | 409 | The folder at the root is marked as something other than the root. Nothing was written into it; set `Harness:Root` to another folder, or move that one away. |
| `harness_folder_taken` | 409 | A folder already stands at the path a project's or a campaign's folder takes and is marked for something else — another level, another project, an id nobody here holds — or, for a move, any folder stands there. Nothing was written into it, and the project or campaign the request is about is as it was; rename or move that folder away, then send the request again. |
| `harness_folder_missing` | 409 | The folder recorded for the project, or for the campaign being moved, is not there or no longer carries its marker. The project or campaign the request is about is as it was; move the folder back to the recorded path, or, for a project's, record where it is now with `jason project link`, and for a campaign's, relink its project, which finds a campaign's folder by its marker. |

Each message names the folder, so the person who reads it knows where to look. No folder's path reaches the runtime's
log: a campaign's folder is named from the campaign's name.

## 9. What the schema holds for later

- **A role's own notes at three levels.** A role note is at one level: the client's, naming no campaign and no project —
  a role's general craft; a project's; or a campaign's. There is one per role at each, and a note never names both a
  campaign and a project: a campaign's project is the campaign's, and it is never stored twice. A reader composes them
  in that order — the client's, then the project's, then the campaign's — and where two say different things the lower
  wins: the project's over the client's, the campaign's over both. Nothing writes a note at the client's or a project's
  level yet; the operations on notes still name a campaign ([execution profiles](execution-profiles.md) describe the
  notes a role keeps).
