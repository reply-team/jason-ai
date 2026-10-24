# `events.read` — read what happened in one account

**Version 1.** The machine-readable contract is [`events.read.json`](events.read.json); this page explains it. Where
the two seem to differ, the document is right and this page is a bug.

> **An events contract, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/) and the delivery contract under [`../notifications/`](../notifications/), and never
> among either: no work item names it, no route carries it, and no approval, preflight or repeat rule applies to it. A
> read of what happened in an account belongs to no campaign. A provider plugin is invoked for it only by the runtime's
> own reader, and only when its manifest declares `contracts.events: [1]`.

## What it is for

Jason learns what happened at a provider — a reply came in, an invitation was accepted, a message bounced — by reading
it, on a cadence the runtime keeps, from each account a route binds. One read asks for everything after the point the
plugin's own cursor names, a page at a time. What it reads is kept by the runtime as it was read, and a campaign hears
of an event only through the provider identifiers that the operations of that campaign pinned or, where that finds none,
by the contact pinned at the plugin, through every campaign that is not archived and holds them as a member. A machine
that was off loses nothing: the cursor waits.

What travels is identifiers, moments and the provider's own flags. Nothing anybody wrote does: no subject, no preview,
no message's text, no name.

## The input you receive

```json
{ "cursor": "c-41", "since": null, "until": null, "limit": 100 }
```

Every field is always there, `null` included. A read starts from exactly one of two places:

- **a cursor** — one your plugin returned with an earlier page, handed back exactly as you returned it; `since` and
  `until` are then `null`;
- **a moment** — `since`, with `until` either `null` (up to now) or the end of a range. The runtime reads by date when
  it has no cursor yet, when the one it has is too old to follow, when you said you can no longer read it, and when a
  person asked for a range.

`limit` is at most 100. The invocation's `context` carries the route's `binding` — which account to read — and no
campaign, work item or attempt.

## What you answer

```json
{
  "events": [
    { "kind": "thread_moved", "id": null, "occurred_at": "2026-10-01T09:00:00.000Z", "thread": "t_501",
      "contact": "c_1001", "campaign": "p_1", "moved_by": "other_side", "channel": "email", "unread": true,
      "has_meeting_intent": null }
  ],
  "cursor": "c-42",
  "has_more": false
}
```

- **`events`** — at most `limit`, in the provider's own order. A page may come back shorter than `limit` with
  `has_more: true`: the runtime refuses a result above 1 MiB, so stop filling a page before it would pass 768 KiB. One
  event always fits.
- **`cursor`** — always. Handed back, it yields the events after this page. When `has_more` is `false`, it is where the
  next read starts, minutes or months later.
- **`has_more`** — whether the provider holds more after this page, to be read now with the cursor.

### The cursor is yours

It is a string of 1 to 2048 printable ASCII characters with no space, and nothing but your plugin ever reads it. The
runtime stores it as you returned it, beside the account it was read from, hands it back unchanged, and never parses,
logs or shows it. Put in it whatever you need to continue: a provider's own feed position, an offset and a moment, a
version of your own format. When you can no longer read one — your own older format, a provider that reset its feed —
answer `events_cursor_unknown`, and the runtime reads again by date.

### Reading by date

`since` is the depth. Across the pages your cursors chain, return every event at or after `since` and, where `until` is
given, before `until`. A provider that pages only by offset is paged newest first until the last item's moment passes
`since`, with the offset and the moment the read began carried in your cursor, so that an event that happens while you
page is read the next time rather than skipped. A provider that holds less history than `since` asks for answers what
it holds; that is not a failure.

## The four kinds

Every event has a `kind`, an `occurred_at` — when it happened, as the provider stamps it — and an `id`, the provider's
own identifier for the event or `null`. Every identifier is 1 to 256 printable ASCII characters with no space. A moment
is written in UTC with `Z`, to the second or to a fraction of it of at most seven digits — `2026-10-01T09:00:00.120Z` —
never with an offset, and never at a leap second's `60`: convert the provider's stamp before you answer.

| `kind` | Identifiers | The provider's flags |
|---|---|---|
| `thread_moved` | `thread`, `contact`, `campaign` | `moved_by`, `channel`, `unread`, `has_meeting_intent` |
| `reply_detected` | `contact`, `campaign`, `thread` | `channel` |
| `invitation_accepted` | `contact`, `campaign`, `account` (the provider's identifier for the account that sent it) | — |
| `bounce` | `contact`, `campaign`, `message` | `bounce`: `hard`, `soft` or `null` |

`thread` on a moved thread and `contact` on the other three are required; every other identifier is `null` where the
event names none. `channel` is `email`, `linkedin` or `other`.

**Who moved a thread.** A thread moves when the other side writes, and also when the account itself acts — a reply sent
into it, a category set, the read flag changed — which Jason does too. `moved_by` is the provider's own word for the
move:

- `other_side` — a message the other side wrote;
- `account` — a message sent as the account, by Jason, by a sequence or by a person in the provider's own screens;
- `provider` — a change with no message: a category, the read flag, a meeting intent;
- `null` — the provider cannot say.

Only a move by the other side reads as a reply. Never report a move by the account or the provider as `other_side`.
Report `null` only where the provider truly cannot say: such a move wakes the campaign's manager as a reply would, so a
follow-up sent into a thread that already has a reply reads as one too. You may leave out moves by the account and the
provider altogether.

**Nothing else travels.** Each kind is a closed object: a member the table does not name — a subject, a preview, a
person's name — is refused with the whole page, and so is a kind outside the four. A schema cannot prove that an
identifier is not a name; returning only what the provider uses to identify things is what this contract asks of you.

**A read pins nothing.** The runtime finds a campaign by the identifiers the operations of that campaign pinned. An
answer that carries `external_ids` beside its result is refused.

## What happens to the answer

A page the output schema accepts is kept whole, each event once however often it is read — two moves of one thread at
one moment by two movers are two events, so report each. One the schema refuses is kept not at all, and the cursor
stays where it was. A failure is one of the four codes below:

- **`transient`**, and an ambiguous end — a timeout, a crash, an answer outside the contract — is read again later;
- **`permanent`** is read again only after a day, except `events_cursor_unknown`, after which the runtime reads by date
  at once.

A read changes nothing at a provider, so reading again is always safe.

## The properties

<!-- BEGIN GENERATED properties -->
<!-- Rendered from events.read.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| version | 1 |
| input | `cursor`, `since`, `until`, `limit` |
| output | `events`, `cursor`, `has_more` |
| kinds | `thread_moved`, `reply_detected`, `invitation_accepted`, `bounce` |
| page | at most 100 events |
| timeout | 60000 ms |

| Failure code | Class | When |
|---|---|---|
| `events_unreachable` | `transient` | the provider did not answer, or answered that it cannot be read for now |
| `events_unauthorized` | `permanent` | the provider refused the account's credential, or the account may not read its events |
| `events_unsupported` | `permanent` | the account has no feed of events to read |
| `events_cursor_unknown` | `permanent` | the cursor handed back is one the provider no longer knows, or one this plugin can no longer read |

<!-- END GENERATED properties -->

## Conformance

A plugin that performs this contract:

- returns identifiers, moments and the provider's own flags, and nothing else: never a subject, a preview, a message's
  text or a name;
- returns a cursor with every page; handed back, it yields the events after that page, and nothing else reads it;
- read by date, returns every event at or after `since` and before `until` across the pages its cursors chain, pages
  an offset-only provider newest first until the last item's moment passes `since`, and answers what a provider holds
  when it holds less;
- reports a thread moved by `other_side` only for a message the other side wrote, never a move by the account or the
  provider;
- names a thread that moved by the identifier `conversation.list` returns as the thread's `external_id`, with
  `occurred_at` the moment the thread last moved — the moment `conversation.list` reports as its `last_activity_at`, to the
  millisecond — so that a move both reads find is said once;
- names, under `campaign`, the provider campaign a moved thread belongs to wherever the provider keeps one, by the
  identifier `campaign.get` pins; `null` is for a thread outside every provider campaign, a move that reaches, by the
  person, every campaign that is not archived and holds them as a member. The runtime leaves a campaign's inbound read to
  the events on the strength of this line, so naming the campaign is still the right course: the first rule decides alone
  when it finds one, and a move that names none reaches the campaigns the person is in rather than the one whose thread
  it is;
- reports `moved_by: null` only where the provider cannot say, knowing that such a move wakes a manager as a reply;
- answers `events_cursor_unknown` for a cursor it can no longer read, and `events_unreachable` for a provider that did
  not answer or is unavailable for now;
- pins nothing;
- keeps its credential out of the binding, out of what it logs and out of what it returns: it reads it with `host.env`.

The fixtures under [`../fixtures/events.read/`](../fixtures/events.read/) are run by the repository's tests against
this document, so an example on this page and the contract cannot drift apart.
