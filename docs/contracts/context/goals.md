# `goals` — what a campaign is for

**Version 1.** The machine-readable document is [`goals.json`](goals.json); this page explains it. Where the two seem
to differ, the document is right and this page is a bug.

> **A reserved key of a campaign's context, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/) and never among them: no work item names it, no route carries it, and no plugin is
> invoked for it. It is one key of one campaign's context, which the runtime holds to this document whenever the key is
> written; every other key of a context stays the caller's own, and the runtime never reads it.

## What it is for

The outcomes the campaign's roles steer each conversation towards, settled with the person when the campaign is
planned: a demo, a meeting, a trial, a purchase, a reply that says the person is interested, or anything else in the
person's own words. Several are allowed, in the order they are worth trying, and each says when it counts as reached,
so a role reading a reply knows which goal it moves the conversation towards and the person can later read how often
each was reached.

A campaign with no goals is valid. The key absent, a JSON `null` under it and an empty list all mean the same: no goal
has been settled, and the roles take the default goal, a demo.

## The value

```json
[
  { "id": "demo", "kind": "demo", "title": "A demo with the operations lead", "reached_when": "a demo is on their calendar", "order": 1 },
  { "id": "trial", "kind": "trial", "title": "A two-week trial", "reached_when": "their team has signed up for the trial", "order": 2 }
]
```

- `id` names the goal within the campaign: a lower-case word of letters, digits and underscores, starting with a
  letter. No two goals of a campaign share one. A verdict on a reply names the goal it steers to by this id, so an id
  keeps naming the same goal for the campaign's life: a goal whose title or wording is edited keeps its id, and a goal
  that means something else is a new goal with a new id. A list that leaves out an id a verdict once named is
  accepted as written: the verdict's line copied the goal's id and kind when it was recorded, so it still reads
  without the list, and only a later verdict is held to the goals as they then stand. A reader of an old line relies
  on the line's `goal_kind`, never on a join to the current goals by id.
- `kind` is one of `meeting`, `demo`, `trial`, `interested_reply`, `purchase` and `custom`; `custom` is the kind for
  a goal the list does not name, which its title and `reached_when` then say.
- `title` names the goal for a person reading a list of them.
- `reached_when` is the moment the goal counts as reached, in the person's words.
- `order` is the order the goals are worth trying in, a whole number from 1; a lower order comes first, and two goals
  may share one.

## Writing it

The goals are written with the rest of the context: whole, by `campaign.create`, or as one top-level key of
`campaign.update_context`, which replaces the list and never merges into it. Either is refused, and nothing is written,
when the value breaks the document: `validation_failed`, with each problem at the field `context.goals` followed by
the problem's pointer — `context.goals/0/kind` — and the code `invalid`, the reason in the message. A patch that does
not name the key never checks it again, so goals written before this document changed are read as they stand.
`--unset goals` removes them.

```sh
jason campaign create --name "Spring outreach" --context '{"goals":[{"id":"demo","kind":"demo","title":"A demo with the operations lead","reached_when":"a demo is on their calendar","order":1}]}'
jason campaign update-context cmp_01HZY3M4N5P6Q7R8S9T0V1W2X3 --set '{"goals":[]}'
jason reply record cmp_01HZY3M4N5P6Q7R8S9T0V1W2X3 --plugin mailbox --thread thr-1042 --message msg-77 --message-at 2026-05-04T09:30:00Z --category interested --confidence 0.9 --reaction draft --goal demo --reason "interested by the table; draft toward the demo"
```

The last line is a verdict on one reply that names the goal by its id; the runtime refuses an id the campaign's goals
do not carry ([`../../runtime-api.md`](../../runtime-api.md), `reply.record`). Its reason is the actor's reason for the
verdict, never the reply's words.

## The fields

<!-- BEGIN GENERATED properties -->
<!-- Rendered from goals.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| key | `goals` |
| version | 1 |
| value | a list of at most 20, each an object whose fields follow |
| items told apart by | `id`, which no two items share |

| Field | Required | What it holds |
|---|---|---|
| `goals[].id` | yes | text, matching `^[a-z][a-z0-9_]{0,63}$` |
| `goals[].kind` | yes | one of `meeting`, `demo`, `trial`, `interested_reply`, `purchase`, `custom` |
| `goals[].title` | yes | text of 1 to 200 characters |
| `goals[].reached_when` | yes | text of 1 to 2000 characters |
| `goals[].order` | yes | a whole number from 1 to 100 |

<!-- END GENERATED properties -->

The whole context, goals included, is also bounded by the campaign context's own cap of 256 KiB.

The fixtures under [`../fixtures/context/goals/`](../fixtures/context/goals/) are run by the repository's tests against
this document, so an example on this page and the schema cannot drift apart.
