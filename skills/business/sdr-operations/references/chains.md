# End-to-end chains

Eight jobs, written as the sequence of operations they actually decompose into, with the decision and
the check at each step, and every approval gate marked.

## How to use these

They are **worked examples, not a playbook**. The contract says what an operation is — what it
changes, whether it reaches a real person, whether it can be undone, what to read before running it
again. A chain shows one real composition of those operations, in an order that survives contact with
the job. Neither answers the question a practitioner actually has: whether this outreach is worth
running, who should receive it and what it should say. That judgement is not in this file and is not
in the contract. It is in the playbook skills, and in the person who owns the number.

Read the chain that matches the job in front of you. Do not read all eight, and do not treat a chain
as a checklist to be satisfied — a chain followed for a job it does not describe is worse than no
chain, because it produces confident motion in the wrong direction.

What is normative here is the **ordering** and the **gates**.

- **The ordering.** Where one step must precede another, the reason is stated, and it is nearly
  always that the later step is unanswerable or unsafe without the earlier one. Chain 4 is the
  clearest case: each step can invalidate the question the next one would answer, so running them out
  of order produces a confident wrong answer rather than an error.
- **The gates.** Every approval is marked in its own column, using the contract's own vocabulary. A
  chain never lowers a gate and never invents one. Where an operation's approval is conditional, the
  chain says which call resolves the condition — `operation.preview`, always, never an assumption.
- **The failures.** Where a step exists only to prevent a specific failure, the failure is named in
  the check, after **Prevents:**. A step whose failure cannot be named is a step someone added
  because it felt careful.

Everything else is illustration. The worked figures — 212 rows, 41% accepts-all, 88 live enrolments
— are realistic and are **not contract values**. No threshold, count or rate in this file is a
promise, and none of them should be copied into a policy. Substituting your own numbers is expected.
Substituting your own order is a change to the contract.

Codes in parentheses — `A9`, `E2`, `M4` — are invariants. Each is stated once, with the operations
that enforce it, in `references/invariants.md`. A chain cites them; it does not restate them.

**This is where the neutral ordering lives, and it lives only here.** A provider adapter says how an
operation is performed against one product, which of its errors mean what, and where that product
cannot perform an operation at all. It does not restate the order. An order written in two places
drifts, and the copy that drifts is always the one further from the contract.

## The gate column

| Value | What it means for an agent |
|---|---|
| `auto` | Proceed. Nothing to ask. |
| `auto` **protective** | Proceed **immediately**. The protective floor: the harm here is non-performance, so waiting for permission is the destructive choice. Never queue one of these for approval. |
| `confirm_once` | One human decision covers the whole call, including a call carrying a collection. |
| `confirm_once` **+ preview** | The same, but the approval is carried by an artefact: `operation.preview` runs first and its output is what the human approves. The preview **names** the population; it does not merely count it. |
| `confirm_each` | One human decision per item. Used where each item is its own act against a named person. |
| **human** | Not a call. A person decides or does something, and the chain waits. |
| — | Nothing is invoked at this step. A decision to be made before the next call, or a result to read. |

Two gate rules that hold across every chain: **a passed check is not a permission** — gates are
re-evaluated immediately before the act, and a batch prepared against an earlier check is re-screened
at dispatch (A7) — and **an approval that times out in an unattended run resolves as refuse-and-halt**
(L2). Neither has an exception anywhere in this file.

---

## 1. Launching outreach to two hundred people from a raw file

**The situation.** A spreadsheet of 212 rows arrives from a colleague who bought it. The ask is
"launch a campaign to these two hundred people this week."

This is the longest chain in the contract, and its length is the point: **five separate decisions with
no safe default sit between a file and a send** — the duplicate policy, the blank-value rule, the
contactability policy, the collision scope, and the collision policy at enrolment. None of them can be
inferred from the data.

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 1 | `capability.list`, `autonomy.get`, `vocabulary.list` | What may this credential do, what runs unattended, what values are legal here | `auto` | `unknown` on a needed capability is not permission (A6). Read at session start; authority resolves at plan time (N5) |
| 2 | `source.inspect(source_ref)` | Accept, edit or reject the proposed mapping | `auto` | Every detected column is either mapped or **individually named as discarded** (B10). Read the structural findings — encoding, date formats, unit ambiguity on revenue and headcount, header rows sitting inside the data. It proposes; it never writes |
| 3 | *(decide, do not call)* | The six mandatory inputs to the write: duplicate policy · blank-value rule · owner · account binding · provenance · idempotency key | — | None has a safe default (B3, B6, B13). Owner must be an active person or the write refuses. Provenance here is a purchased third-party file, which is what makes the privacy notice owed at step 20 (A18) |
| 4 | `operation.preview(import.apply, …)` | Is the projected outcome what we intended | `auto` | Projected create / update / skip / quarantine counts, duplicate clusters, missing-required-field counts, metered projection against its ceiling. The preview never acts and never spends (N4) |
| 5 | `import.apply(source_ref, mapping, policy, blank_rule, owner, provenance, key)` | Go | `confirm_once` | **Always**, including create-only: the size is unbounded and the blast radius is the whole database. It never buys and never verifies (C4). It matches exclusion state before it writes, **including minimised entries left by an erasure** (A9). **Prevents:** an unbounded spend hiding inside the most-called write in the contract, and a re-contact of somebody who asked to be erased |
| 6 | `job.get` / `import.get` | What actually landed | `auto` | Per-row outcome in `contact.create`'s shape, each carrying its source row identifier. *Worked example:* 212 rows → 178 created, 14 updated, 6 skipped, 8 **quarantined** (identity resolved `ambiguous`, B1), 6 refused on an exclusion match — 2 of those from a past erasure, which is A9 doing its job |
| 7 | Resolve the 8 quarantined rows | Which person is this, really | **human** | `ambiguous` is a terminal answer that routes to a human and is **never coerced** into a match (B1). **Prevents:** a merge that silently overwrites an owned record with a stranger's data |
| 8 | `contactability_policy.get`, or `contactability_policy.define` first | What are we willing to send to | `auto` / `confirm_once` on define | Must answer for accepts-all domains, unknown verdicts, role accounts, free mailboxes, disposable addresses, and the re-verification staleness window. **Nothing here has a default** (D5) |
| 9 | `email_address.verify(collection, key)` | Spend the verification allowance | `confirm_once` | Metered, per address. Verdict and policy advice are returned separately; `accepts_all` is terminal (B8). *Worked example:* 78 deliverable, 41 accepts-all, 12 undeliverable, 61 unknown |
| 10 | `audience.assess(scope=import batch)` | Is this fit to work | `auto` | Completeness per required field, verification distribution and staleness, duplicate clusters, field age, formatting defects, exclusion hits, contactability hits, jurisdiction mix, contacts per account — every figure a count against a **stated threshold**, with an as-of. It reads only and never spends (D6). This is the artefact a human signs |
| 11 | `list.create` + `list_membership.add`, or `segment.define` + `segment.materialize` | Freeze the population | `confirm_once` on the write | A segment is criteria; a list is a frozen fact with a date on it. **Prevents:** "who did we contact in June" becoming unanswerable, which is what happens when outreach runs against live criteria (D1) |
| 12 | `audience.screen(list)` — **first run** | Take out what we must not contact | `auto` **protective** | Removed members are reported by name so the list can be rebuilt (D3) |
| 13 | `collision.check(accounts, collision_scope)` | Who else is touching these companies | `auto` | `collision_scope` is mandatory with no default — legal entity, domain, or ultimate parent — because whether a subsidiary counts as the same company is a commercial decision, not a data fact. Returns **who and why, never true/false** |
| 14 | `campaign.create` → `step.add` ×N → `template.create` / `template.publish` → `schedule.set` → `pacing.set` → `sender_binding.set` → `reply_policy.set` | Author the programme | `confirm_once` per write | A campaign is never live on creation (E1). Each step declares channel × execution mode × kind × position × delay with **unit and basis** (E7, E9, E10). An automatic step on a channel that does not sanction automation is refused **here**, not at send time (E15) |
| 15 | `message_element.set(sender)` | The mandatory elements every message carries | `confirm_once` | Legal identification block, postal address where required, and **each unsubscribe artefact separately** — header, machine-actionable endpoint, visible link (F7). **Prevents:** a passing header check being read as a working unsubscribe |
| 16 | `sender.health`, `domain_compliance.check`, `capacity.estimate` | Can we physically carry 200 over this horizon | `auto` | Capacity is nested budgets with scope, window type and unit; `remaining: unknown` is a first-class answer and is returned as one, never as a number (F3, A6). If authentication fails, `authentication_requirement.list` names what must be published — **publishing it is not ours to do** |
| 17 | `message.draft(step 1, a named real contact)` → `content_policy.check` → `message_compliance.check` → `disclosure.check` | Is the artefact that will actually go acceptable | `auto` on the checks | All three bind the **rendered** message, not the template (E14). `message_compliance.check` also checks the reputation of every domain the message links to — tracking and shortened links are reputation-bearing separately from the sending domain |
| 18 | `campaign.test` | Receive step 1 ourselves and click the unsubscribe link | `confirm_once` | Only to addresses the workspace can demonstrate it owns; excluded from every denominator; **a real send consuming real capacity** (E5). Open question 12 asks whether this is a hard precondition of validation or a strong recommendation |
| 19 | `campaign.validate` | Is it safe to turn on | `auto` | Missing or unpublished content, steps no sender can serve, a schedule that never opens, **no terminal condition** (E4), pre-enrolled count, projected first-24-hour volume, projected metered cost, whether the test passed. Validation is a report; **the gate is a human reading it** |
| 20 | `outreach.precheck(the 200, channel, message_class=unsolicited)` | Who may be contacted, and what would fix the rest | `auto` | One full gate result per person, **every failed gate returned** (A8). *Worked example:* 154 allow · 9 deny(suppressed) · 21 `deny_with_remedy(notice_owed)` · 11 defer(timing) · 5 defer(frequency). The 21 are satisfied by rendering the notice into the message; reading gate 4 as "send 21 standalone notices" chooses the expensive route, not the required one (A18) |
| 21 | `audience.screen(list)` — **second run** | Re-screen immediately before enrolment | `auto` **protective** | D4. **Prevents:** contacting somebody who opted out between step 12 and now. On a week-long build, somebody always did |
| 22 | `operation.preview(campaign.enroll)` | Resolve the conditional reach | `auto` | Bookkeeping while the campaign is not live, `act` the moment it is (G2). Never assumed |
| 23 | `campaign.enroll(list, collision_policy, start_position, first_touch_timing, key)` | Enrol | `confirm_once` **+ preview** | All three of collision policy, start position and first-touch timing are explicit; none may be defaulted (G7). Per-item outcome: created / reactivated / skipped, each with its reason |
| 24 | `stoprule.set` ×N | What stops this by itself | `confirm_once` | Legal subjects include bounce rate, complaint rate, negative-reply rate, opt-out rate, error rate, spend velocity, **sender health, domain reputation and remaining budget** (L8). **Prevents:** a restriction being discovered from a failed send rather than before the batch |
| 25 | `operation.preview(campaign.activate)` → `campaign.activate` | Turn it on | `confirm_once` **+ preview** | **An act of magnitude 200** (E2). The preview is mandatory: recipient count, first-touch time, projected first-24-hour volume, metered cost. *Worked example:* pacing admits 50 people a day, so first-24-hour volume is 50, not 200 |
| 26 | `engagement.summarize` next morning | Did it do what the preview said | `auto` | Read the **did-not-happen** counts first (M5): deferred, cap-skipped, blackout-skipped, refused by the gate, throttled by capacity |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| Enrol into a draft, then activate — it looks more careful than enrolling into a live campaign | **E2.** Activation's reach is `act` with magnitude equal to the enrolled population. `operation.preview(campaign.activate)` returns the recipient count and first-touch time before the confirmation, so the second gate is the real one either way |
| Let the import verify or enrich "while it is in there" — one pass over the data feels efficient | **C4.** Creating and importing never buy and never verify. Keeping them apart is what stops an unbounded spend hiding inside the most-called write in the contract |
| Screen once, at step 12 — the list was screened, and screening again looks redundant | **D4.** Exclusion state moves between materialisation and enrolment. The second screen is where the weekend's opt-outs are caught |
| Treat the step-20 pass as permission to send at step 25 — the check said allow | **A7.** A pass reserves nothing and does not survive. The gates are re-evaluated immediately before the act and the batch is re-screened at dispatch |
| Retry `import.apply` after a timeout without the key — retrying a failed call is normal engineering | The operation's own *before repeating* clause: **a re-run without the key is a second import, not a retry.** `invocation.get(key)` distinguishes `never_seen` from `seen_and_failed` (N1) |
| Pick a duplicate policy because one is obviously sensible — every other system has a default | **B3.** There is no safe default and the contract refuses to invent one. The wrong one silently overwrites owned records |
| Send 21 standalone privacy notices, because gate 4 said the notice is owed | **A18.** The gate is satisfied by the outreach message carrying the notice. Standalone delivery is the fallback, not the requirement |

---

## 2. Working a morning inbox

**The situation.** 09:00. Overnight: 34 replies, 3 inbound arrivals, 61 due tasks, 9 items in the
approval queue.

**The day is experienced as a queue, not as campaigns** — which is why `task.list` and
`conversation.list` are core operations and `campaign.get` is not read at all in this chain. The
ordering below is not preference: the inbound queue carries a clock and the reply queue does not, so
three arrivals outrank thirty-four replies however the list lengths look.

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 1 | `capability.list`, `autonomy.get`, `vocabulary.list` | What may I do today, unattended or not | `auto` | Re-read at the start of every session. Authority changes when the credential changes |
| 2 | `inbound_lead.list` | **First**, before anything else | `auto` | The arrived-and-unworked queue with the clock on each row, oldest first, and how long is left against the declared target. The clock runs from the source's own event time (K1). *Worked example:* one arrival is 11 minutes old against a 15-minute target — it outranks all 34 replies |
| 3 | `conversation.list` | What came back, most overdue first | `auto` | **The queue arrives ordered** (H10). Do not re-sort by created date |
| 4 | `approval.list` | What is waiting for a human, and how long it has waited | `auto` | Read every session under any oversight model. An item pending since 02:00 in an unattended run means the run halted (L2) |
| 5 | `task.list` | What is owed today, in the order to do it | `auto` | Includes work a signal put there, not only work the cadence calendar produced (J4) |
| 6 | `conversation.get(thread)` | Read before answering | `auto` | Every message in order, who is on it, what we have decided it means, what it is attached to |
| 7 | `conversation.classify(thread)` | What does this reply mean | `auto` | One of nine base values, always accepted, with the account's vocabulary mapped on top and **both** returned (H2). The judge's identity and confidence are recorded, and a machine may not overwrite a human's decision (H3). **Classifying changes no participation state** (H1) |
| 8 | *branch on the meaning* | See the branch table below | varies | The classification alone changes nothing. If no operation follows it, nothing happened |
| 9 | `task.complete` / `task.skip` / `task.reschedule` | Close out the queue honestly | `auto` | Completing sends nothing (H6); **skipped is not completed** (H7); a day that overran is rescheduled, not silently abandoned. Open question 21 notes that cross-class ranking inside this queue has no declared policy |
| 10 | `conversation.close` | Done with the thread | `auto` | Worked, not ignored. It carries **no** business outcome and **is not a way to stop outreach** (H9) |
| 11 | `disposition.set` | What came of the participation | `auto` | From the account's declared vocabulary, distinct from the reply's meaning and from the execution state (G3) |

**The branch at step 8 — what each meaning actually produces.**

| Classification | Next operations | Gate | The rule that binds it |
|---|---|---|---|
| A request to stop | **Not a classification at all.** `optout.record` → `suppression.add(legal_obligation)` → `outreach.hold` → `scheduled_message.cancel` → `enrollment.stop(opted_out)`. **Never reply** | `auto` for the protective half — `optout.record`, `outreach.hold`, `scheduled_message.cancel`, and `suppression.add` on the person's own identifier. `confirm_once` for `enrollment.stop`, and for `suppression.add` over a domain or a pattern. Do the `auto` half first; never hold it for the other | A2, H4, L6, G8 |
| `not_interested` | `enrollment.stop(reason=not_interested)`, optionally `cooldown.set` | `confirm_once` | G6. A targeting outcome, not an opt-out. **Prevents:** conflating the two, which makes the person permanently unmarketable |
| `interested_later` | `cooldown.set(person, date, reason)` **and** `enrollment.stop` or `enrollment.pause` | `auto` / `confirm_once` on stop | The classification must produce a cooldown or it changes nothing; `cooldown.set` is what gate 12 reads. Open question 14 asks whether this should land in a cooldown or a nurture programme |
| `out_of_office` / `auto_reply` | Nothing outbound. Optionally `message.schedule` for their stated return date | `confirm_once` on the schedule | H4: nothing automatic answers something automatic. A dated message for someone's return is a **new outbound message, scheduled** — not a reply. These two carry *certainty*, not confidence |
| `information_request` | `message.draft` → `outreach.precheck(message_class=answer_to_request)` → `approval.request` or `message.send` | `confirm_each` on the send | A22: answering a request they made is a different message class. A legal exclusion still refuses; a marketing preference does not turn their own question into marketing |
| `objection_engaged{price\|budget\|no_bandwidth\|…}` | `message.draft` with the sub-code, then the same gate and approval path | `confirm_each` on the send | The sub-codes exist because "what does it cost" and "we have no budget this year" lead to different next operations |
| `interested_now{meeting_ready}` | `meeting.propose` or `meeting.book` | `confirm_each` | Both put specific times in front of a named person |
| `wrong_person{referral}` | See chain 3 | — | A16 |
| `unclassified` | `escalation.raise` or `conversation.assign` | `auto` | L4: escalation grants nothing; assign moves an inbox owner and promises nothing |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| Label "take me off your list" as `not_interested` — it is the closest-looking value and keeps the reporting axis tidy | **A2.** A request to stop is an obligation, not a meaning, and `conversation.classify` refuses it as a value. Were it a label, a relabel would lift a legal duty |
| Reply to an out-of-office — it looks like a reply and the thread is open | **H4.** Nothing automatic answers something automatic. The correct response is a scheduled new message for their return date |
| Close the thread to stop the cadence — closing feels like finishing | **H9.** A conversation carries no business outcome and closing stops no outreach. The cadence keeps sending |
| `task.complete` on a "send this" task and move on — the task said send, and it is now done | **H6.** Completing a queued task sends nothing. The send is its own act under its own approval, and there is no flag on `task.complete` that makes it send — such a flag would inherit this operation's approval class |
| Answer from whichever sender has capacity — load-balancing is normally correct | **H5.** A reply is sent from the sending identity that owns the thread. Anything else breaks threading and reads as a different company |
| Work the 34 replies first, because volume feels like the bigger job | **K8 / H10.** The inbound queue arrives ordered against a declared target. A reply that has waited nine hours has no clock; an arrival at eleven minutes does |
| Let the machine re-label a thread the human already judged — the model is more confident this morning | **H3.** A person's decision beats a machine's, and both are kept |

---

## 3. A reply that says "talk to my colleague"

**The situation.** The prospect writes: *"I'm not the right person for this — you want Steve in
finance."* No surname, no address, no title.

This chain is short, and it is the single most dangerous short chain in the contract: **it produces a
new person we will contact on the strength of a stranger's sentence.**

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 1 | `conversation.get` | Read the whole thread | `auto` | Who is on it, what it is attached to |
| 2 | `conversation.classify` | `wrong_person{referral}` | `auto` | A base value, always accepted. The sub-code distinguishes it from `wrong_person{no_referral}`, which produces a completely different next step |
| 3 | `referral.record(conversation, named_person, relationship, key)` | Record who they named, and where the name came from | `confirm_once` — **raised** | Raised because it creates a person we will contact on a stranger's sentence. It returns **`unresolved` with a task reference** when a first name and a department cannot be turned into a contactable person, **and it never guesses an address.** "Steve in finance" is the common case, not the exception |
| 4 | `task.get` (the returned task) → `contact.search` | Is Steve somebody we already hold | `auto` | Free, never reaches outside, never costs credits. **Prevents:** buying an identity we already own |
| 5 | *if not held:* `candidate.search` → `candidate.promote`, or `contact.enrich` | Buy the identity | `confirm_once` | Metered and separately approved. `withheld_by_policy` is not `not_found` (B7) |
| 6 | `contact.create` / `candidate.promote` with **`provenance = third_party_referral`** | Create the person | `confirm_once` | **A16 and A17 are the whole point of this step.** A referral sets provenance, never a consent basis, and provenance values and consent bases are two different enumerations |
| 7 | `account_membership.create` | Attach Steve to the account deliberately | `auto` | **Prevents:** a second company appearing for every spelling of a name |
| 8 | `collision.check(account)` | Who else is touching this company | `auto` | The referred person is treated as a **successor touch inheriting the referrer's collision slot**, so a referral chain cannot quietly become four simultaneous touches at one company. This is exactly the part open question 6 contests |
| 9 | `outreach.precheck(Steve, channel, unsolicited)` | May we contact him | `auto` | Evaluated under **exactly the same lawful basis as any cold contact**. Gate 3 (basis) and gate 4 (notice owed) both apply — our data did not come from Steve, it came from a colleague, so the notice is owed |
| 10 | `campaign.enroll(Steve, collision_policy, start_position, first_touch_timing, key)` | Put him in | `confirm_once` **+ preview** | `message.draft` resolves the durable **referred-by reference**, so the copy can honestly say who sent us without anybody hand-typing it |
| 11 | `enrollment.stop(original person, reason=wrong_person, cause=Steve)` | End the referrer's participation | `confirm_once` | **G6:** the reason is mandatory and **may name a different person as the cause**. **Prevents:** a second and third touch to somebody who has already answered |
| 12 | `conversation.close` + `disposition.set` | Close out | `auto` | H9: the close carries no outcome; the disposition does |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| Write "warm introduction" into Steve's consent field — it genuinely is warmer than a cold contact, and "referral" sounds like permission | **A16.** A referral establishes provenance, never a basis, and no evidence bundle may cite a stranger's message. Without this rule the gate passes a cold contact and `consent.prove` produces a bundle whose evidence is somebody else's email |
| Guess `steve.smith@company.example` from the domain pattern — the pattern is obvious and usually right | `referral.record` **never guesses an address** and returns `unresolved` with a task instead. A guessed address that is wrong is a message to a stranger; one that is right is still a person whose data we invented |
| Skip the privacy notice because a colleague introduced us | **A18.** The data did not come from **Steve**. Gate 4 is a deny-with-remedy, and the remedy is the notice, normally rendered into the message |
| Leave the referrer enrolled — nothing failed and the cadence is still valid | **G6.** They told us they are the wrong person |
| Count "wrong person" as interest, because it arrived as a helpful reply | The classification is `wrong_person`, not `interested_*`. A referral means *this* person is not the buyer, and folding it into interest overstates the funnel at its widest point |

---

## 4. Diagnosing a drop in reply rate

**The situation.** "Reply rate is down. Fix the copy." Reported figure: 3.5% last month, 2.4% this
week.

The order below is not preference. It is arithmetic: **each step can invalidate the question the next
one would answer**, so running them out of order produces a confident wrong answer. The copy is
checked last, and in practice it is the least common cause — which is precisely why it is the first
thing blamed, because it is the only axis most people can imagine acting on.

Steps 1–7 are the envelope, 8–13 the deliverability axis, 14–16 the targeting axis, 17–19 the copy
axis. Only step 20 changes anything.

| # | Operation | The question | Gate | What would end the investigation here |
|---|---|---|---|---|
| 1 | `metric.describe(reply_rate)` | What does this measure mean here, what was it mapped from, what can it be grouped by, how much can it be trusted | `auto` | The two figures turn out to be different measures. "Reply rate" is one of the two most-managed numbers in the industry and has no shared definition |
| 2 | `engagement.summarize(scope, window, group_by=time)` | The envelope | `auto` | A different as-of, window, basis, denominator or **detection method** between the two readings. A change of detection method inside the window is a mandatory caveat, not a footnote (M1) |
| 3 | **Read the did-not-happen counts** | *Did fewer people reply, or did we send fewer?* | — | *Worked example:* sends fell from 1,000/week to 620/week, with `throttled_by_capacity = 310` and `skipped_by_blackout = 70`. The reply **rate** may be unchanged; the reply **count** fell because sending fell. M5 exists so this is answerable at all |
| 4 | Check the basis: cohort or window | Are we comparing like with like | — | **M3.** A window figure for a cohort still mid-flight compares an immature population with a mature one. A cohort three days into a fourteen-day cadence has had two of seven touches |
| 5 | Check the denominator count and the `insufficient_volume` flag | Is this a number or a direction | — | **M4.** At an observed 3.0% reply rate, n=200 gives a 95% interval of roughly 1.4%–6.4%. A 3.5%→2.4% move on 200 sends is inside the noise. **This step ends more investigations than any other** |
| 6 | `stoprule_firing.list` | Did something stop by itself | `auto` | The read that tells the next session why sending is off: the trigger value, the operations halted, and every item left unprocessed (L8) |
| 7 | `schedule.resolve`, `campaign.get`, `blackout.list` | Did the calendar or the pacing change | `auto` | A holiday calendar, a changed pacing figure or a freeze explains a volume drop completely and is invisible in a rate |
| 8 | `sender.health` | Can this capability send at all | `auto` | Restrictions with cause-or-`undisclosed`, warm-up position and whether it was invalidated, capacity budgets with nullable remaining, automation sanction. `remaining: unknown` is a real answer (F3, A6) |
| 9 | `sender.summarize` | Failures by class, complaints, deferrals, throttles | `auto` | Every rate carries its publisher, denominator basis, window and as-of (F6) |
| 10 | `domain_compliance.check` | Per publisher, per requirement: pass, fail or unknown — **naming the failing requirement** | `auto` | Authentication failure is binary, checkable, and the one cause here that is both common and completely fixable |
| 11 | `domain_reputation.get` | The named signals, each with publisher, scale, window and denominator, including blocklist listings and the dataset each came from | `auto` | **No composite score is ever manufactured** (F6). There is no shared reputation scale across publishers, so a composite would be an invented quantity with nobody behind it |
| 12 | `message_compliance.check(rendered message)` | Required elements, a working unsubscribe checked properly, **and the reputation of every domain the message links to** | `auto` | Tracking domains and shortened links are reputation-bearing separately from the sending domain, and are the most-missed cause in this family |
| 13 | `placement_test.get` | Where did a test land, per mailbox provider | `auto` | Typed as **simulated placement**, never as our inbox rate (M2). Whether a real message landed in the inbox or the spam folder is not measurable |
| 14 | `engagement.summarize(group_by=provenance)` | Where did these people come from | `auto` | **M6.** *Worked example:* replies from the June import 3.4%, from Monday's purchased file 0.9%. That is the whole answer, found in one call |
| 15 | `engagement.summarize(group_by=verification_verdict)` | What did we know about the addresses | `auto` | *Worked example:* accepts-all is 41% of the new batch against 6% of the old. Accepts-all is terminal — no check resolves it and only a real send does (B8) |
| 16 | `engagement.summarize(group_by=enrichment_source \| import_batch)` | Which source, which batch | `auto` | All four targeting dimensions are group-by keys precisely so this axis is diagnosable |
| 17 | `engagement.summarize(group_by=step)` | Which step lost the replies | `auto` | Per-step figures need both bases — the step's own sends and the originally enrolled cohort — or survivorship makes late steps look better than early ones |
| 18 | `template.get` | Which version does each live campaign bind, and when was it published | `auto` | A published version is immutable (E13), so the publish date is a hard date to compare against the drop date. If they do not line up, the copy did not cause it |
| 19 | `metric.compare(arms)` | Is the variant difference real | `auto` | Returns counts, the interval around each rate, the sample size the comparison needs, an underpowered flag, and **the number of reads taken**. **It never names a winner** (M7), which open question 13 asks whether practice will tolerate |
| 20 | `campaign.pause` · `sender.pause` · `approved_content.retire` | Stop the right thing | `confirm_once` (protective where it stops sending) | Pause the **campaign** for a targeting or content problem; pause the **capability** for a sender problem; retire the **content asset** where a platform gates content rather than senders. "Pause the sender" is the wrong reflex on more than one channel |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| Rewrite the copy first — it is the only axis most people can imagine acting on | **M6.** Provenance, enrichment source, verification verdict and import batch are group-by keys specifically because the most common cause of a one-week collapse is that this week's list is worse than last quarter's |
| Escalate a 3.5%→2.4% move on 200 sends — both numbers are real and the direction is clear | **M4.** The rate carries its denominator count and a typed `insufficient_volume` flag with the threshold used. The 95% interval at n=200 is several times wider than the move |
| Compare this week's cohort to last quarter's — it is the obvious comparison | **M3.** Cohort versus window is arithmetic, not preference. Each cohort row carries its age and completeness |
| Read a placement figure as an inbox rate — it is the closest thing to the number everybody wants | **M2.** It is typed as simulated placement, measured against monitored addresses with no engagement history, and excluded from every denominator |
| Build a single "sender health score" from complaint rate, bounce rate and reputation — one number is easier to act on | **F6.** Each is published by a different party, over a different window, on a different denominator. Receivers publish no bounce-rate threshold at all; every bounce threshold in the market is a sending platform's own rule |
| Declare a winning variant, because the dashboard shows one arm ahead | **M7.** Detecting a small relative lift on a low base rate needs sample sizes an SDR's monthly volume does not reach, and re-reading until a difference appears inflates the false-positive rate far above its nominal value — which is exactly what an agent re-reading metrics hourly will do |
| Conclude "deliverability is fine" from a passing header check | **F7.** The unsubscribe mechanism is three separate artefacts, and header presence proves only the first |

---

## 5. Stopping all outreach to a company that just became a customer

**The situation.** Sales closed the deal at 16:40. Four SDRs have people in cadences at that company.
Nobody wants a prospecting email arriving at a new customer tomorrow morning.

**"Stop everything at this company" is the most common operational request in B2B**, and it is the
reason the kill switch takes a scope rather than a person. Open question 10 asks how far that scope
should reach at account level: act on domain-matched people, or only report them.

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 1 | `account.search` → `account.get` | Which account is it, really | `auto` | Never queries an outside database and never costs anything |
| 2 | `account_membership.list` | Who do we hold at this company | `auto` | **Every row states its basis**: `attached` (somebody said so) · `domain_matched` · `name_matched` · `none`. This is what lets the hold report residual risk instead of claiming completion. *Worked example:* 14 rows — 9 attached, 4 domain-matched, 1 name-matched |
| 3 | `outreach.hold(scope=account, key)` | Stop, now | `auto` **protective** | It never waits for permission, because waiting is the destructive choice (L6). Its composition: pause every enrolment it can, finish the ones it cannot, **cancel open tasks, cancel scheduled messages, withdraw un-accepted handoffs, cancel outstanding meeting proposals, reject pending drafts** (I5). Returns per-item results plus **an explicit statement of which people it could not reach, and why** |
| 4 | Read the hold's ledger | What actually stopped | — | *Worked example:* 11 enrolments paused · 2 finished because they could not be paused (**position lost — the result names those two people**) · 6 tasks cancelled · 3 scheduled messages cancelled · 1 un-accepted handoff withdrawn · 1 meeting proposal cancelled · 2 pending drafts rejected, **each rejection stating what else it ended** (L5). Residual risk: 1 name-matched row with no explicit attachment |
| 5 | `scheduled_message.list(scope)` | Verify nothing pending survived | `auto` | **Prevents:** the single most-missed failure in this chain — a queued scheduled send, invisible in every campaign view, arriving at the new customer tomorrow. A stop that leaves one queued has stopped nothing |
| 6 | `suppression.add(scope=domain or account, reason_class=commercial, reason=existing_customer, channel_scope=all, purpose_scope=all, key)` | Make it durable | `confirm_once` | Required for a domain or account pattern. **The reason class is the whole decision here** (A11). Channel and purpose scope go to the widest, because widening later is free and narrowing later is a breach (A12) |
| 7 | `account.update(state=customer)` | Record the commercial position | `confirm_once` | **Account state is not a suppression, and a suppression is not account state.** Marking a company `customer` records a commercial position; excluding it from contact is `suppression.add`. Both are needed; neither performs the other |
| 8 | `collision.check(account)` | Confirm the live customer, support and opportunity motions are visible | `auto` | Returns who and why, never true/false — a boolean cannot distinguish "defer mine" from "hands off this company entirely" |
| 9 | `outreach.precheck` on two or three of the 14 | Verify from the outside | `auto` | Expect `deny(suppressed, commercial)` at gate 2. **Prevents:** trusting the ledger instead of the gate |
| 10 | *(months later, on churn)* `suppression.remove(entry, key)` | Lift it | `confirm_once` | **Permitted with no evidence artefact, because the class is commercial** (A11). This step is the entire reason `reason_class` exists |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| Fan a person-scoped hold out over 14 contacts — the person-scoped hold is the one the agent already knows | The scope input. Fanning out gives no atomicity, no resumability and no aggregate result at the moment speed matters most, and it silently misses anyone not in the list the agent happened to build |
| File the exclusion as `legal_obligation` because it feels important | **A11.** A retention floor over a commercial exclusion is meaningless, and "they churned" is not an evidence artefact. Filing it legally makes every won customer **permanently unmarketable** |
| Set the account state to `blocked` and stop there — it reads like a block | Account state records a commercial position and stops nothing. Gate 2 reads suppression entries, not account states |
| Trust "14 contacts held" as completion — the result says 14 | **The basis per row.** A domain-matched colleague nobody attached is real risk, and the hold reports it rather than hiding it. A name-matched row is weaker still |
| Assume paused means unchanged, because pausing is reversible | Where the hold had to **finish** an enrolment rather than pause it, the position in the cadence is gone. The result names those people, and `outreach.release` re-runs the permission check per person rather than resuming blind |
| Scope the suppression to the campaign that caused it | **A12.** An exclusion binds a brand or legal entity, never a mailbox, a sender or a campaign |

---

## 6. Someone asks never to be contacted, and later asks for erasure

**The situation.** March: *"Remove me from your list and never contact me again."* November: *"Delete
everything you hold about me."*

Handled naively, these two requests **destroy each other**: the second erases the record that makes
the first enforceable. The sequence opt-out → suppress → erasure → deleted entry → the address arrives
in next quarter's file → contacted is **the worst achievable outcome in this domain**, and it is
reachable by an agent that obeys every instruction it was given.

### Phase 1 — March, the stop

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 1 | `conversation.get`, or `optout.poll` | Where did the stop arrive from | `auto` | We are required to publish several separate stop mechanisms, so reading what happens when somebody uses one is not optional. Every event carries the channel's own event time, our retrieval time, **and the age of the backlog** — so "we have not looked in eleven days" is a visible fact rather than a silence |
| 2 | `optout.record(person, identifier, scope, channel, basis, event_time, received_at, key)` | Record the act itself | `auto` **protective** | Irreversible, and never delayed. **Two clocks, and the legal one is not ours** (A3). Where `event_time` is genuinely unknown it is recorded as unknown, and the clock runs from the earliest moment we could have known |
| 3 | *(do not do this)* `conversation.classify` | — | — | **A2.** A request to stop never sits on the meaning axis. **Prevents:** a relabel for reporting lifting a legal duty |
| 4 | `suppression.add(reason_class=legal_obligation, evidence_reference=the opt-out record, channel_scope=all, purpose_scope=all, key)` | Make it enforceable | `auto` **protective** for a single verified identifier | **List first:** adding a pattern that already exists is a refusal, so a blind retry is wrong |
| 5 | `outreach.hold(scope=person, key)` | Stop everything queued | `auto` **protective** | Enrolments, tasks, pending approvals, scheduled sends, un-accepted handoffs, outstanding meeting proposals (I5) |
| 6 | `scheduled_message.list` → `scheduled_message.cancel` | Catch what the hold could not | `auto` **protective** | **Prevents:** an opted-out person receiving the message queued in April |
| 7 | `enrollment.stop(reason=opted_out)` per participation | Close the participations | `confirm_once` | The reason is mandatory (G6) |
| 8 | `optout.confirm` — **only if the channel permits exactly one** | The single acknowledgement | `auto` | Exactly once, non-promotional, and it **may never require anything of the recipient**. A second one is a breach, not a duplicate. Otherwise: **no reply is ever sent to a request to stop** (H4) |
| 9 | Deadline computation | When must this be effective by | — | **A4.** Computed from the applicable regime and the operator's own service level, strictest wins. The published deadlines genuinely disagree — a machine-actionable unsubscribe honoured within days, a US email statute allowing rather longer, a telephone regime requiring the internal entry at the time of the request and honouring it for years, an EU-style objection stopping essentially at once with a formal reply inside a month. **The contract states the derivation and no number** |

### Phase 2 — November, the erasure

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 10 | `privacy_request.create(subject, type=erasure, key)` | Start the clock | `auto` **protective** | Failing to open the record is itself the breach. **Two clocks on a privacy request, not one:** stopping marketing takes effect essentially at once; responding formally has its own longer deadline. One due-date field conflates them, and the marketing keeps running while the response is drafted |
| 11 | `privacy_request.update` | Verify identity, or extend with a recorded reason | `auto` | Identity verification is a legitimate, bounded step. An extension's reason is recorded **because the reason has to be told to them** |
| 12 | `privacy_request.fulfill(request, key)` | Erase | `confirm_each` | It **converts** the suppression entry rather than deleting it: the converted entry is **minimised** — an irreversible hash of each identifier, the reason class, the timestamps, nothing else — and `suppression.check`, `outreach.precheck`, `contact.create`, `import.apply` and `inbound_lead.record` all still match against it (A9) |
| 13 | Read the per-store, per-identifier ledger | What actually happened | — | Four outcomes: `erased` · `minimised` · `retained_under_floor` (naming which floor) · `unreachable` (naming the store). **A single "done" is a lie in every real system**, because at least one store is always somebody else's (A10) |
| 14 | *(no operation — read the enumeration)* | What may not be erased | — | The retention floor set is **enumerated, not asserted**: (1) the minimised suppression entry; (2) the opt-out record with its event time; (3) the consent evidence bundle for outreach already sent; (4) the disclosure record bound to messages already sent; (5) the editorial-review record bound to messages already sent; (6) the audit-log entry for each of the above; (7) the privacy-request record itself with its per-store ledger. Call recordings and their consent evidence, qualification records and handoff decisions are retained under floor (3). **Everything not on this list is erasable.** **Prevents:** an implementer writing erasure as "delete the contact and everything joined to it", destroying the evidence for every other obligation at once |
| 15 | `deletion_feed.poll` → `deletion_feed.report` | Where an external register carries erasure demands | `auto` | The feed is a **pull, not a push**. Matched, unmatched and ambiguous per request — and a request we cannot match still requires a report: **"no match" is a reportable outcome, not silence** |
| 16 | `audit_log.search` | Why was this person contacted at all | `auto` | The read an auditor asks for, and the reason the audit log sits on the retention floor |

### Phase 3 — the following February, the proof it worked

| # | Operation | What happens |
|---|---|---|
| 17 | `import.apply` on a new purchased file containing that address | The row **matches the minimised entry** and is refused before it is written. Nobody had to remember. This is the single test that tells you whether an implementation got A9 right |
| 18 | `export.request` for a quarterly report | Refuses to include the identifier. **A13:** a `legal_obligation` exclusion blocks **disclosure and transfer**, not only sending. An export containing an opted-out address is a breach even though nothing was sent |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| Erase the suppression entry along with the contact — "delete everything you hold about me" plainly includes it, and leaving it looks like non-compliance | **A9.** The suppression entry is a distinct retention domain, converted to a minimised form rather than deleted. Suppression means retaining *just enough* information to respect the preference in future, and there is no automatic right to have that entry deleted |
| Report "erased — done", because every store returned success | **A10.** Per store and per identifier, four outcomes, and `unreachable` names the store. Anything else is a claim nobody checked |
| Send a friendly "sorry to see you go, anything we can do?" — it is courteous, and it is what a person would write | **H4.** No reply is ever sent to a request to stop. The one permitted acknowledgement, where a channel permits it, is `optout.confirm` — once, non-promotional, requiring nothing of them |
| Use `contact.delete` as the erasure — it is the operation whose name matches the request | **B12.** Deleting a record is not erasing a person and never lifts an exclusion. It is also not clocked, so the legal deadline is never computed |
| Use `export.request` to fulfil an access request — it produces exactly the data the person asked for | The two are kept apart deliberately: a reporting extract, versus a legally clocked act performed against a recorded request. Neither may be performed through the other |
| Run the deadline from `received_at` — it is our own timestamp and it is trustworthy | **A3.** The legal clock runs from the person's own act. Running it from our arrival time understates every deadline by the length of our own polling lag |
| Treat the erasure as the end of the marketing obligation, because the request was fulfilled | **The two clocks.** Stopping marketing took effect in March and never lapses. The erasure is a separate obligation with a separate deadline and a separate record |

---

## 7. Taking over a departed colleague's book of work

**The situation.** A rep left on Friday. Their book: 412 contacts, 88 live enrolments, 31 open
conversations, 140 open tasks, 3 un-accepted handoffs, 2 outstanding meeting proposals, and a mailbox
that is the reply path for all 31 threads. The book is to be split three ways by territory.

Two facts make this an operation and not an admin task. **A handover is four separate re-pointings,
not one** (H13). And **it reaches prospects**: rebinding the sending identity on a live thread means
the person on the other end sees a new name arrive mid-conversation (F9). That is why `book.transfer`
is declared `act`. Attrition in this role is high enough that this chain is routine, not exceptional.
Open question 15 asks whether live threads should be rebound at all, or whether the mailbox should
stay alive instead.

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 1 | `actor.get(departed person)` | What state are they in | `auto` | Active, ramping, unavailable, gone. **Administering people is not in this contract**; this family reads their state and refuses to write against anyone who is not active |
| 2 | `workload.get(departed person)` | The inventory | `auto` | **One row per binding, each flagged valid or invalid against the person's current state.** H12: a person becoming non-active invalidates **every existing binding to them**, not merely future writes — escalation targets, pending approvals, review assignments, ownership, senders behind live enrolments |
| 3 | `enrollment.list(owner)`, `conversation.list(owner)`, `task.list(assignee)`, `handoff.list`, `approval.list`, `meeting.list`, `escalation.list` | The detail behind each binding | `auto` | Each is a different binding and none implies the others |
| 4 | `workload_policy.get` for each receiver | Is there headroom | `auto` | **H11: human capacity is enforced, not advisory.** Where a ceiling is declared, transfer refuses or defers past it **per item**. Where none is declared, the answer is "none declared" — never a soft number nobody honours |
| 5 | `operation.preview(book.transfer)` | What exactly moves, and who sees a change | `auto` | **Mandatory: the transfer refuses unless it references a preview of itself.** The only operation in the contract with that requirement, because the blast radius is a whole book |
| 6 | `book.transfer(scope_filter, target or assignment_policy, key)` | Move it | `confirm_once` **+ preview** | Takes a scope filter and **either a named recipient or an assignment policy**, because a real handover usually splits a book three ways. *Worked example:* 412 contacts split by territory — 180 / 154 / 78; receiver B is at ceiling, so **26 items return `deferred_over_capacity`** rather than landing silently |
| 7 | Read the per-item transfer ledger | What moved and what did not | — | **The ledger is the operation's real output.** Before repeating, read the previous run's ledger. **Prevents:** a partially applied transfer repeated wholesale, re-pointing threads that were already re-pointed — and each re-point is visible to a prospect |
| 8 | `sender_binding.set` for campaigns still bound to the departed mailbox | Where do new touches send from | `confirm_once` **+ preview** where it rebinds live participations | Conditional reach: `control` for new entrants only, `act` where it rebinds live participations. Resolved by the preview, never assumed |
| 9 | `sender.disconnect(departed mailbox)` | Detach the mailbox — **last, not first** | `confirm_once` | **Refused while live enrolments or open conversations depend on it, unless a rebind target is supplied** (F8). **Prevents:** destroying the reply path for 31 open conversations, so that every prospect's answer bounces |
| 10 | `ownership.assign(subjects, target, on_conflict, reason, key)` | The durable owner of contacts and accounts | `confirm_once` | **Mandatory `on_conflict` with no safe default:** `refuse` (take it only if unowned — which is how two people racing a shared queue produce one winner and one named `already_owned` refusal) or `reassign` (deliberate handover, reason required). **It changes one binding**, and declares that it changes none of the others (H13) |
| 11 | `task.reassign` | The queue | `confirm_once` | Separate binding, separate operation. Returns `deferred_over_capacity` per item where a ceiling would break |
| 12 | `conversation.assign` | The inbox | `auto` | Internal routing only. Nothing is promised and no clock starts |
| 13 | `handoff.withdraw` / `handoff.return` | Handoffs in flight | `confirm_once` | Withdraw before acceptance; an already-accepted handoff **refuses withdrawal and must be returned by the receiver** |
| 14 | `account.release` where explicit; leases expire by themselves | Claims on accounts | `auto` | **A leased claim expires**, because unbounded claims plus normal turnover means permanently locked accounts |
| 15 | `autonomy.set` | Re-point every escalation target that named them | `confirm_once` | **A run whose escalation target is invalid must not start** (H12, L2). **Prevents:** the forgotten step that makes an unattended run silently unsafe — a 3am escalation into a void |
| 16 | `plan.validate` on anything scheduled that named them | Does it still run | `auto` | One verdict per planned step: authority, ordering, preconditions, live bindings, budget headroom, capacity |
| 17 | `goal.define` / `goal.get` | Targets for the period | `confirm_once` on define | **The unit is immutable inside a period**, because changing it mid-period invalidates every figure already reported |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| File the whole thing as internal admin — nothing is being sent; it is a personnel change | **F9 and the `act` declaration.** Rebinding a sending identity mid-conversation is visible to the prospect. Eighty-eight prospects would see a new name arrive mid-thread with no explanation, approved as an admin task |
| Disconnect the mailbox first, to "close it out cleanly" — the tidy first move on a leaver's checklist | **F8.** Refused while live enrolments or open conversations depend on it, unless a rebind target is supplied |
| Assume `ownership.assign` moved everything — one operation named "ownership" ought to move ownership | **H13.** Ownership is four independent bindings: record owner, sending capability behind live enrolments, task assignment, conversation assignment. Each operation declares that it does not change the others |
| Repeat a partially applied transfer wholesale, because the first run errored halfway | The *before repeating* clause: read the previous run's per-item ledger |
| Land 140 tasks on one person — the receiver is the territory owner and that is the rule | **H11.** Capacity is enforced, per item, with `deferred_over_capacity` as a named outcome, not a soft warning |
| Leave the overnight run pointed at the leaver — the run is configured and working | **H12.** A non-active person invalidates every existing binding, and the run must not start |

---

## 8. An unattended overnight run

**The situation.** A run is scheduled for 02:00: enrol 120 people, send step 1 to everybody eligible,
work the reply queue at 06:00. Nobody is awake.

Everything in this chain exists because of one asymmetry: **when nobody is present, every ambiguity
resolves in whichever direction the code happens to take, and the contract's job is to make that
direction safe by construction rather than by attentiveness.**

### Before it starts — the pre-flight, and none of it is optional

| # | Operation | The decision | Gate | The check |
|---|---|---|---|---|
| 1 | `capability.list` | What may this credential actually do | `auto` | Granted, denied or **unknown**, one answer per capability. `unknown` is legal and common, and no plan may assume an authority it could not read (A6, N5). Re-read at the start of every unattended run, because authority changes when the credential changes |
| 2 | `autonomy.get` | What runs without asking | `auto` | The mode, the queue timeout **and what a timeout means**, the unattended action cap, the escalation target, per-channel asymmetry, and **where the gate lives** — this contract, the calling runtime, or nowhere. **When it is nowhere and the run is unattended, every operation above `auto` refuses.** Double-prompting and no-prompting are both defects, and only a declared gate location prevents both |
| 3 | `workload.get(escalation target)` | Is the named human actually there | `auto` | **A run whose escalation target is invalid must not start** (H12). **Prevents:** an escalation that is a message into a void |
| 4 | `budget.get` per meter | What allowance is left | `auto` | `unknown` remaining is a real answer and **is not the same as ample remaining** (A6). `budget.set` is a **hard stop, not an alert** — an alert that stops nothing is a different feature (C5) |
| 5 | `stoprule.list` | What will stop this by itself | `auto` | Confirm rules exist on the subjects that matter: bounce rate, complaint rate, negative-reply rate, opt-out rate, error rate, spend velocity, **sender health, domain reputation and remaining budget** (L8) |
| 6 | `sender.health` over every capability in the run | Can they send at 02:00 | `auto` | Restrictions, warm-up position and whether it was invalidated, capacity budgets, automation sanction. **F4:** if a restriction was recorded, the warm-up position is invalid, and resuming requires a ramp restart or a recorded override |
| 7 | `capacity.estimate` | Can the plan physically run over the horizon | `auto` | Consumes the same budget shape, including `unknown`, and never invents a second capacity model |
| 8 | `send_window.resolve` for the population | Is 02:00 legal for these recipients | `auto` | **Quiet hours resolve in the recipient's local time** and no operator policy may widen them (A20). A run scheduled at 02:00 our time is 20:00 or 09:00 somewhere |
| 9 | `plan.validate` | Check the whole plan before any of it runs | `auto` | One verdict per planned step: authority, ordering, preconditions, live bindings, budget headroom, capacity. **N5: authority resolves at plan time.** **Prevents:** discovering a refusal at step 40 of 200, with 39 acts done, 161 not, and no defined resumption |
| 10 | *(if used)* the standing approval record | May a named artefact go out unattended | **human**, recorded in advance | A recorded human decision: it names the artefact, names the actor, carries a ceiling and **expires**. It is how a time-critical act runs with nobody awake, and it is **not an exemption from the declared approval floor** |

### During the run

| # | Operation | The rule |
|---|---|---|
| 11 | `outreach.precheck` → the act | The gates are evaluated **again** immediately before each act, and a batch prepared against the pre-flight check is re-screened at dispatch (A7) |
| 12 | every `act`, every collection write, every durable-object write, every metered call | Carries a **caller-supplied idempotency key** (N1) |
| 13 | on a lost connection: `invocation.get(key)` | Returns `never_seen` or `seen_and_failed`. **These must never be collapsed.** A timed-out send may already have succeeded, so the recovery is a lookup, never a blind retry |
| 14 | an approval that times out at 03:00 | **Resolves as refuse-and-halt.** It never proceeds; the item stays pending for a human and the run stops (L2) |
| 15 | a stop rule fires | Produces a record naming **the trigger value, the operations halted, and every item left unprocessed** (L8). An unattended run has no human present to remember |
| 16 | `escalation.raise` | Pages a named human and **grants nothing** (L4). Takes a key, because a replayed raise pages a human twice at 3am. An open escalation is a reason to add context, not to raise a second |

### The morning after

| # | Operation | What it answers |
|---|---|---|
| 17 | `stoprule_firing.list` | **Why sending is off.** The rule, the measured value that tripped it, what was halted, what was left unprocessed |
| 18 | `job.get` | Per-item progress on anything queued |
| 19 | `invocation.get` for anything unresolved | Whether the lost call actually happened |
| 20 | `trigger_run.list` | What the standing rules did — **including evaluations that produced nothing, and work suppressed by a permission or frequency rule** (J5). A rule that quietly stopped working looks identical to one with nothing to do |
| 21 | `approval.list` | What is waiting, and how long it has waited |
| 22 | `engagement.summarize` | Sends **and** did-not-happen counts by reason (M5) |

**The mistakes this chain intercepts.**

| The tempting mistake | What stops it |
|---|---|
| Read an approval timeout as consent — nobody objected, the work is time-critical, and halting feels like failing | **L2.** An approval timeout inside an unattended run resolves as refuse-and-halt. It never proceeds. This is the highest-consequence line in the oversight family |
| Treat `unknown` capability or `unknown` remaining budget as "probably fine" — it is 02:00 and there is nobody to ask | **A6.** `unknown` never reads as `allow`. The honest options are `allow_at_risk` with the unknown named, or a denial — and for an unattended run, a denial |
| Blind-retry a send that timed out — retrying a failed request is correct in every other system | **N1.** A timed-out send may already have succeeded. The recovery is `invocation.get(key)`, which distinguishes `never_seen` from `seen_and_failed` |
| Use a standing approval as a lowered approval class — it looks like the mechanism for "this may run unattended" | It is a recorded human decision with a named artefact, a ceiling and an expiry, not a reduction of the class. The floor still applies to everything the record does not name |
| Run with an escalation target who left — the configuration is unchanged and nothing failed | **H12.** A non-active person invalidates every existing binding, and a run with an invalid escalation target must not start |
| Discover the authority refusal at step 40 — checking authority for 200 steps up front feels wasteful | **N5.** `plan.validate` returns one verdict per planned step |
| Schedule "02:00" as an absolute time, because it is our maintenance window | **A20 and E8.** Quiet hours resolve in the recipient's local time. Whose timezone is a policy with an ordered fallback, and the recipient's region being unknown is the normal case |
| Assume the run stopped cleanly because nothing errored — the log is clean | **L8.** A stop rule that fired produces a record with the items left unprocessed. Without reading it, "nothing errored" and "140 of 200 were never attempted" look identical |
