<!-- Generated. Do not edit by hand. -->

# Family 6 — Steps, content and content policy

Generated from the contract fragments in `operations/`. Edit a fragment, not this file.

*The programme* · 18 operations · contract 2.0.0

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
| `step.list`<br>*questions:* 16 | "What does this campaign actually send, in what order, and which steps owe a human?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `E9` |
| `step.add`<br>*questions:* 16 | "Add a step" — channel, execution mode, kind, position, delay with unit and basis, optional declared intent, personalisation mode and budget, content binding, threading, and for a branch its condition, evaluation window and early-satisfaction behaviour. | `act`<br>*conditional:* `control` on a campaign that is not live; `act` on a live one, with magnitude equal to the enrolments at or past the insertion point. | `irreversible`<br>*conditional:* reversible while not live; irreversible once a touch has gone. | `confirm_once`<br>*conditional:* auto while not live; confirm_once while live, with a mandatory statement of what happens to in-flight enrolments.<br>*artefact:* in-flight statement | the step list for a step already created under this key, and the campaign's live state. | `none` | `required` | `required` | `E7`, `E9`, `E10`, `E11`, `E12`, `E15` |
| `step.update` | "Change a step" — delay, content binding, intent, personalisation, threading, branch condition, or whether it is enabled at all. Never its position. | `act`<br>*conditional:* `control` while not live, `act` while live. | `reversible` | `confirm_once`<br>*conditional:* auto while not live; confirm_once while live with the in-flight statement.<br>*artefact:* in-flight statement | read the step; a setter is last-write-wins | `none` | `required` | `required` | `E11` |
| `step.remove` | "Delete a step" — destroys its variants and their measurement history and can advance everyone waiting on it sooner than they expected. | `act`<br>*conditional:* `control` while not live, `act` while live. | `irreversible` | `confirm_once`<br>*artefact:* in-flight statement | the step list — an absent step is a completed removal, not a failure. | `none` | `required` | `required` | `E11` |
| `step.reorder` | "Put the new step at position 2" — moves steps without delete-and-re-add, so variants, template bindings and history survive. | `act`<br>*conditional:* `control` while not live, `act` while live. | `reversible` | `confirm_once`<br>*artefact:* in-flight statement | the step list, compared by position | `none` | `required` | `required` | `E10`, `E11` |
| `variant.add` | "Try a second version of this step's copy", optionally with a split share — an unsettable split is reported, never silently assumed to be even. | `act`<br>*conditional:* `control` while not live, `act` while live. | `reversible` | `confirm_once`<br>*conditional:* auto while not live, confirm_once while live.<br>*artefact:* in-flight statement | list variants for one already created under this key | `none` | `required` | `required` | `E10` |
| `variant.update` | "Change a variant's content, its split share, or whether it is enabled" | `act`<br>*conditional:* `control` while not live, `act` while live. | `reversible` | `confirm_once`<br>*conditional:* auto while not live, confirm_once while live.<br>*artefact:* in-flight statement | read the variant; last-write-wins | `none` | `required` | `required` | — |
| `variant.remove` | "Delete this version" — destroys its measurement history, so a variant under test is disabled rather than removed. | `control` | `irreversible` | `confirm_once` | list variants — absence is a completed removal | `none` | `required` | `required` | — |
| `variant.list` | "What versions of this step exist, which are enabled, and at what share?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `template.create` | "Save this copy so other campaigns can use it" — creates the template and its first draft version; a draft cannot be sent. | `control` | `reversible` | `auto` | list templates for one already created under this key | `none` | `required` | `required` | — |
| `template.update` | "Change the copy" — always produces a new draft version, never mutates a published one. | `control` | `reversible` | `auto` | the template's version list for a draft created under this key | `none` | `required` | `not_applicable` | `E13` |
| `template.get` | "Show me this template" — the content of each version, which version is published, when each was published or retired, and which live campaigns bind each version. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | `E13` |
| `template.list` | "What content do we have, and what state is it in?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `template.publish` | "Make this version sendable" — a published version is immutable. | `control` | `compensatable` | `confirm_once` | the template's published version | `none` | `required` | `not_applicable` | `E13`, `L3` |
| `template.retire` | "Stop anyone using this version" — refused while live campaigns bind it unless a replacement is named; history stays readable. | `control` | `compensatable` | `confirm_once` | the version's state and its dependents | `none` | `required` | `required` | `E13` |
| `content_policy.define` | "State a rule our copy must obey" — never mention pricing, always carry the identification block, never claim a customer by name, never use a competitor's trademark — each with its scope and whether a breach blocks or warns. | `control` | `reversible` | `confirm_once`<br>*departs:* raised above the derived auto: a rule written here blocks or permits every send in its scope, so relaxing one — or scoping it to everything by accident — changes what may leave without any other operation being called. | list the rules for one already created under this key | `none` | `required` | `required` | — |
| `content_policy.list` | "Which content rules apply here, and which of them block rather than warn?" | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `not_applicable` | — |
| `content_policy.check` | "Does this draft break any of them?" — takes a rendered message and returns the rules that fired, where, and whether each blocks or warns. | `read` | `reversible` | `auto` | nothing at stake | `none` | `none` | `required` | `E14` |
