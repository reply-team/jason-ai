# Events

A provider knows things Jason was not there for: somebody replied, an invitation was accepted, a message bounced. The
events family is how Jason comes to know them — by reading each account a route binds, a page at a time, from the
point the plugin's own cursor names — and how what it read reaches a campaign: as a line in that campaign's chronicle,
which wakes its manager like a failed item or an answered question does ([campaign manager](campaign-manager.md)).

Nothing here reads meaning. An event is identifiers, moments and the provider's own flags; which campaigns it concerns
is decided by the identifiers the campaigns' own operations pinned, and what it calls for is the manager's.

This page is the contract for reading an account, the events store and the lines it writes. What a plugin author
implements is [`events.read`](contracts/events/events.read.md).

## 1. The contract

`events.read` is a family of its own beside the canonical operations and the delivery contract
([contracts](contracts/README.md)): a read of an account belongs to no campaign, so no work item names it, no route
carries it and no approval applies to it. A provider plugin declares that it reads its accounts' events in its manifest,
`contracts.events: [1]`, beside `contracts.operations`; a notification plugin that declares it is refused as
`events_not_allowed` ([plugins](plugins.md)). The invocation carries the binding that says which account, and nothing
about a campaign.

A read starts from a cursor the plugin returned, or by date — `since`, with an optional `until` — and answers a page of
at most 100 events, the cursor to read on from, and whether more follow now. A child is given 60 seconds for a page.

| Kind | Identifiers | The provider's flags |
|---|---|---|
| `thread_moved` | `thread`, `contact`, `campaign` | `moved_by`, `channel`, `unread`, `has_meeting_intent` |
| `reply_detected` | `contact`, `campaign`, `thread` | `channel` |
| `invitation_accepted` | `contact`, `campaign`, `account` | — |
| `bounce` | `contact`, `campaign`, `message` | `bounce` |

`moved_by` is the provider's own word for who moved a thread: `other_side`, `account` — a message sent as the account,
Jason's own replies among them — `provider`, a change with no message such as a category or the read flag, or `null`
where the provider cannot say. Every kind is a closed object, so a subject, a preview or a name is refused with the
whole page, and so is an identifier with a space in it.

A failure carries one of `events_unreachable` (transient), `events_unauthorized`, `events_unsupported` and
`events_cursor_unknown` (permanent). A read changes nothing at a provider, so reading again is always safe.

**The cursor is the plugin's.** A cursor is 1 to 2048 printable ASCII characters with no space. The runtime stores it as
it came, beside the account it was read from, hands it back unchanged, compares it only to know that nobody moved it
meanwhile, and never parses, logs, answers or journals it.

**A read pins nothing.** An answer that carries identifiers beside its result is refused: a pin is written by an
operation's answer and by nothing else ([routing](routing.md) §11), and the store only finds them.

## 2. The store

Three tables, in the runtime's own database.

- **`event_sources`** — one row for each account a read hangs on: a plugin and the identity of a binding a route names
  for it, the `sha256:` of the binding's canonical form ([routing](routing.md) §10), or `none` for a route that names
  no binding. The row holds the cursor the plugin last returned and when it was stored. The binding's value is the
  route's, read from the active snapshot at each read; a source whose binding leaves every route keeps its row and its
  cursor. Two bindings that name one account are two sources.
- **`events`** — every event a read stored, as the contract's output schema accepted it: its kind, when it happened,
  when it was stored, which read stored it, the person it names where exactly one contact is pinned at the plugin with
  the identifier the provider gave, and its key.
- **`event_attributions`** — which campaigns an event concerns, one row each, with the rule that found it. An event may
  concern several.

**Kept whole.** The store is append-only three times over, as the journal is: no operation of the Runtime API changes
or removes an event; the runtime refuses to before the database is reached; and the triggers `events_no_update`,
`events_no_delete`, `event_attributions_no_update` and `event_attributions_no_delete` refuse a raw statement that tries.
An event no campaign's pins match is kept, concerning nobody. Nothing re-attributes an event later: which campaigns it
concerns is decided once, when it is stored, by the pins as they stood then — as a campaign linked late is read by the
inbound read only from then ([inbound sync](inbound-sync.md) §5).

**Each event once.** An event's key is the `sha256:` of the plugin, the kind, the identifiers the kind names and the
moment it happened, and for a moved thread who moved it: the provider setting a category as a reply arrives, at the same
moment, is a move of its own, and the other side's reply is still said. It is the plugin's key, not the account's, so
two sources reading one account store each event once. A page read again — after a crash, or a range read again on a
person's request — stores nothing it already holds: the keys are read first, and the store's unique index refuses the
rest. The provider's own event id is kept with the event and is not part of its key.

**A page lands whole, or not at all.** The events a page adds, their attributions, the lines they say and the cursor the
page returned are written in one transaction, and the cursor moves only from the cursor the source held when the read
began. A page that does not land leaves the cursor where it was, and the next read asks for the same events again.

## 3. Which campaigns an event concerns

An event reaches a campaign by the provider's campaign identifier it names, when exactly one campaign that is not
archived holds that identifier pinned at the event's own plugin — rule `campaign_pin` — and by nothing else: not a
route to the plugin, not a membership, not a tag, not a name. When no campaign holds it, or more than one does, the
event concerns nobody. A value kept only as a divergence beside a pin is not a pin.

An event that names no provider campaign — a direct email's reply, a LinkedIn message outside a sequence — concerns
nobody in this version.

## 4. What a stored event says

For each campaign it concerns, a stored event says at most one line, written in the same transaction:

| The event | The line |
|---|---|
| `thread_moved` by `other_side`, or by `null` | `inbound_reply_observed` |
| `thread_moved` by `account` or `provider` | none |
| `reply_detected`, `invitation_accepted`, `bounce` | `provider_event_observed` |

Both lines are written by `system:dispatcher`, name the campaign and no work item or attempt, and are trigger kinds a
manager is woken by under the default `Manager:Triggers`.

- **`inbound_reply_observed`** is the line the inbound read writes, with its fields — `plugin`, `thread`, `channel`,
  `last_activity_at`, `unread`, `has_meeting_intent`, `contact_id` — and one more, `event_id`, the stored event. It is
  keyed as the inbound read keys it, by the plugin, the thread and the moment, so a move both reads found is said once.
  A move the provider cannot say who made reads as a reply, so a follow-up sent into a thread that already has a reply
  is said again, as the inbound read says it.
- **`provider_event_observed`** carries `kind`, `plugin`, `event_id`, `occurred_at`, `channel`, `contact_id`, and the
  kind's own identifiers and flags: `thread` for a detected reply, `account` for an accepted invitation, `message` and
  `bounce` for a bounce. It is keyed by the event's own key.

**Said once.** A campaign says each line once under its key, and the database refuses a second; one key may be said in
two campaigns. **History is not said.** An event that happened before the campaign first went live is stored and found
to concern it, and says no line: `events_from` is set when a campaign first goes live, a pause and a resume leave it,
and an installation upgrading into the events store gives every active and paused campaign the moment of the upgrade.

**Never a person's words.** No line, no log line and no answer of the runtime carries a subject, a preview, a name or a
cursor: the contract admits none into an event, and the runtime writes none of its own.

## 5. Reading an account

**Which accounts.** Every account a route binds at a plugin that is loaded, usable here and declares the events
contract: a distinct plugin and binding identity named by the global routes or by any campaign's, read whether or not a
campaign is linked there yet, so an account's events are held before a campaign needs them. Each is given its source
the first time it is bound. An account no route binds any more keeps its source and its cursor and is not read; it is
read from that cursor again once a route binds it.

**Where it runs.** The dispatcher's scan has a step for it, **events**, after the lease enforcer and before the inbound
read ([work execution](work-execution.md)). The step reads nothing itself: it hands the reader one read, the most
overdue, and only when the reader is idle, so at most one child of the plugin host reads events at a time, beside the
handler pool's and the notifier's. A read is never a work item and never takes a handler slot. A draining or stopped
dispatcher hands out no new read; a read under way goes on until the stop ends its child, and the page in flight is not
kept.

**A read.** At most 20 pages, each one child given the contract's 60 seconds, lowered by the plugin's own ceiling; the
next page is asked for from the cursor the last one returned, while the provider says more follow. Each page is held to
the contract and stored in a transaction of its own (section 2), so what earlier pages stored stays when a later one
fails. A read that stops at 20 pages with more to read is due again from when it ended: at once, but behind an account
that has been due for longer, so one account's backlog takes turns with the others.

**When.** A source this runtime has not read since it started is read at once — a start catches every account up from
its cursor — and so is one never read. After that, `Dispatcher:EventsCadenceSeconds` after the last read started,
doubled for each read in a row that failed, at most a day.

**From where.**

| The source | The read starts | |
|---|---|---|
| no cursor yet | 30 days back | a first start owes nothing before that |
| a cursor stored within 30 days | from the cursor | the cadence, and the catch-up at a start |
| a cursor stored longer ago | 30 days back | the gap: one `events_gap_observed` line |
| a cursor the plugin forgot | a day before it was stored, or 30 days back when that is later | the gap rule, when older |

**The gap.** What lies between a cursor older than 30 days and the 30 days a read reaches back is not read. It is said
once, with the first page of the read that skips it, as an `events_gap_observed` line naming no campaign — the source,
the plugin and the span — keyed by the source and where the span starts, so a read tried again says it once. The next
notification that reaches anybody counts it ([notifications](notifications.md)).

**When a read fails.**

| How it ended | The next read |
|---|---|
| `transient`, or `ambiguous` — a timeout, a crash, an answer outside the contract | at the doubled cadence, with the same input |
| `permanent` or `validation` | a day after the failed read started |
| `events_cursor_unknown` | at once, by date: the cursor is cleared and when it was stored is kept |

An answer outside the contract is `result_invalid`: a page the output schema refuses, or one it accepts that the
runtime cannot read, identifiers beside a result, or a failure whose code the contract does not declare with the class
it came with. Such a page stores nothing, and the cursor stays where it was.

**What is recorded.** The source keeps when its last read started and ended, when the last read that succeeded started,
and how many reads in a row failed, with the last one's code and class. Each read that stored something, failed, or read
by date writes one `events_read` line naming no campaign: the source, the plugin, the binding identity, how it read, the
pages, how many events it stored and found already held, how many lines it said, how it ended, and the package and the
snapshots it ran under — never a cursor, a binding or an event. A read from a cursor that found nothing writes none. The
log carries the source's id, codes, classes and counts.

**Beside the inbound read.** While a plugin's account is read here, the inbound read leaves the campaigns routed to it —
but only a campaign an event would reach by section 3's rule, only while that account's last read that succeeded started
within the shorter of two cadences and `Inbound:LookbackSeconds`, and never before that first success
([inbound sync](inbound-sync.md) §2). A move both find is said once (section 4).

**The setting.**

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Dispatcher:EventsCadenceSeconds` | `600` | 60..86400 | how long after one read of an account starts the next is due |

It applies at the next scan, without a restart. **What it costs:** one read per bound account per cadence, 144 a day at
the default, most of them one child that finds nothing and writes no line.

## 6. Not here yet

- **Reading a range again, a record of the sources, a signal.** No operation of the Runtime API asks for a range of an
  account to be read again, lists the sources and their reads, or brings a read forward.
- **A campaign without a provider campaign.** An event that names a person and no provider campaign concerns nobody.
- **Reading the store.** No operation of the Runtime API lists stored events; a manager reads the lines they say.
