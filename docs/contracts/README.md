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
`workitem.create`, and the arguments the item carries under `context.input` are checked against that
operation's argument schema, all but the one reserved key that [`args`](#the-input-a-plugin-receives) names — there,
and again whenever a patch rewrites them. At the claim the item is
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
  context/<key>.json               a reserved key of a campaign's context, a fifth family, embedded the same way
  context/<key>.md                 the same key explained, with a generated block
  fixtures/context/<key>/value-*.json   values, accepted and refused, with the pointer and reason of each refusal
  journal/<kind>.json              a kind of the runtime's own journal lines, a sixth family, embedded the same way
  fixtures/journal/<kind>/body-*.json   bodies, accepted and refused, with the pointer and reason of each refusal
  subscriptions/<name>.json        the subscription contract, a seventh family, embedded the same way
  subscriptions/<name>.md          the same contract explained, with a generated block
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
| [`audience.availability`](operations/audience.availability.md) | `capability.list` | `read` | `auto` | `safe` | `not_used` | whether this account can search now, and what it has left |
| [`audience.filter_values`](operations/audience.filter_values.md) | `vocabulary.list` | `read` | `auto` | `safe` | `not_used` | the values one of the provider's filters may take, matching some text |
| [`audience.preview`](operations/audience.preview.md) | `segment.preview` | `read` | `auto` | `safe` | `not_used` | count and sample an audience before anything is spent on it |
| [`audience.search`](operations/audience.search.md) | `candidate.promote` | `control` | `confirm_once` | `after_recovery_read` | `not_used` | find people matching a definition, and make them ours |
| [`audience.search_cancel`](operations/audience.search_cancel.md) | `job.cancel` | `control` | `auto` | `after_recovery_read` | `not_used` | stop a search the provider is running; what it found stays found |
| [`audience.search_status`](operations/audience.search_status.md) | `job.get` | `read` | `auto` | `safe` | `not_used` | how far a search has got, and the people it found since the last read |
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
| [`message.cancel`](operations/message.cancel.md) | `scheduled_message.cancel` | `control` | `auto` | `safe` | `not_used` | stop a send the provider still holds for its moment |
| [`message.send`](operations/message.send.md) | `message.send` | `act` | `confirm_each` | `after_recovery_read` | `find_or_create` | send a person a message, outside any campaign, from one of the provider's accounts |
| [`message.status`](operations/message.status.md) | `scheduled_message.list` | `read` | `auto` | `safe` | `not_used` | read one send by the reference the provider gave it |
| [`optout.record`](operations/optout.record.md) | `optout.record` | `control` | `auto` | `after_recovery_read` | `find_or_create` | record that somebody said stop |
| [`schedule.list`](operations/schedule.list.md) | `schedule.resolve` | `read` | `auto` | `safe` | `not_used` | the provider's sending schedules, with the windows each one sets |
| [`sender.list`](operations/sender.list.md) | `sender.list` | `read` | `auto` | `safe` | `not_used` | the provider's accounts a send can go from |
| [`social_invitation.send`](operations/social_invitation.send.md) | `social_invitation.send` | `act` | `confirm_each` | `after_recovery_read` | `find_or_create` | invite a person to connect, from one of the provider's accounts |

<!-- END GENERATED operations -->

## The audience family

Six operations find people for a campaign at the provider, under the noun `audience`. The envelope — how many people,
how many of one company, whether people without an email count, whether phones are wanted — is the documents'. The
**definition** inside it — which industries, titles and company sizes, as the provider names them — is the provider's
own shape, which the plugin declares (below). A person comes back in one shape whoever the provider:
`external_id`, names, title, company (name, domain, size, industry), location, the channels the provider holds, and
`raw`, the provider's own record as JSON text of at most 16 KiB, which a plugin trims rather than exceed and which
nothing in the runtime reads.

- [`audience.preview`](operations/audience.preview.md): how many people a definition matches, or an audience the
  campaign holds by its number, and a sample of them with no contact detail. A read on request (below), free by
  contract and approved by nobody; a provider that charges for the look leaves it out of its manifest.
- [`audience.search`](operations/audience.search.md): find up to `count` people matching the definition and make them
  the campaign's. It is the spending act: a work item, `confirm_once` and `metered`, charged per person found by the
  provider's own tariff. Like every write it answers one item: `finished` with every person found, where the provider
  searches inside one call, or `accepted` with the provider's reference, by which the search is read and stopped. A search reaches nobody, it buys
  records, so it is `reach: control` and runs through a provider block. A person confirms the first search under an
  audience, and the campaign's allowance, a standing approval capped in people, may then let the top-ups under it go
  without the question ([`docs/work-execution.md`](../work-execution.md), "Standing approvals").
- [`audience.search_status`](operations/audience.search_status.md): how far a search has got, and the people it found
  from the cursor on. A work item whose search the runtime composes, reference and cursor, and a caller never writes;
  the runtime makes one itself on a cadence for each search it follows, and lands the people of every read that succeeds
  as the campaign's members, whoever made it ([`docs/campaign-manager.md`](../campaign-manager.md) §10).
  `in_progress` is read again. `finished` (the count asked for was reached) and `exhausted` (the source ran out) are
  terminal, and the search is read no more. `paused` (a person stopped it at the provider), `paused_for_credits` (the
  account ran out of credits) and `paused_for_limit` (the plan's limit of contacts was reached) hold the search until a
  person acts at the provider, so it may move again and is read again.
- [`audience.search_cancel`](operations/audience.search_cancel.md): stop a search; what it found stays found. It
  answers `cancelled`, or `already_ended` for a search that had already ended, which is a success; a repeat after a
  lost answer reads the search first. A running search
  keeps spending after a campaign stops, so the stop carries the protective floor: approved by nobody, and run in a
  paused campaign and through a provider block.
- [`audience.filter_values`](operations/audience.filter_values.md): the values one of the provider's filters takes,
  starting with some text. A read on request, which a provider may not offer.
- [`audience.availability`](operations/audience.availability.md): whether the account may search now, a reason when
  not, and its credits and headroom where the provider can say. A read on request; a provider that can say nothing
  beyond "allowed" answers `available: true` with the rest null.

**The definition is the provider's, declared in its manifest.** A plugin that performs `audience.search` or
`audience.preview` declares the schema of a definition under **`audience_definition`** in `plugin.yaml`
([`docs/plugins.md`](../plugins.md) §3): a schema in the [dialect](#the-schema-dialect) below, every property described,
and beside it, optionally, `keys` — the side of a person each property filters on, and the filter whose values
`audience.filter_values` lists for it. The loader holds the schema to the dialect as it holds a binding schema, refuses
the key on a plugin that performs neither operation, and refuses a plugin that performs `audience.search` without
`audience.search_status` or without `audience.search_cancel` (`audience_family_incomplete`). A definition is checked against the routed plugin's schema at
the claim (`definition_invalid`, right after `input_invalid`) and at the preview, never at `workitem.create`, where the
route, and so the schema, is not resolved yet. The dialect expresses the shapes a provider's filters take: arrays of
`{value, type}` pairs with `type` one of `include` and `exclude`, enumerations, nested ranges of a `minimum` and a
`maximum`, a switch between `or` and `and`, and "at least one filter" as an `anyOf` of `required` branches.

**An audience by its number.** `audience.search` and `audience.preview` take an audience the campaign holds by its
number, `args.audience`, or a definition, `args.definition`: exactly one of the two. The runtime composes the root
`definition` from either (below). The runtime numbers a campaign's audiences, 1, 2, 3, as the claim of each one's first
search makes it ([`docs/campaign-manager.md`](../campaign-manager.md) §10), and a number the campaign holds none under is
refused `audience_unknown`, at the claim and at the preview.

**A campaign's reads are routed by the campaign's route.** `audience.preview`, `audience.filter_values` and
`audience.availability` are read when a person or an agent asks, outside any work item, routed by the campaign's route
for the operation and held to the route's half of the claim ([`docs/routing.md`](../routing.md), "A read on request").
`operation.describe` resolves the same route and answers what the operation takes there: its argument schema, and the
routed plugin's declared definition — `jason operation describe audience.search --campaign <id>`.

**The accounts a send can go from are read on request too.** `sender.list` is also read when a person or an agent asks
for a campaign, outside any work item, routed by the campaign's route for `sender.list` itself and held to the same half
of the claim ([`docs/routing.md`](../routing.md), "A read on request"); the same document is the contract of both, and
the operation stays work as well.

## The delivery contract

One more document is published here, and it is not a canonical operation:
[`notifications/notification.send.json`](notifications/notification.send.md), what a notification plugin is invoked
for — put a pointer in front of a person: how many approvals and questions wait, whether protective actions failed,
accounts have events Jason did not read, provider blocks are open or items wait on a launch budget, and where to look. It is a family of
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

## The subscription contract

A seventh family is published here, and it is none of the others:
[`subscriptions/subscription.handle.json`](subscriptions/subscription.handle.md), what a plugin of any kind is invoked
for when it subscribes to a journal kind and the person granted it — take one line of that kind after the runtime wrote
it, with the envelope the previous handler of the kind's chain returned, and answer the envelope for the next. The line
records what is already done, so no work item names a handler and no route carries one; it is under `subscriptions/`
and read by a catalog of its own, for the reason the delivery and events contracts are. It has their fields and nothing
more: its input, its result, two failure codes — the ones the runtime records against an answer that breaks the
contract, since a handler fails under codes of its own — its timeout, which the plugin's own limit lowers, and its
conformance list. The operation word a handler is invoked under is the line's kind, not this document's id, and a
handler pins nothing. Its fixtures live beside the others' under
[`fixtures/subscription.handle/`](fixtures/subscription.handle) and are run by the same tests.

## The provider block

A failure of the class `blocked` says the provider refused the call for a reason outside the work item — the account's
money or plan, an allowance it counts, a credential it no longer accepts, a channel disconnected or paused — and did
nothing for it. Every published operation declares it under one code, `provider_blocked`, and the failure carries a
block: what is blocked, the account or one sender; why, from a closed list; where a person clears it; and, where the
provider says, when it clears by itself; an open block is cleared only by the provider's judgment of the work. The
block is written once, for every operation, in [`block/provider_block.json`](block/provider_block.json), explained on
[its page](block/provider_block.md), embedded into the runtime the same way, and its value fixtures under
[`fixtures/block/`](fixtures/block) are run by the same tests. The events and delivery families declare no block.

## A capability the provider does not offer yet

A provider ships its API on its own cadence, and a plugin may already call a part of it the provider does not offer yet:
an operation whose route the provider's API does not have, or an argument it does not take. Every operation answers that
with one code, `capability_not_offered`, class `permanent`: a failure code reserved beside every operation and declared
by none, which the runtime passes under that class alone. The work item ends `failed` as on any permanent failure, and
the message names the operation or the argument that is missing. Being reserved, the code clears no [provider
block](#the-provider-block). Nothing is detected in advance. For a route the provider's API does not have, the
provider's answer to the call is the detection. For an argument, a provider that refuses what it does not know answers,
and that answer is the detection; a provider that ignores it gives nothing to read, so a package sends a new argument
only once it knows the provider takes it: by what it knows of the provider, or by a read it makes before the call. The
runtime falls back to nothing: the code is final, and what to do instead is the agent's to choose, another way or
telling the person. When a plugin answers it is [the plugin guide, section 8](../plugins.md#8-failure-classes). Each
capability a provider may lack for a while is a line below: the operation or the argument, and what an agent does
instead where it is not offered.

- `message.send` and `social_invitation.send` with `send_after`: where the provider does not offer it, leave it out and
  create the item with `not_before` at the moment instead; it is claimed once the moment has passed and, being
  `confirm_each`, waits for a person's approval then. A refusal recorded `ambiguous` says an earlier attempt of the
  item may have reached the person: the new item is created only once the provider has said that attempt did not act.
- `message.status` and `message.cancel`: where the provider does not offer them, a send that ended `accepted` is known
  only as accepted. Say so rather than guess whether it went, and read the conversation for what the person received;
  a send that cannot be cancelled is stopped, if at all, at the provider by a person.
- `audience.preview`: where the provider does not offer a free look, nothing says in advance how many people a
  definition matches. Say so, and ask for a search whose `count` is the most the person will pay for: it is an upper
  bound, and the search's `found` says how many there were.
- `audience.filter_values`: where the provider does not offer it, write the values as the person gave them and as the
  provider's own documentation names them. A value the provider does not hold matches nobody, or is refused
  `definition_rejected`; ask the person rather than guess another.

## The named preferences

A fourth family is published here, and it is no work at all: the **named preferences**, structured facts about the
client that a person states, each with a schema, so that a skill can check that one is set before it writes and a
screen can show it. Five are published:

- [`agent.interactive_host`](preferences/agent.interactive_host.md) — the agent host `jason session open` starts for
  the person;
- [`send_mode.cold`](preferences/send_mode.cold.md) — whether what is sent to a contact who has not answered is
  released without asking, unless something puts it to a person first, or goes to a person first, each one;
- [`send_mode.warm`](preferences/send_mode.warm.md) — the same for a contact who has answered;
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

## The reserved context keys

A fifth family is published here, and it is no work either: the **reserved keys of a campaign's context**. A campaign's
context is the caller's own JSON object, and the runtime reads none of it, except the keys this family publishes, each
with a schema the runtime holds its value to whenever the key is written. Every reserved key of a campaign's context
gets one document here, and a key that is not published here is the caller's own. One is published:

- [`goals`](context/goals.md) — what the campaign is for: the outcomes its roles steer each conversation towards.

A document is `{key, version, summary, value_schema}`, with `distinct_by` beside them for a list whose items a member
tells apart, and nothing else: no work item names a key, no route carries one and no plugin is invoked for one, so it
is under `context/` and read by a catalog of its own. A key is one lower-case word of letters, digits and underscores,
starting with a letter, at most 64 characters, and its document is named for it. The value is checked against the
key's `value_schema` in the dialect below, and then, where the document names `distinct_by`, no two items of the list
may share that member: the later one is refused at its member's pointer as `unique_items`. It is checked at
`campaign.create` and at every `campaign.update_context` whose `set` names the key, as `context.input` is checked at
`workitem.create` and `workitem.update`: a refusal is `validation_failed`, each problem at the field `context.<key>`
followed by its pointer, with the code `invalid`; a JSON `null` under the key reads as the key absent, and a patch that
does not name the key never checks it again. A key's schema changes only by gaining an optional field, which raises
the document's version; anything else is a new key. Its fixtures live under
[`fixtures/context/`](fixtures/context) and are run by the same tests.

**A second reserved key of a work item's context, beside `context.input`.** A drafted reply may name the verdict it
answers under `context.reply_verdict`: the `jrn_` id of a `reply_classified` line. It is checked at `workitem.create`,
and at `workitem.update` whenever a patch names it or `input`: the work is a `conversation.reply` or a `message.send`,
and the line is a verdict of the same campaign about the thread the reply answers in, or, for a message, about the
same person. Anything else is refused as `validation_failed` at the field `context.reply_verdict`. The key is what a
standing approval reads at the claim, and nothing else reads it
([`docs/work-execution.md`](../work-execution.md), "Standing approvals").

**A third, the runtime's own: `context.audience` on a search.** The claim of an `audience.search` that it does not
refuse records there the audience the search runs under, `{"id": "aud_…", "number": 2, "definition": {…}}`, with the
definition the search is searched by, whichever way its arguments named the audience
([`docs/campaign-manager.md`](../campaign-manager.md) §10). `workitem.create` and `workitem.update` refuse a search that
writes the key or removes it, as `validation_failed` at the field `context.audience`; on any other work the key is the
caller's own.

**A reserved journal line beside the goals.** A role records its verdict on a reply — the category it read, its
confidence, the reaction it chose and the goal it steers to — through the Runtime API's `reply.record`
([`docs/runtime-api.md`](../runtime-api.md)), which writes one reserved journal line,
[`reply_classified`](journal/reply_classified.json), whose document states what the line carries. The line is the
runtime's: `journal.append` refuses it to every other writer, and it never carries a word a person wrote: no subject,
no body, no preview, no name. The runtime checks that the category is one non-empty line of text
and nothing more; what a category means is the skills' business, never the runtime's. Where the category is a
person's answer to a question about the reply, `reply.record`'s optional `decision_id` argument names that decision;
the runtime checks at the call that it is the campaign's (`decision_not_found`), answered (`decision_not_answered`) by a
named person (`decision_not_human`), with the category as the chosen option (`decision_option_differs`), and raised by
the attempt that recorded a verdict on this reply (`decision_not_about_reply`), and the line then carries the
decision's id and the person's actor.

**Three reserved lines beside it.** A person's word in advance about one category of verdict in one campaign is a
standing approval: `approval.set_standing` writes [`standing_approval_set`](journal/standing_approval_set.json) and
`approval.unset_standing` writes [`standing_approval_unset`](journal/standing_approval_unset.json), each keyed by the
category, and the dispatcher writes [`approval_standing_applied`](journal/approval_standing_applied.json), keyed by the
line that set it, each time it lets a draft go under one. All three are the runtime's, refused by
`journal.append` to every other writer, and none carries a word a person wrote.

## The journal's kinds

A sixth family is published here, and it is no work either: the **kinds the runtime reserves for its own journal
lines**. A line the runtime writes under one of its kinds — `campaign_paused`, `workitem_succeeded`, `reply_classified`
and the rest — is a fact it recorded, and every kind it reserves gets one document under [`journal/`](journal), named
for the kind, with its fixtures under [`fixtures/journal/`](fixtures/journal). A kind a person or an agent appends
under a name of its own through `journal.append` has no document here and is never checked against one. The runtime
answers these documents through `journal.kinds` (`docs/runtime-api.md`), each entity's `at` always a list there.

A document is `{kind, version, since, summary, subscribable, entities, field_since, body}` and nothing else:

- `kind` — lower-case letters, digits and underscores, starting with a letter, at most 64 characters; the file is
  named for it.
- `version` — the document's own, a whole number from 1.
- `since` — the release that first wrote the kind, a full version `X.Y.Z`.
- `summary` — one line, ending with a full stop, saying what a line of the kind records.
- `subscribable` — whether a program may subscribe to lines of the kind.
- `entities` — which things a line names, and where. Each is keyed by one of fifteen names (`campaign`, `project`,
  `contact`, `work_item`, `attempt`, `approval`, `role`, `plugin`, `account`, `decision`, `report`, `profile`,
  `provider_block`, `event_source`, `backfill`) and is `{at, presence, each?}`: `at` is a JSON pointer into the line as
  `journal.list` returns it, either one of its columns (`/campaign_id`, `/work_item_id`, `/attempt_id`, `/contact_id`)
  or a place in its body (`/key`, `/new/...`), or a list of such pointers where a line may name the entity in more than
  one place (`routes_updated` names its plugin at `["/new/plugin", "/old/plugin"]`), each held to the same rules and
  the entity named by every one that holds a value, and never an empty list; `presence` is `always` or `sometimes`;
  and `each: true` says a pointer names an array of ids, one per item.
- `field_since` — a map from a pointer into the body to the release that added the field there, for every field a
  release after `since` added; a line written before that release does not have it. In such a pointer `*` stands for
  any index of an array (`/new/plugins/*/subscriptions`), and names the field in every item the array's `items`
  describes; a `*` where the body describes no array is refused.
- `body` — the schema of the line's body in the dialect below: the object `{key, old, new}`, each member always
  present and JSON `null` where the line has nothing. The schema requires all three and allows nothing else, and a
  member a kind always leaves empty is `{"type": "null"}`. What every line carries whatever its kind — its id, its
  moment, its actor and the reason the actor gave — is outside the body and the same for every kind.

**What holds a line to its document.** Whenever the runtime writes a line of a reserved kind, its body is checked
against the document's schema, and every column the document says is `always` named must be set. In the test suite a
line off its document is refused, so a writer that drifts from its document fails the test that drives it. In an
installation the line is written and the runtime's log names its kind and nothing else: a line the runtime meant to
keep is never lost to its own check. A pattern that does not finish in time is no verdict on a line: in either mode the
line is written and nothing is said of it.

**How a kind changes.** A kind the runtime starts writing arrives with its document in the same pull request. A body
grows by optional fields, and by a branch added to an `anyOf`, only, each field named in `field_since` with the release
that added it; a field never changes meaning, and no field is removed. A kind is never renamed or retired while a
release that writes it is supported. A closed list of words in a body may gain a word when a later release writes one,
so a line written before then holds only the words that release knew. A test reads the documents and body fixtures of
the last published release (at a release's own tag, the release before it) and refuses a document removed, a `since`
changed, a body field removed or newly required (a field the release did not describe is not newly required of the body
where only a branch added to an `anyOf` the release already had requires it, or only an object the release did not
describe, since no body the release published could have needed it), a `field_since` entry or an entity dropped, an
entity once named on every line now named on some or changing `each`, a kind no longer subscribable, or a body the
release published as valid that is refused now. A kind may become subscribable.

Every kind this build reserves, rendered from the documents by a test and compared whole:

<!-- BEGIN GENERATED journal-kinds -->
<!-- Rendered from each kind's document. Edit those; this table follows them. -->

| Kind | Since | Subscribable | Names | What a line records |
|---|---|---|---|---|
| [`approval_approved`](journal/approval_approved.json) | `0.1.0` | yes | campaign, contact, work_item, approval | A person approved parked work, so it goes back to the queue to run. |
| [`approval_automatic_applied`](journal/approval_automatic_applied.json) | `0.42.0` | no | campaign, contact, work_item, approval | The automatic mode in force for a send's contact released the send without asking a person, a pass of its text by the critic or by a person standing, and the line names the approval that records the release, where the mode came from, whether the contact was cold or warm, and the review that released it. |
| [`approval_cancelled`](journal/approval_cancelled.json) | `0.1.0` | yes | campaign, contact, work_item, approval | A decision was ended because the work it was about was cancelled or passed its due date. |
| [`approval_rejected`](journal/approval_rejected.json) | `0.1.0` | yes | campaign, contact, work_item, approval | A person rejected parked work, so it ends as failed. |
| [`approval_requested`](journal/approval_requested.json) | `0.1.0` | yes | campaign, contact, work_item, approval | Work was parked to wait for a person's decision, and the line names the approval that asks for it; its reason is the approval's, which says why the work waits, a send mode's code among them. |
| [`approval_standing_applied`](journal/approval_standing_applied.json) | `0.25.0` | yes | campaign, contact, work_item, approval | A standing approval released work without asking a person, a drafted reply under a category's or a search under the allowance, and the line names the approval that records the release, the standing approval, and the verdict the reply answers or the audience and the people the search asks for. |
| [`approval_superseded`](journal/approval_superseded.json) | `0.1.0` | yes | campaign, contact, work_item, approval | A decision stopped applying because the subject of the work it was about changed before the work ran, and a new decision is asked in its place: by the claim in the same save, or by the next claim, where an edit of the work's input withdrew a question still pending or a doubting review ended an approval that no longer stood for the work as it would now go. |
| [`audience_people_landed`](journal/audience_people_landed.json) | `0.36.0` | yes | campaign, contact, work_item, plugin | One page of a search's people landed in its campaign: what the page held, whom it made members, and whom it made contacts of the directory; one line per page that carried anybody. |
| [`audience_reserve_low`](journal/audience_reserve_low.json) | `0.36.0` | yes | campaign | A campaign's untouched members fell under the threshold the campaign set, said once per crossing; a default manager trigger. |
| [`audience_reserve_recovered`](journal/audience_reserve_recovered.json) | `0.36.0` | yes | campaign | A campaign's untouched members are back at or above the threshold it set, after a line said they had fallen under it; not a trigger. |
| [`audience_search_ended`](journal/audience_search_ended.json) | `0.36.0` | yes | campaign, work_item, plugin | A search for a campaign's audience ended — finished, exhausted, stalled, stopped, or found gone by a read or a stop — with what it was asked for, found and landed; said once per search, a default manager trigger. |
| [`audience_search_held`](journal/audience_search_held.json) | `0.36.0` | yes | campaign, work_item, plugin | A search for a campaign's audience is held at the provider — paused, out of credits or at the plan's limit — with what it was asked for, found and landed; said when a read first reports the hold, and again only after the search moves and holds again; a default manager trigger. |
| [`campaign_archived`](journal/campaign_archived.json) | `0.1.0` | yes | campaign | A campaign was archived for good: its open work and its pending questions are cancelled and it never runs again, but for the stop of each search still running. |
| [`campaign_created`](journal/campaign_created.json) | `0.1.0` | yes | campaign | A campaign was created as a draft, under the name the line carries. |
| [`campaign_paused`](journal/campaign_paused.json) | `0.1.0` | yes | campaign | A campaign stopped being active, so none of its work is dispatched until it is started again. |
| [`campaign_started`](journal/campaign_started.json) | `0.1.0` | yes | campaign | A campaign became active, from a draft or from a pause, so its work can be dispatched. |
| [`campaign_updated`](journal/campaign_updated.json) | `0.1.0` | yes | campaign, project | One setting of a campaign changed: its name, its execution profile, its review cadence, its audience threshold, the project it is in, or its own send mode for cold or for warm contacts; a change of several settings writes one line for each, and a send mode set again by another person writes one whose old and new are the same word, under its new setter. |
| [`contact_archived`](journal/contact_archived.json) | `0.1.0` | yes | contact | A person was archived in the contact directory, so nothing reaches out to them any more and their open work is cancelled. |
| [`contact_created`](journal/contact_created.json) | `0.1.0` | yes | contact | A person was added to the contact directory. |
| [`contact_updated`](journal/contact_updated.json) | `0.1.0` | yes | contact | A person's details in the contact directory changed; the line names the fields that changed and never their values. |
| [`contacts_added`](journal/contacts_added.json) | `0.1.0` | yes | campaign, contact | One request added people to a campaign: the line counts what happened to its items and names the people it added. |
| [`contacts_removed`](journal/contacts_removed.json) | `0.1.0` | yes | campaign, contact | One request removed people from a campaign, which cancels their open work there: the line counts what happened to its items and names the people it removed. |
| [`context_updated`](journal/context_updated.json) | `0.1.0` | yes | campaign | One key of a campaign's context was set, changed or removed; a change of several keys writes one line for each. |
| [`decision_answered`](journal/decision_answered.json) | `0.1.0` | yes | campaign, contact, work_item, attempt, decision | A person answered a question an attempt raised, and the review that continues the work is summoned from this line; the answer is kept on the decision. |
| [`decision_cancelled`](journal/decision_cancelled.json) | `0.1.0` | yes | campaign, contact, work_item, attempt, decision | A question still waiting for a person was cancelled because its campaign was archived. |
| [`decision_raised`](journal/decision_raised.json) | `0.1.0` | yes | campaign, contact, work_item, attempt, decision | An attempt raised a question it cannot answer itself, and the question waits for a person; what was asked is kept on the decision. |
| [`events_backfill_requested`](journal/events_backfill_requested.json) | `0.12.0` | yes | plugin, event_source, backfill | A span of an account's provider events was asked to be read again. |
| [`events_gap_observed`](journal/events_gap_observed.json) | `0.12.0` | yes | plugin, event_source | A span of an account's provider events was older than the store reads back, so it went unread and is said as a gap. |
| [`events_read`](journal/events_read.json) | `0.12.0` | yes | plugin, event_source, backfill | One read of an account's provider events ended, with what it read and stored and the failure it ended with, if any. |
| [`external_effect_reported`](journal/external_effect_reported.json) | `0.1.0` | yes | campaign, contact, work_item, report | Somebody reported an effect that happened outside Jason, and the report was admitted. |
| [`external_id_diverged`](journal/external_id_diverged.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | A provider returned an identifier different from the one already pinned: the pin stands, and the value it returned is recorded beside it. |
| [`external_id_origin`](journal/external_id_origin.json) | `0.19.0` | yes | campaign, contact, work_item, attempt | A plugin said how the provider came to hold the entity whose identifier was just pinned or diverged: found already there, or created by the attempt. |
| [`external_id_pinned`](journal/external_id_pinned.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | A provider's identifier for a campaign or a contact was pinned for the first time, by the attempt that returned it, and is never overwritten. |
| [`global_routes_updated`](journal/global_routes_updated.json) | `0.42.0` | no | plugin | A global route was set or taken away by route.set or route.unset, so an operation, one channel of an operation, or all provider work goes to the plugin it names for every campaign with no route of its own, or no longer does. |
| [`inbound_reply_observed`](journal/inbound_reply_observed.json) | `0.4.0` | yes | campaign, contact, work_item, attempt, plugin | A conversation thread of a campaign moved because the other side wrote, as the inbound read of the campaign or a read of the account's provider events found it. |
| [`message_sent_observed`](journal/message_sent_observed.json) | `0.22.0` | yes | campaign, contact, work_item, plugin | A read of a send found that the message went out, with the moment it went and the provider's identifier for it. |
| [`notification_sent`](journal/notification_sent.json) | `0.2.0` | yes | none | A notification pointing at what waits for a person went out, with the counts it carried and what each channel did with it. |
| [`plugin_grants_updated`](journal/plugin_grants_updated.json) | `0.42.0` | no | plugin | A plugin was granted something its manifest requests, or a grant was taken back, by plugin.grant or plugin.revoke: that capability's grants in force before and after, as the reload froze them. |
| [`plugins_reloaded`](journal/plugins_reloaded.json) | `0.1.0` | yes | plugin | A load of the plugins was activated, at startup or by a reload: the line names every package then active at its digest, and the routes frozen with it. |
| [`preference_set`](journal/preference_set.json) | `0.14.0` | yes | none | A person set a named preference, recorded by the value's size and hash and never the value. |
| [`preference_unset`](journal/preference_unset.json) | `0.14.0` | yes | none | A person took a named preference's value back, recorded by the size and hash of the value it had. |
| [`profile_created`](journal/profile_created.json) | `0.1.0` | yes | profile | An execution profile was created at its first revision, naming the agent host the work it runs is launched on. |
| [`profile_disabled`](journal/profile_disabled.json) | `0.1.0` | yes | profile | An execution profile was disabled, so no attempt is launched under it until it is enabled again. |
| [`profile_enabled`](journal/profile_enabled.json) | `0.1.0` | yes | profile | A disabled execution profile was enabled again, so attempts may be launched under it. |
| [`profile_revised`](journal/profile_revised.json) | `0.1.0` | yes | profile | An execution profile was changed: the line names the fields the change named and the revision current after it, never their values. |
| [`project_created`](journal/project_created.json) | `0.14.0` | yes | project | A project was made, with its name and the slug it keeps. |
| [`project_linked`](journal/project_linked.json) | `0.15.0` | yes | project | A project's folder, or the folders of its campaigns, were recorded again where they now stand. |
| [`project_updated`](journal/project_updated.json) | `0.14.0` | yes | project | A project's name changed. |
| [`provider_block_cleared`](journal/provider_block_cleared.json) | `0.18.0` | yes | campaign, attempt, provider_block | An open provider block that had held work of a campaign cleared, through a trial that got past it or by a person. |
| [`provider_blocked`](journal/provider_blocked.json) | `0.18.0` | yes | campaign, work_item, attempt, plugin, provider_block | An open provider block held work of a campaign, said once in each campaign it holds work of. |
| [`provider_event_observed`](journal/provider_event_observed.json) | `0.12.0` | yes | campaign, contact, plugin | A provider event read from an account reached a campaign: a reply the provider detected, an invitation accepted, or a bounce. |
| [`reply_classified`](journal/reply_classified.json) | `0.25.0` | yes | campaign, contact, work_item, attempt, plugin, decision | A reply in a campaign was given a verdict: what kind of reply it is, how sure the verdict is, and what is to be done about it. |
| [`role_added`](journal/role_added.json) | `0.1.0` | yes | role | A role was registered, so work can name it and an attempt can run as it. |
| [`role_note_set`](journal/role_note_set.json) | `0.1.0` | yes | campaign, project, role | A role's note at one level, a campaign, a project or the client, was written; the line records its size and hash, never what it says. |
| [`role_updated`](journal/role_updated.json) | `0.1.0` | yes | role, profile | The execution profile a role's work runs under was set, changed or cleared. |
| [`routes_updated`](journal/routes_updated.json) | `0.1.0` | yes | campaign, plugin | A campaign's route was set or taken away, so an operation, one channel of an operation, or the campaign's default, goes to the plugin it names or back to the routes above it. |
| [`standing_approval_set`](journal/standing_approval_set.json) | `0.25.0` | yes | campaign | A person gave a campaign a standing approval for one category of reply, or its allowance for the searches under one of its audiences, or replaced the one in force, so the work it covers goes without a decision each time. |
| [`standing_approval_unset`](journal/standing_approval_unset.json) | `0.25.0` | yes | campaign | A person took back a campaign's standing approval for one category of reply, or its allowance for searches, so the work it covered waits for a decision again. |
| [`subscription_cursor_rewound`](journal/subscription_cursor_rewound.json) | `0.29.0` | no | none | A person moved a subscribed kind's cursor back, so the kind's chain hands its plugins again every line after the new cursor; every rewind is a line of its own. |
| [`subscription_failed`](journal/subscription_failed.json) | `0.29.0` | no | plugin | A subscriber's handler ended in a final class for one line, so the chain of the line's kind ended there for that line and the kind moved on. |
| [`suppression_added`](journal/suppression_added.json) | `0.1.0` | yes | none | An address or number was put on the do-not-contact register, so nothing reaches it on its channel. |
| [`suppression_removed`](journal/suppression_removed.json) | `0.1.0` | yes | none | An address or number was taken off the do-not-contact register, which puts it back in reach. |
| [`workitem_awaiting_approval`](journal/workitem_awaiting_approval.json) | `0.1.0` | yes | campaign, contact, work_item | A queued work item was parked to wait for a person's approval before it may run. |
| [`workitem_cancelled`](journal/workitem_cancelled.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | A work item was cancelled before it finished, together with its live attempt, if it had one. |
| [`workitem_context_updated`](journal/workitem_context_updated.json) | `0.1.0` | yes | campaign, contact, work_item | One key of a work item's context was set, changed or removed; a change of several keys writes one line for each. |
| [`workitem_created`](journal/workitem_created.json) | `0.1.0` | yes | campaign, contact, work_item | A work item was created; the line says what it was asked to be, so where the work came from is answered without reading the item. |
| [`workitem_expired`](journal/workitem_expired.json) | `0.1.0` | yes | campaign, contact, work_item | A work item nobody had picked up passed its due date and expired; a new due date can reopen it. |
| [`workitem_failed`](journal/workitem_failed.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | A work item ended as failed: an attempt failed with nothing left worth trying, or a person rejected the work while it waited for their approval. |
| [`workitem_processing`](journal/workitem_processing.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | An attempt the dispatcher opened for a work item started running. |
| [`workitem_released`](journal/workitem_released.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | A work item went back to the queue to be claimed again: its attempt failed in a way worth another attempt, a restart found its attempt never started, or the question it was waiting on ended, because a person approved it or because an edit of the work's input withdrew it. The `approval_approved` or `approval_superseded` line written in the same save tells which, and is the record of a person's decision; the line's `reason` is free text and decides nothing. |
| [`workitem_reopened`](journal/workitem_reopened.json) | `0.1.0` | yes | campaign, contact, work_item | An expired work item was given a new due date and went back to the queue. |
| [`workitem_resumed`](journal/workitem_resumed.json) | `0.18.0` | yes | campaign, contact, work_item, attempt | A waiting work item went back to the queue: what it waited for happened, its bound came, its provider block cleared or let a trial through, or a person resumed it. |
| [`workitem_reviewed`](journal/workitem_reviewed.json) | `0.42.0` | no | campaign, contact, work_item, attempt | A send was reviewed before it went: by a run of the campaign's role work other than the one that made it, or by a named person, the review bound to the text it read by the review hash and passing it or doubting it with a code from a closed list. A doubt's sentence is never on the line. |
| [`workitem_scheduled`](journal/workitem_scheduled.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | The dispatcher claimed a work item and opened an attempt for it, numbered by the line. |
| [`workitem_succeeded`](journal/workitem_succeeded.json) | `0.1.0` | yes | campaign, contact, work_item, attempt | An attempt succeeded and its work item finished as succeeded; the line says whether the item now carries a result. |
| [`workitem_updated`](journal/workitem_updated.json) | `0.1.0` | yes | campaign, contact, work_item | One setting of a work item changed: when it may start, when it is due, its priority, its limits, its execution profile or its result format; or, since 0.42.0, the item was marked as written in the desktop's chat, the first time its input was edited there. A change of several settings writes one line for each. |
| [`workitem_waiting`](journal/workitem_waiting.json) | `0.18.0` | yes | campaign, contact, work_item, attempt, provider_block | A work item stopped to wait for other work items to end, for a moment, or for a provider block to clear, and is not claimed until what it waits for happens or its bound comes. |

<!-- END GENERATED journal-kinds -->


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
| `channel_arguments` | the arguments a provider may offer on some of the operation's channels and not on others, which a plugin takes only where its manifest lists them per channel ([`docs/plugins.md`](../plugins.md) §3), and which the claim refuses on any other channel (`argument_unsupported`); null where there are none. The loader holds each name to be an argument the arguments declare and a caller may leave out, named once, of an operation whose `preflight.channel` is `from_args` |
| `channel_templates` | the channels on which the operation's message may be a template the provider fills in for the person, which a plugin writes one on only where its manifest lists the channel under `templates` ([`docs/plugins.md`](../plugins.md) §3), the claim refusing a template elsewhere as `template_not_offered`: a non-empty list, or null where the message is never one. The loader holds each to be a channel the arguments offer, named once, of an operation whose `preflight.channel` is `from_args` and whose `message` declares `compose` |
| `route_channel_argument` | the argument whose value is the channel a route may name under the operation, so that one operation's calls on one channel go to one plugin and on another to another: `channel` on `message.send` and `conversation.reply`, `sender_channel` on `sender.list`, null on every other operation. It is routing metadata — nothing a plugin receives changes, so it keeps the operation's version — and the channels a route may name are that argument's `enum`. The loader holds it to be an argument the arguments declare, written as a string with an `enum`, every value of it a channel the channel rules know, spelled as they keep it, and `channel` itself where `preflight.channel` is `from_args`. A caller may leave the argument out where the arguments allow it, as `sender.list`'s filter: such a call names no channel and is routed by the operation alone. An argument that is the address a person is found by, an open pattern, is never one. A route that names a channel under the operation is resolved by this field, and the levels, the settings and the checks are in [`docs/routing.md`](../routing.md) |
| `dispatch` | how the dispatcher treats the operation's work beyond what all work shares: `in_paused_campaign` is `waits` — the work waits with its paused campaign, as all work does — or `runs`, allowed only where `reach` is `control` and `approval` is `auto`, for an operation whose not running is the harm; `in_archived_campaign` is `cancelled` — archiving the campaign cancels the work, as it cancels all open work — or `runs`, allowed only where `in_paused_campaign` is `runs`, for a stop without which the provider goes on spending: the work is created, kept and claimed in an archived campaign, today `audience.search_cancel` alone; `under_provider_block` is `waits` — the claim parks the work on an open provider block of its plugin and binding, and it goes back to the queue when the block clears — or `runs`, the work going through an open block and a `blocked` answer ending it. An operation whose `reach` is `act` waits; a block's trial goes to its `act` work first, and a control's answer never clears a block |
| `contact_projection` | exactly which contact fields, and which channels, the plugin receives — `consumed`, the one channel the arguments name, or `none` — declared so that widening it is a visible change. It names exactly what the operation's implementation reads, the least of a person that reaches a plugin's process, for the fields as for the channels |
| `precondition` | what must be true at the provider before the operation can act, beyond how its contact is found, in one sentence |
| `preview` | what a person deciding on the operation reads first — a one-line summary rendered from the composed input, and the values in that input it highlights — or `null` where nobody is asked to decide |
| `input_schema` | the whole composed input the plugin receives, in the dialect below |
| `output_schema` | what a succeeded result must look like |
| `external_ids` | the kinds of provider identifier the operation may return, and what each identifies: a `contact` or a `campaign`, which the runtime pins, or the `work_item` itself — the reference of the act the item made the provider take, such as `send` on the two sends — which pins no entity, is kept on the attempt and in the item's result, and is said in the result at `items[0].reference` as the one value |
| `recovery_read` | what to read before repeating after a lost answer, and why the reading is worth the round trip; a reading that rests on something the provider keeps for a while, such as an idempotency key, is bounded by the provider's own window, counted from `context.unsettled_since`, the moment the earliest attempt that may have acted was claimed. `by_key: true`, absent where it is false, says the reading settles a lost answer by the work item's key — today on `message.send` and `social_invitation.send` — which only an operation that repeats after its recovery read may say, and the runtime then repeats a lost answer only under a plugin whose manifest lists the operation under `settles_by_key` |
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
  present even when the operation takes no arguments, as `{}`. One key of `args` is reserved for every operation and
  declared by none: **`provider`**, the provider's and its skills' own object, which a caller may place beside the
  arguments the document declares. The runtime never reads it: it holds the key to being a JSON object whose own
  member names are distinct, as every object a request carries must be, and to nothing else, refusing any other kind
  with `type` at the key's own pointer, sets it aside before the operation's argument schema applies, and hands it to
  the plugin exactly as written. A package that reads it and cannot make sense of what it holds fails the item before
  any call with **`provider_input_invalid`**, class `validation`: a failure code reserved beside every operation
  and declared by none, which the runtime passes under that class alone, as it passes the plugin host's own codes
  under theirs, and refuses under any other as a code the operation does not declare. A reserved code is not the
  provider's judgment of the work, so it clears no [provider block](#the-provider-block). The code is for this key
  only: a package that cannot read an argument the operation's document declares has a defect of its own, or has
  found one in the document, and no code stands for either.
- `contacts` appears when `preflight.contact` is `required`, or when it is `optional` and the work item names a
  contact. It carries exactly one person — an operation about a person addresses one person in this version, and a
  read about a person reads about one — and is cut to the declared projection: only the channel the operation consumes,
  or none, never the person's whole reachability.
- `campaign` is Jason's record of the campaign the work item belongs to. A read made on a person's request — their
  threads, or one thread's messages — belongs to no work item and carries none; the documents of the operations it
  performs say the campaign may go unnamed.
- `idempotency_key` appears only when the operation requires one, and it is the **work item's** identifier: stable
  across every attempt, which is what makes "the prior run under this key" answerable.
- `send` appears where the operation's input schema declares it — today `message.status` and `message.cancel`. The
  runtime composes it from the succeeded attempt of the send `args.send` names: that work item, its operation and the
  provider's reference for the send. A caller never writes it.
- `search` appears the same way — today `audience.search_status` and `audience.search_cancel` — composed from the
  succeeded attempt of the search `args.search` names: that work item and the provider's reference for the search,
  returned under `external_ids.search`, and, for the status read, the `cursor` the next read starts at: the plugin's
  own, as the search's row keeps it from the last read that answered one, never parsed, and null to read from the start.
  A caller never writes it.
- `definition` appears at the root of `audience.search` and `audience.preview`: the audience's definition, in the
  provider's own shape, composed from `args.definition`, or, for an audience named by its number, from the audience the
  campaign holds. `args` is the caller's as written and the runtime never rewrites it, so the definition is placed at
  the root, as `send` is, and the plugin reads it there and nowhere else.
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
- `example` is added wherever the input's message is a template (`/args/message/compose` is `template`): `{subject,
  body, unfilled}`, the template rendered by [the template standard](#the-template-standard) over the composed input,
  each name read where the table of names says the input holds it on the message's channel. A name the input does not
  hold there, and a name it holds with no value, renders its fallback, or else itself in brackets, as
  `[sender.first_name]`; it is listed under `unfilled` once, in the order it is read — a held name only when it had no
  fallback, since the provider then sends nothing. A condition on a name the input does not hold takes its other branch in the example, where the provider
  may take the first, and the name is listed. The example renders text and escapes nothing. It is the runtime's
  rendering, not the provider's, which may differ — in what it counts as no value, and in what it makes of markup in a
  field.
- Every pointer is checked when the document loads. The walk steps through plain `properties` by name and `items`
  by index, and refuses a name a closed object does not allow, an index past `maxItems` and a step into a value
  whose only type is a scalar; a value that may be a scalar or something else is not refused.
  What lies behind a `$ref`, an `anyOf` or an `allOf` is proven by the preview fixtures instead: every pointer has to
  name a value in at least one of them.

The claim renders the preview when it parks the work, from the subject's own input, and stores it beside the subject:
the approval's `preview` starts with `summary`, `highlights` and, for a template, `example`, and
`jason approval get --human` prints the summary first, above the operation. **The preview is never part of what is
approved:** the subject carries the composed input — beside the operation and its version, the plugin, the account and
the work item — and not the summary or the example, so its hash does not change when a summary is worded differently or
a template rendered otherwise. An operation nobody is asked to decide on
declares `preview: null`, and every `auto` operation does.

Where the parked work names a verdict under `context.reply_verdict` and no standing approval covered it, the preview
also carries `standing_approval`: `{verdict, category, why}` — the verdict's `jrn_` id, its category, and the first
condition that did not hold, one of the codes [`docs/work-execution.md`](../work-execution.md) lists under "Standing
approvals". It tells the person deciding why they were asked; like the rest of the preview, it is not part of what
is approved.

## Pages

A read that can answer more items than one result may hold — `enrollment.list`, `conversation.list`,
`conversation.get`, `sender.list` and `schedule.list` — answers a page of them by an offset, and every such read pages
the same way; `audience.search_status` pages by a cursor, the last point below. `engagement.summarize` answers
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
- One canonical operation pages by a cursor: `audience.search_status`, by the plugin's own cursor, which the runtime
  composes into the input as `search.cursor` and never parses — null to read from the start, otherwise the one the last
  read answered — and which the result answers as `cursor`, the same one back when nothing moved,
  with `has_more` saying whether more people are readable now. `args.limit` is required and there is no `offset`, and the
  page is held to the same two bounds: `people.maxItems` is the same number as `limit`'s maximum, and one person always
  fits under 768 KiB. The other operations page by offset, as the providers mapped first do, and
  [`divergence-from-l1.md`](divergence-from-l1.md) records where the catalogue says otherwise. The events contract is
  the other read that pages by a cursor: one its plugin returns and nothing else reads, with a page of at most 100
  events held to the same 768 KiB, and one event always fits.

## The schema dialect

Schemas are JSON Schema 2020-12 — every schema carries
`"$schema": "https://json-schema.org/draft/2020-12/schema"`, so any conforming validator reads them unchanged,
with the two deviations noted below —
restricted to a published vocabulary. A keyword outside it is refused rather than ignored, because a keyword the
runtime would silently skip is a rule that does not run.

**The vocabulary.** `$schema`, `$id`, `$defs`, `$ref` (local `#/$defs/<name>` only), `type`, `properties`,
`required`, `additionalProperties` (boolean only), `enum`, `const`, `minLength`, `maxLength`, `pattern`, `minimum`,
`maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`, `items`, `minItems`, `maxItems`, `uniqueItems`,
`anyOf`, `allOf`, `not`, `format` (for `date-time` and `jason-template`, and nothing else), `title`, `description`,
`examples` (carried, never enforced).

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

Seven details worth knowing before you write a schema:

- **Combinators report once.** `anyOf`, `allOf` and `not` report a single problem at their own pointer rather than
  every branch's problems; the message names what the first failing branch objected to, and `anyOf` and `allOf` say
  it in that branch's own words. Listing every road not taken buries the one thing a caller has to change. The
  exception is a branch no pattern could finish measuring: unless a measured branch has already decided the
  combinator, its `pattern_timed_out` is passed up as it stands, at its own pointer, because a branch nobody measured
  gives the combinator no verdict to report.
- **An explicit `null` satisfies `required`.** Saying null is saying something; only an absent property is missing.
  That is a rule about a property *inside* a document being validated, and it is not the rule about the work item's
  reserved `context.input` key, where an absent key and a JSON null read alike — neither is a set of arguments.
- **`additionalProperties: false` sees only its own object.** It is evaluated against the properties named in the
  same schema object, and does not see properties contributed by an adjacent `$ref` or by an `allOf` branch. A
  2020-12 validator evaluates it against everything annotated at that location, so a schema that combines
  `additionalProperties: false` with `$ref` or `allOf` is read differently here and elsewhere — which is one place
  the portability above does not hold. Write the properties in the same object as the keyword, and the two
  readings agree.
- **`format` is asserted, and `jason-template` is this dialect's own.** A value a schema gives `"format": "date-time"`
  is held to ISO-8601 with an offset, and one it gives `"format": "jason-template"` to
  [the template standard](#the-template-standard) below; either fails as `format`. JSON Schema 2020-12 reads `format`
  as an annotation unless a validator is told otherwise, and no other validator knows `jason-template`, so another
  validator accepts a template this one refuses: the second place the portability above does not hold.
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

## The template standard

A text the provider fills in for each person — today a `message.send` email, LinkedIn message or InMail whose `compose`
is `template` — is written in this standard and in nothing else. It is Jason's own, so one template reads the same to
every plugin that renders it, and a plugin turns it into its provider's own syntax. This section is the standard's
only statement: the runtime holds a template to it wherever a schema names `"format": "jason-template"`, and a plugin
parses the same grammar.

- **The grammar is total.** Every template is accepted, or refused for the first tag that holds a refused construct,
  reading from the start, or for a refused construct in the text before it — with that construct's reason and the
  character it starts on: `format`, at the pointer of the string. Inside a combinator, as the `anyOf` that lets only
  some formats' messages be a template, the refusal is the combinator's, at the combinator's pointer, and its message
  names the string's pointer, the construct and the character. A refusal never quotes the template: a template is
  somebody's message.
- **A tag is `{{` … `}}`, spelled exactly as the table writes it:** one space between its parts and none inside the
  braces. Everything outside a tag is text, sent as written.
- **A name is one of the names below.** What counts as no value, and how two values compare, is the provider's; [the
  Reply package's README](../../plugins/reply/README.md) says Reply's. A name with no value and no fallback cannot be
  filled, and the message is not sent. The approval's example counts a value that is null, empty or white space as no
  value — a fallback is written in its place, an `#if` takes its other branch, and a comparison reads it as empty text —
  and compares a value with a quoted text exactly, character for character. The message sent may take another branch
  than the example shows, and the provider may fill a name the example lists as unfilled.
- **A quoted text** — a fallback, or the right side of a comparison — is written between single quotes, may be empty,
  and holds neither `'` nor `}}`.
- **A condition** holds `{{else}}` at most once, and nests inside another at most eight deep.
- **A template is text.** It escapes nothing and holds no markup of its own: a value is written as the person's field
  holds it, and what a provider then does with it is the operation's to say.

### What a template may write

| Construct | Treatment | Why |
|---|---|---|
| any text no other row names | accepted | sent as written; a single `{` or `}` is text too |
| `{{name}}` | accepted | the name's value; a name with no value leaves the provider nothing to write, and the message is not sent |
| `{{name \| 'text'}}` | accepted | the name's value, or the quoted text when it has none |
| `{{#if name}}` ... `{{/if}}` | accepted | what it holds when the name has a value |
| `{{#if name == 'text'}}`, `{{#if name != 'text'}}` | accepted | a condition on the value being, or not being, the quoted text |
| `{{else}}` | accepted | once inside a condition: what is written when it does not hold |
| `{{{` | refused | a template escapes nothing, so there is no second kind of value |
| `{{!` | refused | nobody reads it, and the providers do not all drop it |
| `{{>` | refused | it names text the provider keeps, which nobody approving reads |
| `{{#each`, `{{#with`, `{{#unless`, any other `{{#` and any other `{{/` | refused | only `#if` is read alike by the providers |
| `{{else if` | refused | the providers mapped first have none; write an `#if` inside `{{else}}` |
| a word before a name, as `{{toLower name}}`, and a second `\|`, as `{{Random \| 'a' \| 'b'}}` | refused | it is a provider's own computation, which another provider does not have |
| `@prompt[[` | refused | the provider would write in a model's answer, which nobody approved |
| `=`, `<>`, `<`, `>`, `<=`, `>=` inside a tag | refused | the providers mapped first read them differently, or not at all |
| `&&`, `\|\|` inside a tag | refused | the providers mapped first have none; write one `#if` inside another |
| a name after `\|`, `==` or `!=` | refused | a fallback and the right side of a comparison are quoted text, so what they say is what was approved |
| a name the table of names does not list | refused | the provider would fill it with a value of its own, or fail it |
| a `'` with no `'` after it in its tag, or one followed by anything but white space or `}}` | refused | a quoted text runs to the next `'`, so it holds neither `'` nor `}}` |
| a tag spaced otherwise, as `{{ name }}` or `{{name\|'text'}}` | refused | each tag has one spelling: one space between its parts and none inside the braces |
| any other tag, as `{{}}` or `{{#if}}` | refused | the standard has no such tag |
| an `{{#if` never closed, a `{{/if}}` or `{{else}}` outside a condition, and a second `{{else}}` | refused | a provider would render it its own way, or refuse it |
| an `{{#if` inside eight others | refused | eight levels hold any message a person reads |
| `{{` with no `}}` before the end or before the next `{{` | refused | a provider would read the text after it as part of the tag |
| `}}` outside a tag, or a `}` right after one | refused | a provider would read it as the end of a tag |
| `\{{` | refused | the providers mapped first have no escape for `{{`, so a template cannot hold the two braces as text |

### The names a template may use

`contact.*` is the person the message goes to; `sender.*` is the account it goes from, as the provider holds that
account — its identity at the provider, not a preference about how a sender signs. The last two columns say, for each
channel a template is written on, what the composed input holds of each name: the pointer the approval's example reads
it at, or `not held` where the input does not hold it, and the provider alone fills it from what it holds
([the preview a person reads](#the-preview-a-person-reads)).

| Name | What it is | On an email | On LinkedIn |
|---|---|---|---|
| `contact.first_name` | the person's first name | `/contacts/0/first_name` | `/contacts/0/first_name` |
| `contact.last_name` | the person's last name | `/contacts/0/last_name` | `/contacts/0/last_name` |
| `contact.company` | the person's company | `/contacts/0/company` | `/contacts/0/company` |
| `contact.title` | the person's title | `/contacts/0/title` | `/contacts/0/title` |
| `contact.email` | the person's email address, which on an email is the address the message goes to | `/contacts/0/channels/0/value` | not held |
| `contact.custom.<key>` | the field the provider's account holds for the person under `<key>`: a letter or `_`, then at most 63 letters, digits and `_` | not held | not held |
| `sender.first_name` | the sending account's first name, as the provider holds the account | not held | not held |
| `sender.last_name` | the sending account's last name, as the provider holds the account | not held | not held |
| `sender.name` | the sending account's full name, as the provider holds the account | not held | not held |
| `sender.address` | the sending account's address, which on LinkedIn is its profile | `/args/sender/address` | `/args/sender/address` |

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
  host fails where it can be seen rather than being read as something else. A new optional input field that changes
  how other fields are read, as `message.send`'s `compose` changes how the subject and the text are read, is additive
  only when every plugin published under the family before the field existed refuses the input it would misread — as
  the Reply package before 0.10.0 refused any `{{`; otherwise it is breaking.
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
is a plugin outcome exactly as a child writes it and `expect` is `{status, class?, package_settles_by_key?, retriable, invalid?, pinned?, block?, held?}`.
`status` is `succeeded`, `failed`, or `result_invalid` — a well-formed outcome whose result does not satisfy the
output schema, which pins an identifier of a kind the operation never declared, or whose failure carries a code the
operation does not declare under that class. `package_settles_by_key` says whether the package that answered lists the
operation under `settles_by_key`; a failed fixture states it exactly where the operation settles a lost answer by the work
item's key and the failure is `ambiguous`, since only there does `retriable` rest on it. `pinned` is what the runtime
writes into its record of provider identifiers from that answer, as `{kind: value}`. A **preview fixture** is
`{operation, version, case, input, expect}`, where `input` is a composed input the operation's schema accepts and
`expect` is `{summary, highlights, example?}`: the line the template renders, the values it highlights and, where the
message is a template, its example, each compared whole; a fixture without `example` holds the preview to having none.
An operation has preview fixtures exactly when it declares a preview. A **value fixture** of a named preference
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
document declares, under the class the document declares it with, or a code reserved beside every operation, under
the class it is reserved with. A fixture may not teach a plugin author a code no operation accepts, nor the same code
under a class that would have the runtime repeat what the document calls final.
The runtime holds every failed answer to the same list: a code the document does not declare, or a declared code under
another class, is `result_invalid`, with the detail at `/error/code` or `/error/class` — except the plugin host's own
codes, which belong to no operation and pass under the class the host writes them with
([the plugin guide, section 8](../plugins.md#8-failure-classes)); the events family alone refuses those as well.
The reserved `provider_input_invalid` passes the same way, under `validation` alone
([the input a plugin receives](#the-input-a-plugin-receives)), and an events read refuses it too.
The code of [a capability the provider does not offer yet](#a-capability-the-provider-does-not-offer-yet) passes the
same way, under `permanent` alone, and an events read refuses it too.

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
any other class states no `block`. A fixture recorded `blocked` also states `held`: `true` where its operation's
`dispatch.under_provider_block` is `waits`, so the item waits on the block and is released when it clears, and `false`
where it is `runs`, so the failure ends the item; no other fixture states it.

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
