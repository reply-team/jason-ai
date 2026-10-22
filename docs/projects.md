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
| `slug` | Made once, unique, never changed; a handle a person types (`jason project get --slug acme-corp`), and the name of the project's folder once there are folders. |
| `default` | Whether this is the project a campaign that names none lands in. Exactly one is; the database holds it to one. |
| `created_at`, `updated_at` | ISO-8601 UTC. |

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
| `project.create` | `name`, `slug?`, `actor?`, `reason?` | The project. `project_exists` (409) when the slug is taken; `validation_failed` for the name or the slug. |
| `project.get` | exactly one of `project_id` and `slug` | The project. `project_not_found` (404); `validation_failed` when neither or both are given. |
| `project.list` | `query?`, `limit?`, `cursor?` | A page of projects, in the order they were made. `query` finds a name by any part of it, whatever its case or accents. |
| `project.update` | `project_id`, `name?`, `actor?`, `reason?` | The project. An absent `name` leaves it alone; the slug never changes, and asking for the name a project already has writes nothing. `project_not_found` (404). |

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
  made it, and the same key sent with another project is another request: `idempotency_key_reused`.
- **`campaign.update`** takes `project_id`, which moves the campaign. A campaign is always in a project, so `null` is
  refused (`validation_failed` on `project_id`); an archived campaign is refused as for every change
  (`campaign_archived`); a project nobody made is `project_not_found`. A move is said once in the campaign's chronicle as
  `campaign_updated` with the key `project_id`, the old and the new project's ids; moving a campaign to the project it is
  in says nothing.
- **`campaign.list`** takes `project_id` and keeps that project's campaigns, read through the index by project; a blank
  one names no project and is refused (`validation_failed` on `project_id`).
- **Every campaign answer** — a campaign and a listing's summary alike — carries `project_id` and `project_slug`, read
  with the campaign, so a listing shown by project needs no second request.

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
  joined by dots, at most 64 characters. A value is a JSON object the key's schema accepts, of at most 64 KiB in its
  canonical form — measured as a role's note is, with every character outside ASCII escaped at six bytes, and so are DEL
  and each of `<`, `>`, `&`, `'`, `"`, `+` and the backtick, while a character outside the Basic Multilingual Plane
  takes twelve, so the size the cap refuses is the size the chronicle reports. A key's schema changes only by gaining an
  optional field, which raises the document's version, so a skill reading the document knows which fields may be
  present; anything else is a new key.
- **A person's word.** Only a person sets or unsets a preference, and the request names them, as a decision on an
  approval does: an actor that is not a person is refused as `preference_not_human`, and one that names nobody as
  `actor_required`. In the planner's session, on the person's answer, the session skill runs
  `jason preference set <key> …` with the person's name as the actor, as approvals are given in a session. A role Jason
  launches reads the preferences and never writes one.

| Operation | Request | Answer and refusals |
|---|---|---|
| `preference.set` | `key`, `value`, `actor`, `reason?` | The preference. `validation_failed` on `key` when the catalog does not know it, naming the keys it does; on `value` when it is missing or not an object, at `value/<pointer>` for each problem the key's schema finds, and as `too_large` past the cap; `preference_not_human`; `actor_required`. The value a key already has — in whatever order its members were written — writes nothing. |
| `preference.unset` | `key`, `actor`, `reason?` | The same answer with `value` null and nothing said about it: the row is deleted, so the value is gone. A key that is not set writes nothing. The same refusals for the key and the actor. |
| `preference.list` | `limit?`, `cursor?` | Every key the catalog knows, in key order, set or not, so a reader learns what is missing in the same call as what is there. |

A preference answers as `key`, `value`, `value_hash` (the `sha256:` of the value's canonical form), `value_bytes`,
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

## 8. What the schema holds for later

- **A folder per project and per campaign.** Both tables have a column for the path of one; nothing writes it yet, and no
  answer carries it.
- **A role's own notes at three levels.** A role note is at one level: the client's, naming no campaign and no project —
  a role's general craft; a project's; or a campaign's. There is one per role at each, and a note never names both a
  campaign and a project: a campaign's project is the campaign's, and it is never stored twice. A reader composes them
  in that order — the client's, then the project's, then the campaign's — and where two say different things the lower
  wins: the project's over the client's, the campaign's over both. Nothing writes a note at the client's or a project's
  level yet; the operations on notes still name a campaign ([execution profiles](execution-profiles.md) describe the
  notes a role keeps).
