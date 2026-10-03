# Divergence from the vendor-neutral operations contract

The operations Jason publishes are seeded from a vendor-neutral contract of SDR business operations,
published as an open skill pack at contract version **2.0.0** — the first level, the business layer that sits above
any provider, which is what the `l1` field in each document and the `l1` in this file's name refer to. Jason implements a strict subset of it, and adds
fields of its own about execution. This is the list of every place the two differ.

Three kinds of entry appear here. A **restriction** is Jason doing less than the contract allows, deliberately. An
**addition** is a field the contract has no opinion about because it is not a contract-level concern. A **gap** is
somewhere the contract names a thing without defining it, and Jason had to choose — those are the ones worth
feeding back, because the next implementer will otherwise choose differently. The contract now lives in this repository, in
[`skills/business/sdr-operations`](../../skills/business/sdr-operations/SKILL.md) — it was published in
[`reply-team/reply-skills`](https://github.com/reply-team/reply-skills), **formerly** its home — so feedback
travels through this repository's issues: an issue per row, not a private note.

| Aspect | What the contract says | What Jason v0 does | Why | Feed back? |
|---|---|---|---|---|
| Collection cardinality | `list_membership.add` and `campaign.enroll` accept a collection and return one result per item | The same shape, restricted to exactly one item (`minItems` = `maxItems` = 1) | One work item addresses one contact, so a batch is a planning concern rather than an operation. The shape is kept so that lifting the restriction later needs no change of shape | No — a restriction |
| Per-item refusals | A partial success is reported per item | With one item, a refusal by the provider is a failed outcome with its class; item statuses are only `added \| already_member` and `enrolled \| already_enrolled` | With a cardinality of one there is no partial success to report, and a failure class is what the runtime can act on | No — follows from the restriction |
| Who supplies the idempotency key | The key is `required`; by whom is not said | The runtime supplies the work item's own identifier | It is stable across attempts, which is exactly what "the prior run under this key" needs, and it means no caller can forget one | No |
| Approval | `campaign.enroll` is `confirm_once`, carried by a preview artefact | The dangerous reading is published under `approval.value`, and the claim reads it: an operation approved anything but `auto` is parked for a person before a plugin is started, with a preview and a content hash of exactly what is being approved. What remains narrower than the contract is the artefact's reach — a preview here describes one work item's effect rather than naming a population, because one work item addresses one contact | The dangerous reading is what a gate must read, and a preview over a population is the collection cardinality question above rather than a second disagreement | No — no disagreement |
| `accepts_collection` on `campaign.get` | Not stated; the stated default is `true`, while `per_item_results: not_applicable` is the contract's own positive assertion of arity one | Jason writes `false` | The two readings contradict each other on this row, and a read of one campaign plainly carries one object. The reading that matches the operation was chosen | **Yes** — state it in the fragment rather than leaving it to a default |
| `collision` vocabulary | An explicit collision policy is required; its values are not named | `skip \| refuse` | An enrollment must not guess about a participation someone already has; two values cover "leave it" and "do not decide for me" | **Yes** |
| `start` vocabulary | An explicit start position is required; its values are not named | `{position: first_step \| step, step: integer ≥ 1}` | A position is either the beginning or a numbered step; nothing else is expressible without knowing the campaign | **Yes** |
| `first_touch` vocabulary | An explicit first-touch timing is required; its values are not named | `authored_delay \| next_open_window \| immediately` | Taken from the operation's own intent, which names those three timings in prose | **Yes** |
| `campaign.get` status vocabulary | The operation returns the campaign's state; no vocabulary is given | `draft \| live \| paused \| archived \| other`, with the provider's own word under `vendor` | A neutral vocabulary is what a planner can reason over, and `other` keeps it honest instead of forcing a wrong word | **Yes** |
| How a list is identified | Lists are an entity of the contract; no identifier form is given | `args.list.external_id` — the provider's own identifier, named by the planner | Jason has no list entity, so there is nothing local to name it by | **Yes** |
| `campaign.get` output coverage | Settings, senders, schedule and timezone policy, pacing, reply policy and the declared successor | The neutral result carries the identifier, name, status and counts; everything else is returned untranslated under `vendor` | Typing all of it neutrally would mean inventing a schedule model before any provider had been mapped. Nothing is lost, and the neutral part stays strict | **Yes** — the settings worth typing neutrally are worth naming upstream |
| The provider-contact precondition | Not present; the contract keeps operations free of execution detail | `precondition` states it: work by the pinned identifier, otherwise create the contact from the consumed channel and return the identifier | A list or a campaign holds the *provider's* contacts. Without this, every plugin would re-match people by address and quietly create duplicates | Only as a plugin-level note |
| `contact_projection` | Not present | Jason declares exactly which contact fields and which channels reach a plugin | A plugin should receive what the operation consumes, not a person's whole record; declaring it makes widening it visible | No — an execution concern |
| `repeat_after_ambiguous` and `recovery_read` | `before_repeating` names what to observe before running an operation again | Jason turns that observation into a rule a runtime can execute, and states the money argument for it | A dispatcher cannot read prose. The contract's sentence is the reason; this is the rule | No — an execution concern |
| `preflight`, `failure_codes`, `timeout_ms`, `conformance` | Not present | Jason's own fields | All four are about running the operation rather than defining it | No |
| Paging | Not defined; `conversation.list` says "page with the returned cursor" | `limit` and `offset`, both required, with `has_more`. A page may come back short with `has_more: true` before the result would pass 768 KiB, and the next page starts at `offset` plus the items returned | The providers mapped first page by offset, and a short page is the only way a page of long text fits what the runtime accepts as one result | **Yes** — say how a page is asked for and continued |
| `enrollment.list` from the person's end | One read from either end: "who is in this campaign?" and "where is this person across everything?" | The campaign's end only | The person's end is a different provider call with a different shape; it is cheap to add later as an operation of its own | No — a restriction |
| `enrollment.list` status filter | Not stated | No status filter; every person carries a status | A filter applied after reading would break `offset` plus the items returned, because the provider's offset counts the unfiltered list | No — a restriction |
| `engagement.summarize` grouping | "grouped the way the question actually needs" | `group_by: "channel"`, the one grouping, stated by the caller | One grouping every provider can answer, named rather than inherited; another is additive | No — a restriction |
| Basis and denominator | Two runs are only the same number if as-of, window, basis, denominator and detection method all match; their values are not named | `basis: provider_reported`, `detection: provider`, and `denominator: unknown` unless the provider names it; `as_of` is the plugin's clock when it asked | The provider counted, and names neither the denominator of its rates nor the moment of its counts, so the envelope says so rather than inventing either | **Yes** — name the vocabularies of basis, denominator and detection |
| Rates | Not stated whether a summary returns rates | Counts only, from a closed list; a rate is never returned, and a count the provider reports beyond the list is not returned in this version | A rate is two counts and a denominator, and the denominator is exactly what is unknown. Adding a measure later is additive | **Yes** — name the measure vocabulary |
| `conversation.list` order | "ordered by what is most overdue" | The provider's own order, the thread that moved last first, stated in `vendor.ordering` | What is most overdue is a judgement over a thread, not something a provider sorts by | **Yes** |
| A thread's classification | "filterable by meaning" | Not read: the provider's category travels under `vendor`, untranslated | No neutral classification vocabulary exists yet, and a guessed one would have a planner reason over a word nobody defined | **Yes** |
| `conversation.list` filters | "filterable by meaning, channel, owner, campaign and how long each has been waiting" | A person or a campaign, `since`, and replies only or everything | Meaning is the classification row above; channel, owner and waiting time are filters this version does not take yet, and adding one is additive | No — a restriction |
| `enrollment.list` historical participations | "with historical participations included on request" | The participations the provider still lists, finished ones included; one it no longer lists is not served | The provider answers who a sequence holds now; a participation that ended and was removed is a different read | No — a restriction |
| `conversation.list` unread-only filter | Not stated | No unread-only filter; every thread carries `unread` | The same reason as `enrollment.list`'s status filter: a filter after reading breaks the page | No — a restriction |
| `enrollment.list` step | Not stated | `current_step` is the provider's own label for the step, a string, never a number | Providers number steps differently, and some do not number them at all; a label is what they all have | **Yes** — say what a position in a campaign is |
| A thread's person | Not stated | `{external_id, name}`: the provider's identifier, null once it holds the person no longer, and the name it shows — never an address | A list of threads says who each one is with, not how to reach them. The addresses on a thread's own messages come back from `conversation.get`, which reads that one thread, rather than being copied into every page of the list | **Yes** |
| `feature_unavailable` and `contact_not_linked` | No words for either | `feature_unavailable` (permanent): the account's plan or role does not include the capability. `contact_not_linked` (permanent): no provider identifier is pinned for the person, and a read creates nobody | `unauthorized`'s remedy, signing in again, would misdescribe a plan's limit; and a read must not create the person it was asked about | **Yes** — the catalogue lacks both words |
| `provider_answer_too_large` | No word for it | Permanent, on every read: the provider's answer was larger than the plugin may read from a program, so it was cut off | Asking again would be cut at the same place; reading it as a missing answer would repeat it until the attempts ran out | **Yes** |
| `per_item_results` written plainly | `engagement.summarize` writes `{value: required, detail: "one row per group, each with its own envelope."}` — a detail beside a plain value, with no `conditional`, as it does in 15 places | Written plainly as `required`; the detail is on the operation's page | Jason's loader reads a block only when it is conditional, because a block is how a document says its value has a condition; this one has none | **Yes** — say whether a detail beside a plain value is part of the contract |
| `accepts_collection` on the reads | Not stated; the stated default is `true` | `false` on the four reads, as on `campaign.get` | Each read names one campaign, one person or one thread; the default contradicts that | **Yes** — as for `campaign.get` |
| Spelling | British spelling in the source (`enrolment`) | American spelling in identifiers and prose (`enrollment`, `enroll`) | This repository is written in American English throughout | No — not a divergence, recorded so nobody "fixes" it |

## Known gaps of these documents

Not every gap is a difference between the two contracts. These are places where the contract above says
nothing, recorded here so that nobody reads the silence as a decision — and where Jason has had to name
something itself, the name is stated rather than left to each plugin.

**A pin that no longer resolves has a word here, and nowhere else.** An identifier a plugin pinned — a
contact, a campaign — can be deleted, merged, or moved to another account at the provider, and from
that moment every operation that works by it is asking for something that is gone. The two operations
that act on a person declare `contact_not_found` for it, as permanent, so that two plugins meeting the
same situation answer with the same word rather than inventing one each. The vendor-neutral contract
these documents are seeded from still says nothing about it, and that is where the gap remains: the
situation belongs to anyone who works by a provider's identifier, not to Jason.

What no code can supply is the way forward. Permanent is the honest class only because nothing can
repair the situation: the repair is to drop the pin and match the person again — reconciliation, which
is deliberately not in this increment. Until it exists, an identifier that stops resolving turns every
later operation on that person into a failure a plugin can name and a human has to resolve.

**A call that never became work has a word too.** `provider_call_failed`, permanent, on every
published operation: the vendor tool refused the call or never started, so nothing the operation asks for was
attempted and repeating it runs the same broken call. It is deliberately apart from
`provider_unavailable`, which is transient and means the provider was reachable enough to say "not
now".
