# Divergence from the vendor-neutral operations contract

The three operations Jason publishes are seeded from a vendor-neutral contract of SDR business operations,
published as an open skill pack at contract version **2.0.0** — the first level, the business layer that sits above
any provider, which is what the `l1` field in each document and the `l1` in this file's name refer to. Jason implements a strict subset of it, and adds
fields of its own about execution. This is the list of every place the two differ.

Three kinds of entry appear here. A **restriction** is Jason doing less than the contract allows, deliberately. An
**addition** is a field the contract has no opinion about because it is not a contract-level concern. A **gap** is
somewhere the contract names a thing without defining it, and Jason had to choose — those are the ones worth
feeding back, because the next implementer will otherwise choose differently. Feedback travels through the public
repository's issues, as an issue per row, not as a private note.

| Aspect | What the contract says | What Jason v0 does | Why | Feed back? |
|---|---|---|---|---|
| Collection cardinality | `list_membership.add` and `campaign.enroll` accept a collection and return one result per item | The same shape, restricted to exactly one item (`minItems` = `maxItems` = 1) | One work item addresses one contact, so a batch is a planning concern rather than an operation. The shape is kept so that lifting the restriction later needs no change of shape | No — a restriction |
| Per-item refusals | A partial success is reported per item | With one item, a refusal by the provider is a failed outcome with its class; item statuses are only `added \| already_member` and `enrolled \| already_enrolled` | With a cardinality of one there is no partial success to report, and a failure class is what the runtime can act on | No — follows from the restriction |
| Who supplies the idempotency key | The key is `required`; by whom is not said | The runtime supplies the work item's own identifier | It is stable across attempts, which is exactly what "the prior run under this key" needs, and it means no caller can forget one | No |
| Approval | `campaign.enroll` is `confirm_once`, carried by a preview artefact | The dangerous reading is published under `approval.value`, which is what a gate will read. Nothing reads it yet: no `provider_op` is executed at all in this version, and there is no preview artefact | The dangerous reading is what a gate must read, and both the gate and the preview arrive later. Refusing everything meanwhile is the only honest answer | No — no disagreement |
| `accepts_collection` on `campaign.get` | Not stated; the stated default is `true`, while `per_item_results: not_applicable` is the contract's own positive assertion of arity one | Jason writes `false` | The two readings contradict each other on this row, and a read of one campaign plainly carries one object. The reading that matches the operation was chosen | **Yes** — state it in the fragment rather than leaving it to a default |
| `collision` vocabulary | An explicit collision policy is required; its values are not named | `skip \| refuse` | An enrollment must not guess about a participation someone already has; two values cover "leave it" and "do not decide for me" | **Yes** |
| `start` vocabulary | An explicit start position is required; its values are not named | `{position: first_step \| step, step: integer ≥ 1}` | A position is either the beginning or a numbered step; nothing else is expressible without knowing the campaign | **Yes** |
| `first_touch` vocabulary | An explicit first-touch timing is required; its values are not named | `authored_delay \| next_open_window \| immediately` | Taken from the operation's own intent, which names those three timings in prose | **Yes** |
| `campaign.get` status vocabulary | The operation returns the campaign's state; no vocabulary is given | `draft \| live \| paused \| archived \| other`, with the provider's own word under `vendor` | A neutral vocabulary is what a planner can reason over, and `other` keeps it honest instead of forcing a wrong word | **Yes** |
| How a list is identified | Lists are an entity of the contract; no identifier form is given | `args.list.external_id` — the provider's own identifier, named by the planner | Jason has no list entity, so there is nothing local to name it by | **Yes** |
| `campaign.get` output coverage | Settings, senders, schedule and timezone policy, pacing, reply policy and the declared successor | The neutral result carries the identifier, name, status and counts; everything else is returned untranslated under `vendor` | Typing all of it neutrally would mean inventing a schedule model before any provider had been mapped. Nothing is lost, and the neutral part stays strict | **Yes** — the settings worth typing neutrally are worth naming upstream |
| The provider-contact precondition | Not present; the contract keeps operations free of execution detail | `precondition` states it: work by the pinned identifier, otherwise create the contact from the consumed channel and return the identifier | A list or a campaign holds the *provider's* contacts. Without this, every plugin would re-match people by address and quietly create duplicates | Only as an adapter-level note |
| `contact_projection` | Not present | Jason declares exactly which contact fields and which channels reach a plugin | A plugin should receive what the operation consumes, not a person's whole record; declaring it makes widening it visible | No — an execution concern |
| `repeat_after_ambiguous` and `recovery_read` | `before_repeating` names what to observe before running an operation again | Jason turns that observation into a rule a runtime can execute, and states the money argument for it | A dispatcher cannot read prose. The contract's sentence is the reason; this is the rule | No — an execution concern |
| `preflight`, `failure_codes`, `timeout_ms`, `conformance` | Not present | Jason's own fields | All four are about running the operation rather than defining it | No |
| Spelling | British spelling in the source (`enrolment`) | American spelling in identifiers and prose (`enrollment`, `enroll`) | This repository is written in American English throughout | No — not a divergence, recorded so nobody "fixes" it |

## Known gaps of these documents

Not every gap is a difference between the two contracts; this one is a place where neither of them
says anything, recorded here so that nobody reads the silence as a decision.

**No failure code names a pin that no longer resolves.** An identifier a plugin pinned — a contact, a
campaign — can be deleted, merged, or moved to another account at the provider, and from that moment
every operation that works by it is asking for something that is gone. Neither the vendor-neutral
contract nor these three documents declare a code for it, so a plugin has to answer with one of its
own: the worked example in this repository reports `contact_not_found` and classes it permanent.

Permanent is the honest class while nothing can repair the situation, and that is why the gap matters.
The repair is to drop the pin and match the person again — reconciliation, which is deliberately not
in this increment. Until it exists, an identifier that stops resolving turns every later operation on
that person into a failure with no way forward, and two plugins meeting the same situation will answer
it with two different words. Preventing exactly that is what a shared vocabulary is for, which is why
this one belongs upstream rather than in each plugin.
