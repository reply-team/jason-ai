<!-- Generated. Do not edit by hand. -->

# Family 16 — Social and messaging channels

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*Permission and capability* · 13 operations · contract 2.0.0

**Reading the table.** † marks a core operation. The five properties are *reach*
(`read` changes nothing · `control` changes state we own · `act` reaches the outside world),
*reversibility* (`reversible` · `compensatable` · `irreversible`), *approval*
(`auto` · `confirm_once` · `confirm_each`, derived from reversibility × reach), *before repeating*
(the observable state to read before running it again) and *cost* (`none` · `metered`, with its
basis and the meter it consumes). *Key* and *per-item* are the two standing obligations: an
idempotency key on every act, every metered call, every write accepting a collection and every
write creating a durable object. A write that takes no key says why in *exempt* — `absolute_valued`
(the same value written again), `terminal_state` (the same state arrived at again), or `unstated`,
which is the contract admitting it has no reason and not a claim that one exists. And one outcome
per item whenever a collection is carried. Where a property is conditional the cell holds the
dangerous reading, with the condition beneath it.

| Operation | Intent | Reach | Reversibility | Approval | Before repeating | Cost | Key | Per-item | Invariants |
|---|---|---|---|---|---|---|---|---|---|
| `social_invitation.send` | "Connect with them first, then talk" — a connection request, with an optional note. | `act` | `compensatable` | `confirm_each`<br>*departs:* raised: the note is content addressed to a named person. | the invitation state for this pair: none / pending / accepted / withdrawn / declined. | `metered`<br>*basis:* per invitation<br>*meter:* `invitation` | `required` | `required` | — |
| `social_invitation.list` | "What is still pending, and how old is it?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `social_invitation.withdraw` | "Take one back" — and it does not lift a restriction. | `control` | `compensatable`<br>*conditional:* compensatable, subject to a channel-declared cooling-off before the same person may be invited again. | `confirm_once`<br>*conditional:* auto for one invitation; confirm_once for a collection. | the invitation's state. | `none` | `required` | `required` | `F5` |
| `social_invitation.accept` | "Accept an invitation someone sent us". | `act` | `compensatable` | `confirm_once`<br>*artefact:* preview | the invitation's state. | `none` | `required` | `required` | — |
| `social_invitation.decline` | "Decline one we do not want". | `control` | `compensatable` | `auto` | the invitation's state. | `none` | `required` | `required` | — |
| `social_relationship.get` | "What is our standing with this person — connected, pending, none — and at what degree?" | `read` | `reversible` | `auto` | nothing at stake | `metered`<br>*conditional:* metered on channels that meter profile reads; none elsewhere.<br>*basis:* per person looked up, on channels that meter profile reads<br>*meter:* `profile_read` | `required`<br>*conditional:* required where the read is metered. | `required` | `F12` |
| `social_profile.view` | "Open their profile" — it spends a counted allowance, and on some channels they can see that we did. | `act`<br>*conditional:* `act` where the channel surfaces the view to the person; `read` where the operator has declared that it does not. Where the operator has declared neither, absence resolves to `act`. | `irreversible` | `confirm_each`<br>*conditional:* confirm_each while it resolves to `act`; auto where it resolves to `read`. Bulk profile research is exactly the case a standing approval exists for. | the view ledger for this person and sender in the current window. | `metered`<br>*basis:* per profile viewed<br>*meter:* `profile_read` | `required` | `required` | `F12` |
| `social_profile.follow` | "Follow them without connecting". | `act` | `compensatable` | `confirm_once`<br>*artefact:* preview | the follow state for this pair. | `none` | `required` | `required` | — |
| `social_profile.unfollow` | "Stop following". | `control` | `compensatable` | `auto` | the follow state for this pair. | `none` | `required` | `required` | — |
| `social_post.react` | "React to something they posted". | `act` | `compensatable` | `confirm_once`<br>*artefact:* preview | the reaction state for this post and sender. | `none` | `required` | `required` | — |
| `social_post.comment` | "Comment on something they posted" — published content under a named human's identity. | `act` | `compensatable` | `confirm_each`<br>*departs:* raised from confirm_once: it is published content under a named human's identity. | the comment ledger for this post and sender. | `none` | `required` | `not_applicable` | — |
| `social_credit.get` | "How many metered messaging credits are left, when do they expire, and does a reply give one back?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `conversation_window.get` | "May I write to this person freely right now, or only with pre-approved content, or only by spending a credit?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `F12` |
