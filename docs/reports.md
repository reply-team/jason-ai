# Reported effects

An effect somebody performed outside Jason, told to Jason afterwards, and kept as the assertion it is.

This document is for whoever operates an installation, and for whoever writes an agent that can act
through tools Jason does not own. How work that Jason itself performs is claimed, run and recorded is
[docs/work-execution.md](work-execution.md); which plugin performs an operation and in which account
is [docs/routing.md](routing.md); what a canonical operation means is
[docs/contracts/](contracts/README.md).

## 1. What a report is

Jason prefers that a campaign's side effects go through it, because that is the only path where an
argument is validated against a contract, a route is resolved, a person's approval is enforced, an
attempt is supervised and the outcome is recorded. It does not pretend it can make that path
exclusive. A user owns their own agents, CLIs and MCP tools, and sometimes an effect is produced with
one of them — deliberately, or because the managed path failed and somebody finished the job by hand.

The alternative to admitting such an effect is not a tidier runtime; it is a runtime that is wrong
about the world and does not know it. So Jason takes the report, and is exact about what taking it
means.

**Admission establishes that a report is well-formed and attributable. That is the whole of it.** A
report says that somebody claims an effect occurred, and says who claims it. Admitting one does
**not** claim that the runtime:

- approved the action beforehand — no approval was requested, shown or answered;
- enforced idempotency — nothing stopped the effect from being produced twice at the provider;
- selected or verified a route — no route was resolved, and a route the reporter names is their
  belief about what they used, not a decision this runtime made;
- supervised an attempt — there is no attempt row, no lease, no timeout and no retry policy behind it;
- guaranteed the result — what the reporter says happened is what they say happened;
- holds complete evidence — a report carries what the reporter had, and usually that is a note.

None of that is a disclaimer bolted onto the answer. Every read of a report carries
`"verified": false` inside its correlation, in this version always, said out loud rather than left to
be assumed — and `--human` prints the same sentence above every rendering:

```text
Reported from outside Jason — the reporter's word, not verified.
```

## 2. The two halves of the row

A report is two things written side by side, and keeping them apart is the point of the design.

**The reporter's assertion** is the submitted body minus the request envelope — `actor`, which is who
is speaking, and `reason`, which is why they called. Everything else is stored exactly as it arrived:
same keys, same values, same order inside a list. It is never normalised, re-ordered or rewritten,
because words that have been tidied up on the way in are no longer the words anybody said. The
`--human` rendering reads its fields back out of that stored document rather than off a column, so
what is printed is what was submitted.

**The runtime's receipt** is the little the runtime knows by itself:

| Field | What it is |
|---|---|
| `id` | The `rpt_…` identifier, assigned here |
| `received_at` | When the runtime admitted it — the one time in the row the runtime can vouch for |
| `assertion_hash` | The canonical `sha256:` of the assertion, which decides a repeat when no key was given |
| `correlation.operation_known` | Whether the operation the reporter named is one this installation publishes |
| `correlation.contact_in_campaign` | Whether the named contact was in the named campaign when the report landed |

**A submission that carries a receipt field is refused by name, not quietly stripped.** The reserved
names are `id`, `received_at`, `reporter`, `dedup`, `assertion`, `assertion_hash`, `operation_known`
and `correlation`, and any of them in a submission is a `field_reserved` refusal. Stripping them
instead would mean storing a document different from the one that was sent — which is no longer the
assertion anybody made — and it would leave a reporter who believes they set the time of receipt
believing it. A name that is neither a field of the assertion nor part of the envelope is refused the
same way, as `field_unknown`: a submission is bound as the document it arrived as, precisely so that
a key nobody understands is an error rather than a silent loss.

The assertion's own vocabulary is fixed: `effect`, `tool`, `summary`, `provider`, `account`,
`occurred_at`, `observed_at`, `campaign_id`, `contact_id`, `work_item_id`, `operation`,
`external_ids`, `evidence`, `unknown_fields`, `uncertainty` and `idempotency_key`. `effect`, `tool`
and `summary` are required; everything else is supplied as available. `effect` is free text in the
reporter's own vocabulary — Jason does not interpret it — and `tool` says what it was done with: a
CLI, an MCP server, a script, a person at a keyboard.

Two of those fields exist so that a reporter can be honest about the edges of what they know.
`unknown_fields` names fields they cannot supply, and each entry must name a field that actually
exists in the vocabulary above — a marker nobody can interpret is worse than no marker, and a typo
would be exactly that. `uncertainty` is prose about what they are unsure of.

## 3. Who may report

The reporter is the request's actor, and it must be named. An anonymous human is the right default
for creating work and the wrong one here: a report with no author is not provenance, it is a rumour.

| Actor type | Who that is | Checked how |
|---|---|---|
| `human` | A person, by whatever id an installation identifies people with | Taken at its word |
| `role` | An AI role acting in a session, under its role id | Taken at its word |
| `attempt` | A running executor, under its `att_…` id | Verified against the attempts table |
| `system` | The runtime itself | **Refused** — `reporter_reserved` |

An attempt is the one claim the runtime can check, because an attempt is a row in this database: a
claim to be one that names no attempt this runtime knows is refused as a mistake worth telling the
caller about. A human and a role are recorded as claimed, because the runtime holds one capability
token and no caller identity, and writing down what the caller said it was is the honest thing to do
with a claim it cannot test.

`system` is refused with a code of its own rather than the generic one every verb gives, because the
reason is specific: the runtime performs effects; it does not report them. An effect the runtime
produced has an attempt behind it, and that is a different kind of record entirely.

## 4. Submitting one

```sh
jason report submit --actor human:ada \
  --effect email_sent --tool reply-cli \
  --summary "Sent the intro by hand after the enrolment failed." \
  --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD \
  --operation campaign.enroll --provider reply --account team@example.com \
  --occurred-at 2026-09-17T11:04:00Z --unknown observed_at \
  --idempotency-key intro-marta-1
```

`--external-id kind=value` repeats for each identifier the other tool gave it, `--evidence` takes a
JSON object, and `--unknown` repeats for each field the reporter cannot supply. The API operation
underneath is `POST /v1/report.submit`, and the answer is the admitted report:

```json
{
  "id": "rpt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
  "received_at": "2026-09-17T11:06:41Z",
  "reporter": { "type": "human", "id": "ada" },
  "reason": null,
  "dedup": { "outcome": "admitted", "matched": null },
  "assertion": {
    "effect": "email_sent",
    "tool": "reply-cli",
    "summary": "Sent the intro by hand after the enrolment failed.",
    "campaign_id": "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
    "contact_id": "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
    "operation": "campaign.enroll",
    "provider": "reply",
    "account": "team@example.com",
    "occurred_at": "2026-09-17T11:04:00Z",
    "unknown_fields": ["observed_at"],
    "idempotency_key": "intro-marta-1"
  },
  "assertion_hash": "sha256:…",
  "correlation": {
    "campaign_id": "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
    "contact_id": "cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
    "work_item_id": null,
    "operation": "campaign.enroll",
    "operation_known": true,
    "contact_in_campaign": true,
    "verified": false
  }
}
```

A refusal names everything wrong with the request at once, rather than making the caller find the
next problem by fixing this one. The order the checks run in is: names the API does not know, then
the reporter, then what is missing, the wrong shape, or too large — that last group accumulating so
that one answer carries all of it.

## 5. The same effect told twice

A reporter retrying after a dropped connection must not have to tell an error from an echo, so a
repeat is a success: **200, the same report, and a `dedup` block saying which rule matched.**

The published order is short:

1. **The reporter's own key decides when they gave one.** With `idempotency_key`, a second submission
   from the same reporter carrying the same key answers with the first report and `"matched": "key"`.
2. **What they said decides when they did not.** With no key, the canonical hash of the assertion
   decides: the same words from the same reporter answer with the first report and
   `"matched": "content"`.

Both rules are scoped to the reporter, and that scoping is an honest trade-off rather than an
oversight. **Two reporters describing one effect are kept as two rows.** They may well be describing
the same email; collapsing them would mean picking one account of it and throwing the other
reporter's word away, and a report is exactly the kind of record where whose word it is matters. Two
assertions that agree are cheap to read; one assertion that silently absorbed another cannot be
recovered.

The escape hatch is what makes matching on content safe as a default. **A reporter who really did
produce the same effect twice distinguishes the two by giving each its own key.** Without that, an
honest second send would be swallowed as a repeat of the first; with it, the reporter says "this is a
different occurrence" in the one place where only they can know. Content matching is therefore the
behaviour for a reporter who has not thought about it — where a repeat is far more likely to be a
retry than a genuine second effect — and the key is there for the reporter who has.

Two subtleties are worth knowing. The key is part of the document that is hashed, so a submission
with a key and one without are never the same words. And both rules are enforced by unique indexes
rather than by a read before the write, so two callers racing with the same report end with one row
and one id: whoever loses the race re-reads and answers with the winner's report. A repeat writes no
second journal line either.

## 6. Correlation: what the report says it was about

Correlation is optional, supplied as available, and checked only for existence. A report that names
nothing local is still a report — refusing one would push a reporter to invent a correlation rather
than admit they have none.

**An id that names nothing is refused, because it is a typo.** `campaign_id`, `contact_id` and
`work_item_id` are loaded, and an id with no row behind it answers `campaign_not_found`,
`contact_not_found` or `work_item_not_found`. Admitting it would store a dangling reference nobody
could ever interpret; refusing it tells the caller the one thing they can act on.

**Ids that disagree with each other are refused too**, as `correlation_inconsistent`: a work item
that belongs to a different campaign than the one named, or that is about a different person than the
contact named. That is not a fact about the world worth keeping — it is a mistake the caller can see
at a glance once it is named.

**A contact who is not a member of the named campaign is admitted**, with
`"contact_in_campaign": false`. This is the one place where a mismatch is not an error, and the
reason is that it is exactly the kind of fact a report exists to carry: an effect that reached
somebody the local campaign has never heard of is information, and refusing it would force the
reporter to drop either the campaign or the person from their account of what happened. The field is
`null` when the report names only one of the two, or neither.

**An operation this installation does not publish is admitted as the reporter's word**, with
`"operation_known": false`. The catalog decides a boolean here and never the admission: an effect
nobody modelled is still an effect that reached somebody, and a runtime that refused to hear about it
would be choosing not to know. `--human` prints such an operation with `(unknown here)` beside it
rather than leaving it to be discovered.

And the whole correlation carries **`"verified": false`, always, in this version.** What the runtime
checked is that the ids it was given exist. It did not check that the effect happened, that it
happened to that person, that it went through that account, or that it corresponds to that operation.
Saying so in every answer is cheaper than letting a later reader assume more.

## 7. Nothing rewrites an admitted report

There is no verb that changes or removes one — not an update, not a correction, not a withdrawal. A
report is somebody's word at a moment, and a word that can be edited afterwards is not evidence of
anything. A reporter whose account has changed submits a new report; both stand, and a reader can see
that they do.

Three layers enforce it, and each would be enough on its own:

1. **No API path writes one.** `report.submit`, `report.get` and `report.list` are the whole surface,
   and nothing inside the runtime calls the first.
2. **An EF interceptor refuses a modified or deleted row before it reaches the database.** Any save
   whose change tracker holds a report in the modified or deleted state throws.
3. **Two database triggers refuse it at the database.** `reports_no_update` and `reports_no_delete`
   abort with `an admitted report is immutable`, so even a direct `UPDATE` against the file fails.

This is the same discipline the `journal` table already follows, for the same reason: the two tables
whose whole value is that nobody could have changed them are the two tables nobody can change.

## 8. What admission does not do

Admitting a report is inert, deliberately and provably. It writes exactly two things: the report, and
one `external_effect_reported` line in the journal naming it — and that line carries identifiers
only, so what was said stays in the report, where it belongs. Everything else in the database is left
exactly as it was:

- **No attempt is written.** An attempt is something this runtime claimed, ran and stands behind.
- **No work item moves.** A report naming a work item does not complete it, fail it, or spend one of
  its attempts. The item's status stays the dispatcher's business.
- **No provenance is touched.** Provenance records what ran and under which route; a report describes
  something that never went through a route at all.
- **No external identifier is pinned.** An identifier a reporter mentions is somebody else's word
  about a provider this runtime never talked to, and promoting it to a pin would put a claim the
  runtime cannot stand behind where its own findings live. It stays inside the assertion, as part of
  what was said.
- **Nothing is held, cancelled or reconsidered.** No future work is blocked, no approval is reopened,
  no plan changes.

**That last absence is the deliberate one.** It is tempting to have a report about an email hold the
work item that would send the same email again, and the architecture does anticipate that an accepted
report should eventually update a provenance-qualified view and cause dependent work to be
reconsidered. Doing it now would mean deciding, automatically, that this reported effect is the same
effect as that queued one — and the runtime cannot verify that. What it has is an unverified claim
mentioning a contact and an operation, from a reporter it mostly cannot check, about a provider it
never spoke to. Acting on it would be treating `verified: false` as if it said something else.

Matching a reported effect against what the runtime believes, and against what a provider would say
if it were asked, is **reconciliation**. It is a real piece of work with a design of its own, and
this version does not have it (DEF-RECON-001, and the report model's own open questions in
DEF-EXT-001). Until it exists, a person reads the reports and decides — a smaller promise than
automatic reconsideration, and one the runtime can actually keep.

## 9. Reading them back

```sh
jason report get rpt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
jason report list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --human
jason report list --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll
jason report list --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --since 2026-09-17T00:00:00Z
```

`report.list` narrows by what a report was correlated to — `campaign_id`, `contact_id`,
`work_item_id` — by the operation the reporter named, and by `since`. It pages with `limit` and
`cursor` like every other listing, oldest first, and a listing stays small: no assertion, no
evidence, no hash. Who reported what, with which tool, about what.

Two details of the filters are worth saying. The operation filter matches the reporter's text as they
wrote it, including an operation this installation has never published — narrowing to it is a fair
question about what people say they have been doing. And `since` is inclusive, so a walk of
successive windows never loses what landed exactly on a boundary. **A filter naming something that
does not exist is the loader's own refusal**, not an empty page: a caller who mistyped a campaign is
told so, rather than shown nothing and left to read it as a fact about the world.

**A work item's own view shows what was reported about it, apart from its attempts.** `workitem.get`
carries an `external_reports` list beside `attempts`, newest first, and the separation is the whole
point: an attempt is something this runtime claimed, ran and stands behind; a report is somebody's
word about something it never touched. One list holding both would invite exactly the confusion this
design exists to prevent. The list is sent when one item is fetched — not on a listing, where twenty
assertions per row would be paid for by every caller — and it is capped; past the cap,
`report.list --work-item` has the rest.

## 10. The caps

Constants, not settings. Nothing here is an installation's choice, and a limit an operator can raise
is a limit somebody will raise.

| What | Cap |
|---|---|
| `effect`, `tool`, `provider`, `account`, `operation`, `idempotency_key`, the three correlation ids, and each external id's `kind` and `value` | 200 characters |
| `summary`, `uncertainty`, `reason` | 2000 characters |
| `evidence`, as canonical JSON | 65536 bytes (64 KiB) |
| `external_ids` entries | 20 |
| `unknown_fields` entries | 20 |
| The reporter's actor id | 100 characters |
| Reports on a work item's own view | 20, newest first |
| A `report.list` page | 100 by default, 1000 at most |

Evidence is a note about what happened, not the thing itself. The 64 KiB is the journal's own limit,
which is the right size for a note and refuses a payload; anything larger belongs where the tool that
produced it keeps it, named from the evidence rather than copied into it.

## 11. Known limitations of this version

- **No reconciliation.** A report never updates Jason's view of the world, holds dependent work or
  reopens an approval. A person reads and decides (DEF-RECON-001).
- **Nothing is verified.** `correlation.verified` is `false` in every answer, and there is no path
  that could make it true — no provider is read back, and no evidence is checked against anything
  (DEF-EXT-001).
- **No confidence, and no conflict handling.** A reporter says what they are unsure of in prose; the
  runtime does not score it, and two reports that contradict each other simply both stand
  (DEF-EXT-001).
- **No correction and no withdrawal**, by design. A mistaken report is answered by a further report,
  never by editing the first.
- **Content matching is per reporter, and cannot be turned off.** Two reporters describing one effect
  are two rows; one reporter who genuinely produced the same effect twice must say so with a key.
- **`effect` is uninterpreted free text.** There is no vocabulary of effects, so `email_sent` and
  `sent_email` are two different things to every filter and every reader.
- **Only three published operations to correlate to.** `campaign.get`, `list_membership.add` and
  `campaign.enroll` are the whole catalog, so `operation_known` is false for most of what anybody
  will actually report (DEF-OPS-001).

The design these follow from — managed execution preferred but not exclusive, post-factum reporting
expected rather than pretended away, and admission that never becomes a retroactive managed attempt —
is in [docs/architecture.md](architecture.md), section 11.
