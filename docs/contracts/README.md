# Canonical operation contracts

A **canonical operation** is one unit of SDR work named in the practitioner's language, with no provider detail in
it: `campaign.get`, `list_membership.add`, `campaign.enroll`. A plugin implements operations; a planner composes
them; the runtime enforces them. Nothing in this package names a vendor, and nothing in it depends on one.

Each operation has exactly one source of truth: the JSON document under [`operations/`](operations). That file is
**embedded into `Jason.Contracts`** as a resource, so the contract the runtime applies is the very file you are
reading — there is no second copy to drift. The `.md` page beside it explains the operation for a person; the
generated block inside that page is rendered from the JSON by a test. **Prose explains; the generated block
governs, and the JSON governs the block.**

**What runs today.** The documents are published and embedded, and the runtime holds a `provider_op`
work item to the one it names: an operation no contract is published for is refused at
`workitem.create`, and the arguments the item carries under `context.input` are measured against that
operation's argument schema — there, and again whenever a patch rewrites them. Nothing routes a work
item to a plugin yet, so the composed input, the check on a plugin's answer and the repeat rule below
are the contract a plugin is written **against** rather than something the runtime performs; routing
arrives with the next increment. [`docs/plugins.md`](../plugins.md) §7 is the same material from the
plugin author's side, with a worked example.

```
docs/contracts/
  README.md                        this page
  divergence-from-l1.md            where Jason's stricter subset departs from the contract it is seeded from
  operations/<name>.json           the contract — one file, one version, embedded into the runtime
  operations/<name>.md             the same contract explained, with a generated properties block
  fixtures/<name>/input-*.json     argument vectors, valid and invalid, with the pointer and reason of each failure
  fixtures/<name>/outcome-*.json   answer vectors, with the status, class and retriability each implies
```

## What a document states

The first group of fields is the **vendor-neutral contract**, copied word for word from the SDR operations
contract this catalog is seeded from — recorded in each document under `l1`, for the first level: the business
layer that sits above any provider (see "Where these come from"): `reach`, `reversibility`, `approval`,
`approval_artefact`, `approval_departs`, `before_repeating`, `idempotency_key`, `per_item_results`,
`accepts_collection`, `cost`, `cost_basis`, `meter`, and the `invariants` that name the operation. Four of them —
`reach`, `reversibility`, `approval` and `cost` — may be a plain value or a **conditional block** of
`{value, conditional, detail}`, where `value` is always the *dangerous* reading. Every gate reads `value`, so a
condition can never soften one, and `detail` is what an agent reads to decide.

The second group is Jason's own, and it is about execution:

| Field | What it says |
|---|---|
| `preflight` | whether the work item's contact is needed (`required` / `not_used`) and where the channel comes from (`from_args` / `none`) |
| `contact_projection` | exactly which contact fields, and which channels, the plugin receives — declared so that widening it is a visible change |
| `precondition` | what must be true at the provider before the operation can act, in one sentence |
| `input_schema` | the whole composed input the plugin receives, in the dialect below |
| `output_schema` | what a succeeded result must look like |
| `external_ids` | the kinds of provider identifier the operation may return, and what each identifies |
| `recovery_read` | what to read before repeating after a lost answer, and why the reading is worth the round trip |
| `repeat_after_ambiguous` | `safe`, `after_recovery_read` or `never` — what may be done when an attempt ends without a usable answer |
| `failure_codes` | the codes a plugin may report, each under one of the four classes, with the moment it applies |
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
- `contacts` appears only when `preflight.contact` is `required`, carries exactly one person in this version, and
  is cut to the declared projection — only the channel the operation consumes, never the person's whole
  reachability.
- `campaign` is Jason's record of the campaign the work item belongs to.
- `idempotency_key` appears only when the operation requires one, and it is the **work item's** identifier: stable
  across every attempt, which is what makes "the prior run under this key" answerable.
- Every `external_ids` object is filtered to the calling plugin's own pins. Another provider's identifier is never
  visible.

## The schema dialect

Schemas are JSON Schema 2020-12 — every schema carries
`"$schema": "https://json-schema.org/draft/2020-12/schema"`, so any conforming validator reads them unchanged —
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
`additional_properties`, `any_of`, `all_of`, `not`, `format`, `schema_keyword_unknown`, `schema_ref_unresolved`,
`duplicate_property`, `number_out_of_range`, `schema_too_deep`, `schema_cyclic`, `schema_pattern_invalid`,
`schema_too_large`, `schema_number_not_finite`.

Three details worth knowing before you write a schema:

- **Combinators report once.** `anyOf`, `allOf` and `not` report a single problem at their own pointer rather than
  every branch's problems; the message names what the first failing branch objected to. Listing every road not
  taken buries the one thing a caller has to change.
- **An explicit `null` satisfies `required`.** Saying null is saying something; only an absent property is missing.
- **The same code validates untrusted schemas**, because a plugin's `binding` schema comes from its manifest.
  Patterns compile without backtracking and run under a match timeout, which rules out lookaround and
  backreferences; `$ref` resolves only inside its own document and never in a loop; nesting stops at 32 levels; a
  schema over 64 KiB is refused unread.

## Versioning

Every operation is at `version: 1`. A plugin's manifest declares the operation-contract **family** version under
`contracts.operations`, and a plugin that declares family version N implements version N of every operation it
lists.

- **Additive** — a new optional input field, a new optional output field, a new failure code, a new external-id
  kind — keeps the version.
- **Breaking** — a newly required field, a removed field, a changed enum, a changed property such as `reach` —
  bumps the operation's version *and* the family version. A plugin then declares `[1, 2]` to serve both.

A routed plugin whose family versions do not contain the operation's version is refused with
`contract_incompatible` — a check that arrives with routing, like everything else that calls a plugin.
Compatibility windows and deprecation policy are not settled yet and are deliberately not
implied here.

## Running the fixtures

Every file under `fixtures/` is executed by a test, which is what keeps these documents and the runtime honest.
From the repository root:

```
dotnet test --project runtime/tests/Jason.Contracts.Tests -- --filter-class "Jason.Contracts.Tests.OperationContracts.FixtureTests"
```

An **input fixture** is `{operation, version, case, input, expect}`, where `expect` is either the string `"valid"`
or `{"invalid": [ … ]}`. An **outcome fixture** is `{operation, version, case, outcome, expect}`, where `outcome`
is a plugin outcome exactly as a child writes it and `expect` is `{status, class?, retriable, invalid?}`. `status`
is `succeeded`, `failed`, or `result_invalid` — a well-formed outcome whose result does not satisfy the output
schema, or which pins an identifier of a kind the operation never declared.

An `invalid` list names exactly what is wrong with the document, and it is written the same way in both kinds of
fixture: each entry is a JSON pointer, or `{"pointer": "…", "reason": "…"}` where the fixture means one particular
refusal, the reason being one of the codes above. The reported set must equal the stated set, so a document broken
somewhere other than where the fixture says fails instead of passing for a reason nobody wrote down; and where a
fixture states the reason, a constraint that changes kind at that pointer fails too, rather than going on passing
while it no longer demonstrates what the case is named for.

A `failed` fixture is held against the operation's own `failure_codes`: the code its error carries is one the
document declares, under the class the document declares it with. A fixture may not teach a plugin author a code no
operation accepts, nor the same code under a class that would have the runtime repeat what the document calls final.

Retriability in an outcome fixture is stated per the operation's own rule, so that a plugin author reading the
fixture sees what the runtime will do with that answer: `transient` is repeated; `permanent` and `validation` are
final; `ambiguous` is repeated only where the operation says `safe` or `after_recovery_read`; and `result_invalid`
is always final, because a shape error is deterministic and the next attempt would run the same code over the same
answer.

## Where these come from

The first group of fields is copied from a vendor-neutral contract of SDR business operations, published as an open
skill pack in [`reply-team/reply-skills`](https://github.com/reply-team/reply-skills) and versioned independently
of Jason; each document records which version it was copied from under
`l1.contract_version`, together with the family and the operation's name there. Jason implements a small, strict
subset of it, and [`divergence-from-l1.md`](divergence-from-l1.md) records every place the two differ, why, and
whether the difference is something the upstream contract should take back. Feedback travels through that
repository's issues.
