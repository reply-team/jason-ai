# Canonical operation contracts

A **canonical operation** is one unit of SDR work named in the practitioner's language, with no provider detail in
it: `campaign.get`, `list_membership.add`, `campaign.enroll`. A plugin implements operations; a planner composes
them; the runtime enforces them. Nothing in this package names a vendor, and nothing in it depends on one.

**Two namespaces, the same words.** A canonical operation's name reads like one of Jason's own verbs —
`campaign.get` beside `jason campaign get` — and the two are different things. The operation is work a provider
performs through a plugin, named by a `provider_op` work item's `--operation`; the verb asks Jason's own runtime about
Jason's own records. Nothing in one namespace reaches the other: `jason campaign get` never calls a provider, and no
work item can name `campaign.get` to read Jason's record.

Each operation has exactly one source of truth: the JSON document under [`operations/`](operations). That file is
**embedded into `Jason.Contracts`** as a resource, so the contract the runtime applies is the very file you are
reading — there is no second copy to drift. The `.md` page beside it explains the operation for a person; the
generated block inside that page is rendered from the JSON by a test. **Prose explains; the generated block
governs, and the JSON governs the block.**

**What runs today.** The documents are published and embedded, and the runtime holds a `provider_op`
work item to the one it names from end to end: an operation no contract is published for is refused at
`workitem.create`, and the arguments the item carries under `context.input` are measured against that
operation's argument schema — there, and again whenever a patch rewrites them. At the claim the item is
routed to a plugin, the whole input below is composed and validated, the plugin is given the budget the
document states, and its answer is measured against the output schema before it becomes the item's
result; the repeat rule below is what the runtime reads when such an attempt ends ambiguously.
[`docs/plugins.md`](../plugins.md) §7 is the same material from the plugin author's side, with a worked
example. An operation reaches a plugin only where a route sends it; without one, the item fails at the claim
with `no_route`.

```
docs/contracts/
  README.md                        this page
  divergence-from-l1.md            where Jason's stricter subset departs from the contract it is seeded from
  operations/<name>.json           the contract — one file, one version, embedded into the runtime
  operations/<name>.md             the same contract explained, with a generated properties block
  fixtures/<name>/input-*.json     argument vectors, valid and invalid, with the pointer and reason of each failure
  fixtures/<name>/outcome-*.json   answer vectors, with the status, class and retriability each implies
  fixtures/<name>/preview-*.json   what a person deciding reads, rendered from an input: the summary and its highlights
  notifications/<name>.json        the delivery contract, a family of its own, embedded the same way
  notifications/<name>.md          the same contract explained, with a generated block
  events/<name>.json               the events contract, a third family, embedded the same way
  events/<name>.md                 the same contract explained, with a generated block
  preferences/<key>.json           a named preference of the client, a fourth family, embedded the same way
  preferences/<key>.md             the same preference explained, with a generated block
  fixtures/preferences/<key>/value-*.json   values, accepted and refused, with the pointer and reason of each refusal
  block/provider_block.json        the provider block a blocked failure carries, written once for every operation
  block/provider_block.md          the same block explained
  fixtures/block/value-*.json      blocks, accepted and refused, with the pointer and reason of each refusal
```

## The published operations

Every operation this build publishes, rendered from the documents by a test and compared whole, so the table cannot
say what a document does not. Each value is the one the runtime reads — the dangerous reading, where a document states
a condition beside it — and the last column is the line the operation's own page opens with.

<!-- BEGIN GENERATED operations -->
<!-- Rendered from each document and its page's title. Edit those; this table follows them. -->

| Operation | Level one | Reach | Approval | After an ambiguous end | Provider contact | What it is |
|---|---|---|---|---|---|---|
| [`campaign.activate`](operations/campaign.activate.md) | `campaign.activate` | `act` | `confirm_once` | `after_recovery_read` | `not_used` | start a provider campaign sending |
| [`campaign.create`](operations/campaign.create.md) | `campaign.create` | `control` | `auto` | `after_recovery_read` | `not_used` | make the provider's campaign, as an empty draft |
| [`campaign.enroll`](operations/campaign.enroll.md) | `campaign.enroll` | `act` | `confirm_once` | `after_recovery_read` | `find_or_create` | put one person into a provider campaign |
| [`campaign.get`](operations/campaign.get.md) | `campaign.get` | `read` | `auto` | `safe` | `not_used` | read one campaign from the provider |
| [`campaign.pause`](operations/campaign.pause.md) | `campaign.pause` | `control` | `auto` | `safe` | `not_used` | stop a provider campaign |
| [`contact.delete`](operations/contact.delete.md) | `contact.delete` | `control` | `confirm_once` | `after_recovery_read` | `pinned_only` | delete the provider's contact for one person |
| [`contact.update`](operations/contact.update.md) | `contact.update` | `control` | `auto` | `after_recovery_read` | `find_or_create` | change a person's fields on the provider's contact for them |
| [`conversation.classify`](operations/conversation.classify.md) | `conversation.classify` | `control` | `auto` | `safe` | `not_used` | file a thread under one of the provider's categories, with the provider's own meaning on its reserved names |
| [`conversation.close`](operations/conversation.close.md) | `conversation.close` | `control` | `auto` | `safe` | `not_used` | set a thread aside as worked |
| [`conversation.get`](operations/conversation.get.md) | `conversation.get` | `read` | `auto` | `safe` | `not_used` | read one thread before answering it |
| [`conversation.list`](operations/conversation.list.md) | `conversation.list` | `read` | `auto` | `safe` | `pinned_only` | the conversation threads of a person or a campaign |
| [`conversation.reopen`](operations/conversation.reopen.md) | `conversation.reopen` | `control` | `auto` | `safe` | `not_used` | put a thread back among those that need attention |
| [`conversation.reply`](operations/conversation.reply.md) | `message.send` | `act` | `confirm_each` | `never` | `find_only` | answer the person on a thread, in that thread |
| [`engagement.summarize`](operations/engagement.summarize.md) | `engagement.summarize` | `read` | `auto` | `safe` | `not_used` | the provider's counts for a campaign |
| [`enrollment.list`](operations/enrollment.list.md) | `enrollment.list` | `read` | `auto` | `safe` | `not_used` | who is in a campaign at the provider |
| [`enrollment.stop`](operations/enrollment.stop.md) | `enrollment.stop` | `control` | `confirm_once` | `after_recovery_read` | `find_only` | take one person out of a provider campaign |
| [`list_membership.add`](operations/list_membership.add.md) | `list_membership.add` | `control` | `auto` | `after_recovery_read` | `find_or_create` | put one person on a provider list |
| [`message.send`](operations/message.send.md) | `message.send` | `act` | `confirm_each` | `never` | `find_or_create` | send a person a message, outside any campaign, from one of the provider's accounts |
| [`optout.record`](operations/optout.record.md) | `optout.record` | `control` | `auto` | `after_recovery_read` | `find_or_create` | record that somebody said stop |
| [`schedule.list`](operations/schedule.list.md) | `schedule.resolve` | `read` | `auto` | `safe` | `not_used` | the provider's sending schedules, with the windows each one sets |
| [`sender.list`](operations/sender.list.md) | `sender.list` | `read` | `auto` | `safe` | `not_used` | the provider's accounts a send can go from |
| [`social_invitation.send`](operations/social_invitation.send.md) | `social_invitation.send` | `act` | `confirm_each` | `never` | `find_or_create` | invite a person to connect, from one of the provider's accounts |

<!-- END GENERATED operations -->

## The delivery contract

One more document is published here, and it is not a canonical operation:
[`notifications/notification.send.json`](notifications/notification.send.md), what a notification plugin is invoked
for — put a pointer in front of a person: how many approvals and questions wait, whether protective actions failed,
accounts have events Jason did not read or provider blocks are open, and where to look. It is a family of
its own, under `notifications/` rather than `operations/`, read by its own catalog. The operation catalog has no notion
of kind: a document dropped among the operations would become a provider operation a work item could name, a route
could carry and a lease would be sized for. So it has none of an operation's level-one fields — no reach, no approval,
no preflight, no repeat rule — because a delivery is not SDR work, and only its input, its result, three failure codes,
its timeout and its conformance list. Its fixtures live beside the operations' under
[`fixtures/notification.send/`](fixtures/notification.send) and are run by the same tests.

## The events contract

A third family is published here, and it is neither a canonical operation nor a delivery:
[`events/events.read.json`](events/events.read.md), what a provider plugin that declares `contracts.events: [1]` is
invoked for — read what happened in one account after the point the plugin's own cursor names: a thread that moved, a
reply the provider detected, an invitation accepted, a message that bounced. A read of an account belongs to no
campaign, so no work item names it and no route carries it; it is under `events/` and read by a catalog of its own, for
the same reason the delivery contract is. It has the delivery contract's fields and nothing more: its input, its
result, four failure codes, its timeout and its conformance list. What it returns is identifiers, moments and the
provider's own flags — every kind a closed object, with nothing anybody wrote — and it pins nothing: a read finds the
identifiers operations pinned and writes none. Its fixtures live beside the others' under
[`fixtures/events.read/`](fixtures/events.read) and are run by the same tests.

## The provider block

A failure of the class `blocked` says the provider refused the call for a reason outside the work item — the account's
money or plan, an allowance it counts, a credential it no longer accepts, a channel disconnected or paused — and did
nothing for it. Every published operation declares it under one code, `provider_blocked`, and the failure carries a
block: what is blocked, the account or one sender; why, from a closed list; where a person clears it; and, where the
provider says, when it clears by itself. The block is written once, for every operation, in
[`block/provider_block.json`](block/provider_block.json), explained on [its page](block/provider_block.md), embedded
into the runtime the same way, and its value fixtures under [`fixtures/block/`](fixtures/block) are run by the same
tests. The events and delivery families declare no block.

## The named preferences

A fourth family is published here, and it is no work at all: the **named preferences**, structured facts about the
client that a person states, each with a schema, so that a skill can check that one is set before it writes and a
screen can show it. Two are published:

- [`sender.identity`](preferences/sender.identity.md) — who messages are written as;
- [`tone_of_voice`](preferences/tone_of_voice.md) — how the company sounds.

A preference document is `{key, version, summary, value_schema}` and nothing else: no work item names a key, no route
carries one and no plugin is invoked for one, so it is under `preferences/` and read by a catalog of its own. A key is
lower-case words of letters, digits and underscores, each starting with a letter, joined by dots, at most 64
characters, and its document is named for it. Every value is a JSON object, checked against the key's `value_schema`
in the dialect below, of at most 64 KiB of UTF-8 in its canonical form, counted with nothing escaped, so a character
costs what UTF-8 spells it in, from one byte in ASCII to four outside the Basic Multilingual Plane
(a preference reports the `value_bytes` the cap compares, once the value has been changed by this version). A key's schema changes only by gaining an optional
field, which raises the document's version, so a skill reading the document knows which fields may be present; anything
else is a new key. There is one value per key for the whole installation, set and unset by a person
and read by everyone else. Its fixtures live under [`fixtures/preferences/`](fixtures/preferences) and are run by the
same tests.

## What a document states

The first group of fields is the **vendor-neutral contract**, copied word for word from the SDR operations contract
this catalog is seeded from — recorded in each document under `l1`, for the first level: the business layer that sits
above any provider (see "Where these come from"): `reach`, `reversibility`, `approval`, `approval_artefact`,
`approval_departs`, `protective_floor`, `approval_reason`, `before_repeating`, `idempotency_key`, `per_item_results`,
`accepts_collection`, `cost`, `cost_basis`, `meter`, and the `invariants` that name the operation. `approval_departs`
and `protective_floor` are the catalogue's own marks, copied from the fragment as written: the first is "true when the
value RAISES above the derivation table", the second "true for the one legitimate LOWERING: an operation whose
non-performance is itself the harm", and `approval_reason` says why. The catalogue sets both on `optout.record`, whose
`auto` raises nothing — the table derives `confirm_once` for an irreversible control — so that document says `true`,
`true` because its fragment does, not because one approval is both a raise and a lowering. The catalogue raises
`contact.delete` and `social_invitation.send`, which state `approval_departs: true`, `protective_floor: false` and each
fragment's reason. An
operation that does neither states `false`, `false` and `null`. The floor is checked when the document loads: it is refused unless
`approval` is `auto`, `approval_reason` says why, `preflight.suppression` is `not_applicable` and
`dispatch.in_paused_campaign` is `runs`, so neither the suppression register nor a paused campaign holds back an
operation whose not running is the harm: its work starts once nothing else in its campaign is scheduled or processing,
in its priority order. `approval` is one of three words — `auto`, `confirm_once` or `confirm_each` — and a document
stating any other fails to load. The two a person gives both park the work for one decision per work item, bound to the
input it would run with; one item is one person, so `confirm_each`, a decision per person reached, asks for no more
than that, and a document stating it names one person — `preflight.contact` is `required` and `accepts_collection` is
`false` — and highlights `/contacts/0/channels/0/value`, where that person is reached, in its preview. Seven of these
fields — `reach`, `reversibility`, `approval`, `before_repeating`,
`idempotency_key`, `per_item_results` and `cost` — may be a plain value or a **conditional block** of
`{value, conditional, detail}`, where `value` is always the *dangerous* reading. Every gate reads `value`, so a
condition can never soften one, and `detail` is what an agent reads to decide.

The second group is Jason's own, and it is about execution:

| Field | What it says |
|---|---|
| `preflight` | whether the work item's contact is needed (`required` / `not_used`, or `optional`: taken when the work item names one) and where the channel comes from (`from_args` / `none`); an `optional` contact never takes its channel from the arguments; and whether the suppression register refuses the work for a person whose consumed value it holds (`suppression`: `checked`, or `not_applicable` — allowed only where `reach` is `control`, for an operation that takes a person out rather than reaching them); and how the plugin finds the provider's own contact for that person (`provider_contact`: `find_or_create`, `find_only`, `pinned_only` or `not_used` — [`docs/plugins.md`](../plugins.md) §7, "The contact proxy rule", is the rule), which the loader holds to what each word implies: a way that finds the person declares `external_ids.contact`, `find_or_create` takes its channel `from_args`, `pinned_only` declares `contact_not_linked`, and `not_used` is said exactly where `contact` is `not_used` |
| `dispatch` | how the dispatcher treats the operation's work beyond what all work shares: `in_paused_campaign` is `waits` — the work waits with its paused campaign, as all work does — or `runs`, allowed only where `reach` is `control` and `approval` is `auto`, for an operation whose not running is the harm |
| `contact_projection` | exactly which contact fields, and which channels, the plugin receives — `consumed`, the one channel the arguments name, or `none` — declared so that widening it is a visible change. It names exactly what the operation's implementation reads, the least of a person that reaches a plugin's process, for the fields as for the channels |
| `precondition` | what must be true at the provider before the operation can act, beyond how its contact is found, in one sentence |
| `preview` | what a person deciding on the operation reads first — a one-line summary rendered from the composed input, and the values in that input it highlights — or `null` where nobody is asked to decide |
| `input_schema` | the whole composed input the plugin receives, in the dialect below |
| `output_schema` | what a succeeded result must look like |
| `external_ids` | the kinds of provider identifier the operation may return, and what each identifies |
| `recovery_read` | what to read before repeating after a lost answer, and why the reading is worth the round trip |
| `repeat_after_ambiguous` | `safe`, `after_recovery_read` or `never` — what may be done when an attempt ends without a usable answer |
| `failure_codes` | the codes a plugin may report, each under one of the five classes, with the moment it applies |
| `timeout_ms` | the child's budget for this operation |
| `conformance` | what a conforming plugin must be able to demonstrate |

**Absence is not a default.** Every field is stated in every document, `null` included; a document that leaves one
out fails to load and names the field. A missing property would read as whatever the reader assumed, and that is
exactly the reading nobody wrote down.

## The input a plugin receives

The runtime composes it; the plugin never sees a work item's context. It is always:

```json
{
  "args": { },
  "contacts": [ { } ],
  "campaign": { "id": "cmp_…", "name": "…", "status": "draft", "external_ids": { } },
  "idempotency_key": "wi_…"
}
```

- `args` is what the caller put under the work item's `context.input`, and nothing else from the context. It is
  present even when the operation takes no arguments, as `{}`.
- `contacts` appears when `preflight.contact` is `required`, or when it is `optional` and the work item names a
  contact. It carries exactly one person — an operation about a person addresses one person in this version, and a
  read about a person reads about one — and is cut to the declared projection: only the channel the operation consumes,
  or none, never the person's whole reachability.
- `campaign` is Jason's record of the campaign the work item belongs to. A read made on a person's request — their
  threads, or one thread's messages — belongs to no work item and carries none; the documents of the operations it
  performs say the campaign may go unnamed.
- `idempotency_key` appears only when the operation requires one, and it is the **work item's** identifier: stable
  across every attempt, which is what makes "the prior run under this key" answerable.
- Every `external_ids` object is filtered to the calling plugin's own pins. Another provider's identifier is never
  visible.

## The preview a person reads

An operation a person approves may declare a `preview`: one line a person deciding reads first, and the values in
the input it singles out.

```json
"preview": {
  "summary": "Enroll {/contacts/0/channels/0/value} in the provider's campaign for Jason's campaign {/campaign/name}: …",
  "highlights": ["/contacts/0/channels/0/value", "/campaign/name", "/args/start", "/args/first_touch", "/args/collision"]
}
```

- `summary` is text with `{/json-pointer}` placeholders into the **composed input** — the document the plugin would
  receive. There is nothing else: no escapes, no conditionals and no logic, and any other brace is refused when the
  document loads.
- A value renders as follows: a string bare; a number or a boolean as its JSON literal; null, or nothing at the
  pointer, as `—`; an object or an array as compact JSON, its text left as written apart from a character outside
  the Basic Multilingual Plane, which the JSON writes as an escape. Every control character in a value, either
  Unicode line separator and every format character — the bidirectional controls, the zero-width characters, the
  byte-order mark and the tag characters among them, one outside the Basic Multilingual Plane included — become a
  space, so the summary is always one line, read in the order it is written: a value can neither forge a second line
  nor turn the first one around.
- `highlights` lists pointers; each carries a copy of its value, or `null` where the input holds nothing there.
- Every pointer is checked when the document loads. The walk steps through plain `properties` by name and `items`
  by index, and refuses a name a closed object does not allow, an index past `maxItems` and a step into a value
  whose only type is a scalar; a value that may be a scalar or something else is not refused.
  What lies behind a `$ref`, an `anyOf` or an `allOf` is proven by the preview fixtures instead: every pointer has to
  name a value in at least one of them.

The claim renders the preview when it parks the work, from the subject's own input, and stores it beside the subject:
the approval's `preview` starts with `summary` and `highlights`, and `jason approval get --human` prints the summary
first, above the operation. **The preview is never part of what is approved:** the subject carries the composed input
— beside the operation and its version, the plugin, the account and the work item — and not the summary, so its hash
does not change when a summary is worded differently. An operation nobody is asked to decide on
declares `preview: null`, and every `auto` operation does.

## Pages

A read that can answer more items than one result may hold — `enrollment.list`, `conversation.list`,
`conversation.get`, `sender.list` and `schedule.list` — answers a page of them, and every paged read pages the same way. `engagement.summarize` answers
at most a row per channel and is not paged.

- `args.limit` — at least 1, at most what the operation states — and `args.offset` — 0 to 1 000 000 — are both
  required: a page size nobody chose is a decision made on somebody's behalf.
- The result holds at most `limit` items, starting at `offset` in the provider's own order, which `vendor.ordering`
  names, and `has_more` says whether more follow. The page array's `maxItems` is the same number as `limit`'s
  maximum.
- **A page may come back shorter than `limit` with `has_more: true`.** The runtime refuses a result above 1 MiB,
  counted as System.Text.Json's default encoder writes it — six bytes for DEL, for every character outside ASCII
  and for each of `<`, `>`, `&`, `'`, `"`, `+` and the backtick, twelve for a character outside the Basic Multilingual
  Plane — and a page of the largest items a schema allows can pass that. So a plugin stops filling a page before the
  result would pass 768 KiB.
- The next page starts at `offset` plus the number of items this page returned, never at `offset + limit`.
- One item always fits. Every output string of a paged read is bounded, and the largest result the schema allows
  with one item on the page — every string at its longest, twelve bytes a character — is held under 768 KiB by a
  test.
- No canonical operation pages by a cursor: the providers mapped first page by offset, and
  [`divergence-from-l1.md`](divergence-from-l1.md) records where the catalogue says otherwise. The events contract is
  the one read that does: it pages by a cursor its plugin returns and nothing else reads, with a page of at most 100
  events held to the same 768 KiB, and one event always fits.

## The schema dialect

Schemas are JSON Schema 2020-12 — every schema carries
`"$schema": "https://json-schema.org/draft/2020-12/schema"`, so any conforming validator reads them unchanged,
with the one deviation noted below —
restricted to a published vocabulary. A keyword outside it is refused rather than ignored, because a keyword the
runtime would silently skip is a rule that does not run.

**The vocabulary.** `$schema`, `$id`, `$defs`, `$ref` (local `#/$defs/<name>` only), `type`, `properties`,
`required`, `additionalProperties` (boolean only), `enum`, `const`, `minLength`, `maxLength`, `pattern`, `minimum`,
`maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`, `items`, `minItems`, `maxItems`, `uniqueItems`,
`anyOf`, `allOf`, `not`, `format` (for `date-time` and nothing else), `title`, `description`, `examples` (carried,
never enforced).

**Reason codes.** Every failure carries a JSON pointer — the empty string is the whole document — and one of these
fixed codes: `type`, `required`, `enum`, `const`, `min_length`, `max_length`, `pattern`, `minimum`, `maximum`,
`exclusive_minimum`, `exclusive_maximum`, `multiple_of`, `min_items`, `max_items`, `unique_items`,
`additional_properties`, `any_of`, `all_of`, `not`, `format`, `schema_keyword_unknown`,
`schema_keyword_malformed`, `schema_ref_unresolved`, `duplicate_property`, `number_not_comparable`, `pattern_timed_out`,
`schema_too_deep`, `schema_cyclic`, `schema_pattern_invalid`, `schema_too_large`, `schema_number_not_finite`.

The `schema_…` codes say the schema is wrong, not the value: they are what a rule that could not run reports, and
the reader who has to fix one is looking at the contract rather than at the arguments. `pattern_timed_out` is about
neither: a pattern did not finish matching, twice, so nothing is known about the value — the last detail below says
what that costs. Everything else is about the value at the pointer.

Six details worth knowing before you write a schema:

- **Combinators report once.** `anyOf`, `allOf` and `not` report a single problem at their own pointer rather than
  every branch's problems; the message names what the first failing branch objected to. Listing every road not
  taken buries the one thing a caller has to change. The exception is a branch no pattern could finish measuring:
  unless a measured branch has already decided the combinator, its `pattern_timed_out` is passed up as it stands, at
  its own pointer, because a branch nobody measured gives the combinator no verdict to report.
- **An explicit `null` satisfies `required`.** Saying null is saying something; only an absent property is missing.
  That is a rule about a property *inside* a document being validated, and it is not the rule about the work item's
  reserved `context.input` key, where an absent key and a JSON null read alike — neither is a set of arguments.
- **`additionalProperties: false` sees only its own object.** It is evaluated against the properties named in the
  same schema object, and does not see properties contributed by an adjacent `$ref` or by an `allOf` branch. A
  2020-12 validator evaluates it against everything annotated at that location, so a schema that combines
  `additionalProperties: false` with `$ref` or `allOf` is read differently here and elsewhere — which is the one
  place the portability above does not hold. Write the properties in the same object as the keyword, and the two
  readings agree.
- **Numbers are compared as decimals**, so that a money-like value means what it says. A number no decimal can hold
  exactly — `1e40`, `1e-40` — is refused as `number_not_comparable`, whether it is the bound or the value, because
  the rule could not run at all. That is not the same as `minimum` or `maximum`, which say a rule ran and the value
  was outside the range the schema set. Whether a number is a whole one, for `type: integer`, is not a comparison
  and is decided for any number, however large.
- **The same code validates untrusted schemas**, because a plugin's `binding` schema comes from its manifest.
  A pattern is read against [the pattern grammar](#the-pattern-grammar) below, and compiles without backtracking
  under a match timeout. `$ref` resolves only inside its own document and never in a loop; nesting stops at 32
  levels; a schema over 64 KiB is refused unread.
- **The build's own patterns are warmed when the runtime starts, and a plugin's binding patterns are decided when its
  package loads, on a warm engine; at a work item a timeout is never a verdict on the value, and what happens then
  depends on where it happened.** Every match runs under a published 100 ms, and with matching linear in the input the
  only costs that can run past it are a cold engine's — building its automaton, compiling its code — and a pathological
  pattern's. The runtime matches the patterns of these documents once when it starts. A plugin's binding patterns are
  probed when its package loads, against a short string and against the same string repeated to 64 KiB, the longest
  binding a route can carry, and one that still cannot finish on a warm engine refuses the package: the reload reports
  it as `field_invalid` at `binding#/properties/<name>/pattern`, the code a manifest gives every dialect problem in its
  binding schema, with a message that names the cause, `pattern_timed_out`. A work item's `result_format` schema is
  probed the same way when the item is written, and when a patch names it, and a pattern that still cannot finish is
  refused there as `schema_pattern_invalid`, final: the schema has to change. A string in an answer longer than 64 KiB
  is held only by that work item's own timeout and attempt count. A match that times out anywhere is tried once more at
  once. A second timeout is reported as `pattern_timed_out`, and nothing has been decided: a request is answered
  `validation_timed_out`, retryable; at the claim the attempt ends `transient`; over a plugin's answer the plugin has
  run, so the attempt is `ambiguous` and the operation's own repeat rule decides.

### The pattern grammar

A pattern means what ECMA-262, the dialect JSON Schema names, says it means, and the engine that runs it reads some
of the same text differently. So every construct a pattern may write is in this table, outside a character class
and inside one. It is admitted as written where the two readings agree; rewritten to the engine's spelling of
ECMA-262's meaning where they differ and one can be written; and refused as `schema_pattern_invalid` where they
differ and none can, or where the engine cannot run it without backtracking. A construct the table does not name
is refused as unknown. A refusal names the construct and the character it starts at, and never quotes the pattern;
a problem about a value — `pattern`, or `pattern_timed_out` — quotes the pattern, because the pattern is the rule
the value has to meet. ECMA-262 has been the dialect all along; this table is how the runtime holds a pattern to it.

| Construct | Outside a class | Inside a class | Why |
|---|---|---|---|
| any character no other row names | admitted | admitted | itself, in both readings |
| `^` | admitted | admitted | the start of the value; first in a class, the class's negation; elsewhere in a class, itself |
| `$` | `\z` | admitted | ECMA-262's end is the end of the value, where the engine's also matches before a final line feed; in a class, itself |
| `.` | `[^\n\r\u2028\u2029]` | admitted | ECMA-262 leaves out the four line terminators, where the engine leaves out only the line feed; in a class, itself |
| `\|` | admitted | admitted | the same in both readings; in a class, itself |
| `(` ... `)` | admitted | admitted | the same in both readings; in a class, itself |
| `(?:` ... `)` | admitted | — | the same in both readings |
| `(?=`, `(?!` | refused | — | the engine that runs patterns cannot run it without backtracking, which keeps a hostile pattern linear |
| `(?<=`, `(?<!` | refused | — | the engine that runs patterns cannot run it without backtracking, which keeps a hostile pattern linear |
| `(?<`name`>`, `(?'`name`'` | refused | — | a name serves nothing in a pattern that only accepts or refuses a value |
| `(?>` | refused | — | the engine's own; ECMA-262 has none |
| `(?(` | refused | — | the engine's own; ECMA-262 has none |
| `(?#` | refused | — | the engine's own; ECMA-262 has none |
| `(?i`, `(?m`, `(?n`, `(?s`, `(?x`, `(?-` | refused | — | the engine's own; a schema's pattern carries no flags, and ECMA-262 reads it only with one |
| any other `(?` | refused | — | neither reading has it |
| `*`, `+`, `?`, each also followed by `?` | admitted | admitted | the same in both readings; in a class, itself |
| `{n}`, `{n,}`, `{n,m}`, each also followed by `?` | admitted | admitted | the same in both readings; in a class, itself |
| `{` that starts no counted quantifier, and `}` | refused | admitted | only ECMA-262's older grammar reads it as itself; write `\{` or `\}`; in a class, itself |
| `]` outside a class | admitted | — | itself, in both readings |
| `[` ... `]`, `[^` ... `]` | admitted | admitted | the same in both readings; in a class, `[` is itself |
| `[]`, `[^]` | refused | — | ECMA-262 reads `[]` as a class of nothing and `[^]` as any character, where the engine reads the `]` as a member; write `\]` for a bracket in a class |
| a `[` never closed | refused | — | neither reading has it |
| `a-z` | — | admitted | the same in both readings |
| a range with `\d`, `\w` or `\s` at either end | — | refused | ECMA-262's older grammar reads it as three members, where the engine reads it its own way |
| `-[` | — | refused | the engine's own; in ECMA-262 the `[` is a member |
| `\d` | `[0-9]` | `0-9` | ECMA-262's digits are the ten ASCII ones, where the engine's are every script's |
| `\D` | `[^0-9]` | refused | its complement of ECMA-262's digits; in a class, a complement cannot be written as members |
| `\w` | `[A-Za-z0-9_]` | `A-Za-z0-9_` | ECMA-262's word characters are ASCII letters, digits and `_`, where the engine's are every script's |
| `\W` | `[^A-Za-z0-9_]` | refused | its complement of ECMA-262's word characters; in a class, a complement cannot be written as members |
| `\s` | `[\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]` | `\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF` | ECMA-262's white space and line terminators: U+FEFF is one, U+0085 is not, the other way round from the engine |
| `\S` | `[^\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]` | refused | its complement of ECMA-262's white space; in a class, a complement cannot be written as members |
| `\b`, `\B` | refused | refused | a boundary between word characters, which the two readings define differently; in a class ECMA-262 reads `\b` as a backspace |
| `\A`, `\Z`, `\z`, `\G` | refused | refused | ECMA-262 reads it as the letter, the engine as an anchor; write the letter without the backslash, or `^` and `$` for the start and the end |
| `\p{`...`}`, `\P{`...`}` | refused | refused | a schema's pattern carries no flags, and ECMA-262 reads it only with one |
| `\1` to `\9`, `\k` | refused | refused | the engine that runs patterns cannot run it without backtracking, which keeps a hostile pattern linear; in a class ECMA-262 reads a digit or a letter |
| `\0` followed by a digit | refused | refused | ECMA-262's older grammar reads an octal escape, the engine its own |
| `\0` followed by no digit | admitted | admitted | the same in both readings |
| `\t`, `\n`, `\v`, `\f`, `\r` | admitted | admitted | the same in both readings |
| `\x` followed by two hex digits | admitted | admitted | the same in both readings |
| `\u` followed by four hex digits | admitted | admitted | the same in both readings |
| `\x` or `\u` without exactly its hex digits, and `\u{` | refused | refused | a code point in braces needs a flag a schema's pattern does not have; otherwise neither reading has it |
| `\c` | refused | refused | the two readings do not agree on its letters |
| `\e`, `\a` | refused | refused | the engine's own; ECMA-262 reads the letter |
| `\` before one of `^ $ \ . * + ? ( ) [ ] { } \| / -` | admitted | admitted | that character itself, in both readings |
| `\` before any other character | refused | refused | the two readings do not agree on what it means, or neither has it |
| `\` at the end | refused | refused | neither reading has it |

## Versioning

Every operation is at `version: 1`. A plugin's manifest declares the operation-contract **family** version under
`contracts.operations`, and a plugin that declares family version N implements version N of every operation it
lists.

- **Additive** — a new optional input field, a required input field made optional, a new optional output field, a new
  failure code, a new external-id kind — keeps the version. A required input field made optional is additive only where
  the runtime supplies the field's former value to the plugin's input, or the operation's document says the plugin must
  take its absence; otherwise it is a breaking change of the operation's version. A new failure class with its
  payload, and the new optional protocol field that carries it, keep the version too: a host that predates them
  refuses them at `host.fail`, and the invocation ends `plugin_exception`, `permanent`, so a newer plugin on an older
  host fails where it can be seen rather than being read as something else.
- **Breaking** — a newly required field, a removed field, a changed enum, a changed property such as `reach` —
  bumps the operation's version *and* the family version. A plugin then declares `[1, 2]` to serve both.
- **Clarification** — prose that says more exactly what a document's machine fields already allow, with no field,
  enum value, property or schema changed. It keeps the version.
- **Relaxation** — a refusal that becomes a success under unchanged fields, such as a pre-flight policy loosened from
  `find_only` to `find_or_create`. It keeps the version: nothing a caller sends or reads changes, and only what the
  plugin does at the provider grows. The operation's page says in one sentence what the policy was before. A refusal
  that protects a person — `suppressed`, a channel conflict, a stop the register or the provider holds — never relaxes:
  taking such a refusal away is breaking.

A route to a plugin whose family versions do not contain the operation's version is refused when the
route is activated, as `route_contract_incompatible`, and again at the claim as
`contract_incompatible`: the plan a run is handed has to be true of the package in front of it.
Compatibility windows and deprecation policy are not settled yet and are deliberately not
implied here.

## The fixtures

Every file under `fixtures/` is executed by the runtime's own tests, which is what keeps these documents and the
runtime honest: a fixture that stops holding fails the runtime's build, so a fixture published here is one the
runtime released with it agrees with. Read them as worked examples of the contract.

An **input fixture** is `{operation, version, case, input, expect}`, where `expect` is either the string `"valid"`
or `{"invalid": [ … ]}`. An **outcome fixture** is `{operation, version, case, outcome, expect}`, where `outcome`
is a plugin outcome exactly as a child writes it and `expect` is `{status, class?, retriable, invalid?, pinned?, block?}`.
`status` is `succeeded`, `failed`, or `result_invalid` — a well-formed outcome whose result does not satisfy the
output schema, which pins an identifier of a kind the operation never declared, or whose failure carries a code the
operation does not declare under that class. `pinned` is what the runtime
writes into its record of provider identifiers from that answer, as `{kind: value}`. A **preview fixture** is
`{operation, version, case, input, expect}`, where `input` is a composed input the operation's schema accepts and
`expect` is `{summary, highlights}`: the line the template renders and the values it highlights, both compared
whole. An operation has preview fixtures exactly when it declares a preview. A **value fixture** of a named preference
is `{key, version, case, value, expect}`, with `expect` written as an input fixture's, and it lives in the directory
named for its key; every key has at least one value it accepts and one it refuses. A **value fixture** of the provider
block is `{contract, version, case, value, expect}`, written the same way, under `fixtures/block/`.

An `invalid` list names exactly what is wrong with the document, and it is written the same way in an input fixture
and an outcome fixture: each entry is a JSON pointer, or `{"pointer": "…", "reason": "…"}` where the fixture means
one particular refusal, the reason being one of the codes above. The reported set must equal the stated set, so a
document broken somewhere other than where the fixture says fails instead of passing for a reason nobody wrote down;
and where a fixture states the reason, a constraint that changes kind at that pointer fails too, rather than going on
passing while it no longer demonstrates what the case is named for.

A `failed` fixture is held against the operation's own `failure_codes`: the code its error carries is one the
document declares, under the class the document declares it with. A fixture may not teach a plugin author a code no
operation accepts, nor the same code under a class that would have the runtime repeat what the document calls final.
The runtime holds every failed answer to the same list: a code the document does not declare, or a declared code under
another class, is `result_invalid`, with the detail at `/error/code` or `/error/class` — except the plugin host's own
codes, which belong to no operation and pass under the class the host writes them with
([the plugin guide, section 8](../plugins.md#8-failure-classes)); the events family alone refuses those as well.

**A failure may still pin.** A failed outcome carries its identifiers on the error — `error.external_ids`, which is
where `host.fail` puts them and the only place the runtime reads them — and a declared kind there is written down by
the same rule as on a success. The answer-lost case is exactly where a pin matters most: the effect happened, the
answer never came back, and the identifier is the only trace of what was done. An **undeclared** kind on a failure is
refused rather than recorded, but it does **not** become `result_invalid` the way it would on a success: replacing
the failure the plugin reported with a shape error would hide the reason the operation failed. The attempt keeps the
plugin's own failure and names the refused identifier beside it. The two fixtures named `outcome-answer-lost` and
`outcome-answer-lost-with-an-undeclared-identifier` are that asymmetry, executed.

**A block is kept or refused.** A `blocked` fixture states `block`: the string `"kept"` where the block satisfies the
published one and is kept on the attempt, or `{"invalid": [ … ]}`, written as above, where it does not. A block outside
the published one is refused the way an undeclared identifier on a failure is: the plugin's failure stands, recorded
`permanent`, with what is wrong with the block named beside it, so the fixture's `class` is `permanent`. A fixture of
any other class states no `block`.

Retriability in an outcome fixture is stated per the operation's own rule, so that a plugin author reading the
fixture sees what the runtime will do with that answer: `transient` is repeated; `permanent` and `validation` are
final; `blocked` is never repeated as a retry — work a block holds waits on the block, and anything else ends there;
`ambiguous` is repeated only where the operation says `safe` or `after_recovery_read`; and `result_invalid`
is always final, because a shape error, or a failure outside the declared codes, is deterministic and the next attempt
would run the same code over the same answer.

## Where these come from

The first group of fields is copied from a vendor-neutral contract of SDR business operations. That contract now
lives in this repository, as the [`sdr-operations`](../../skills/business/sdr-operations/SKILL.md) skill of the
business pack — it was published as an open skill pack in
[`reply-team/reply-skills`](https://github.com/reply-team/reply-skills), **formerly** its home, and is versioned
independently of Jason. The two documents are not the same kind of thing and a reader has to be told which is
which: that catalogue names 325 operations and says what SDR work *is*, while the JSON documents here are what
this runtime validates and executes. Where they disagree, these documents decide. Each one records which version
of the catalogue it was copied from under
`l1.contract_version`, together with the family and the operation's name there. Jason implements a small, strict
subset of it, and [`divergence-from-l1.md`](divergence-from-l1.md) records every place the two differ, why, and
whether the difference is something the catalogue should take back. The catalogue is in this repository now;
until this repository takes issues, feedback on it goes where the README's
[Getting help](../../README.md#getting-help) says.
