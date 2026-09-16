# Routing and binding

How a campaign's provider work reaches a plugin, and which account at that provider it is done in.

This document is for whoever operates an installation. What a plugin is and how to write one is
[docs/plugins.md](plugins.md); what a canonical operation means is
[docs/contracts/](contracts/README.md); how work items are claimed and answered is
[docs/work-execution.md](work-execution.md).

## 1. What routing and binding answer

**Routing** answers *which plugin performs this operation for this campaign*. **Binding** answers
*which account, workspace or mailbox at that provider the plugin works in*. They are separate on
purpose: one installation routinely sends every campaign to one provider and works several of its
accounts, and one campaign occasionally needs a different provider for a single operation.

Nothing is routed anywhere by default. A fresh runtime routes no operation to any plugin, so every
`provider_op` work item fails at the claim with `no_route` until somebody writes a route — and a
product that shipped a default route would make its vendor-neutrality a claim its own configuration
contradicted.

## 2. Two scopes, four levels

Routes live at two scopes — **global**, in `settings.json`, and **campaign**, in the database — and
each scope has a **default** route and per-**operation** overrides. That is four levels, and the most
specific one wins:

```text
campaign operation override  →  campaign default  →  global operation override  →  global default
```

**Masking is exact, and it is the one rule people get wrong.** A campaign default hides *every* global
operation override for that campaign. Given these four routes:

| Level | Route |
|---|---|
| campaign `cmp_A`, operation `campaign.get` | — |
| campaign `cmp_A`, default | `provider-b` |
| global, operation `campaign.get` | `provider-c` |
| global, default | `provider-d` |

the answers are:

| Asked for | Answer | Scope |
|---|---|---|
| `cmp_A`, `campaign.get` | `provider-b` | `campaign_default` — the global override for `campaign.get` is hidden |
| `cmp_A`, `list_membership.add` | `provider-b` | `campaign_default` |
| `cmp_B` (no routes of its own), `campaign.get` | `provider-c` | `global_operation` |

Keeping the global exception inside `cmp_A` takes an explicit campaign override:

```sh
jason route set --campaign cmp_A --operation campaign.get --plugin provider-c
```

The alternative — merging the levels — would mean a campaign that named its provider could still have
one operation quietly sent somewhere else, which is exactly the surprise routing exists to prevent.

**Bindings are not merged either.** The level that wins brings its own binding, or none: a campaign
override does not inherit the account the global route named, so an override that needs one says so.
Nothing anywhere combines two bindings into a third.

## 3. The global routes, in `settings.json`

```json
{
  "Routes": {
    "Default": {
      "Plugin": "acme-provider",
      "Binding": { "workspace": "eu-team" }
    },
    "Operations": {
      "campaign.get": { "Plugin": "other-provider" }
    }
  }
}
```

`Plugin` is a plugin id — the directory name under `~/.jason/plugins/`. Each key under `Operations`
must name an operation this build publishes a contract for. `Binding` is optional and is whatever the
plugin's manifest says a route to it must carry.

**A global route is frozen into the active route snapshot by `jason plugin reload`, and by the load at
startup — editing the file changes nothing until then.** It is the same discipline plugin grants
already follow, and for the same reason: what is running should change when somebody says so, not when
a file is saved. `jason runtime status --human` names the snapshot that is actually active — here on a
runtime whose global default is one plugin and which also holds one campaign route:

```text
Routes:     snapshot rts_01M2M7H79NXYND9TAPQFDEQTHY · default fake-provider · 0 overrides · 1 campaign route
```

## 4. A campaign's own routes

```sh
jason route set   --campaign cmp_… --plugin acme-provider --binding '{"workspace":"latam"}'
jason route set   --campaign cmp_… --operation campaign.get --plugin other-provider
jason route unset --campaign cmp_… --operation campaign.get
jason route list  --campaign cmp_…
jason route resolve --campaign cmp_… --operation list_membership.add --human
```

A write takes effect at once: the row is written and a new route snapshot is activated in the same
act, under the same gate a reload takes. **It never re-reads `settings.json`** — the new snapshot
copies the global half from the snapshot already active. Changing one campaign's route is not the act
that activates a global edit somebody left in the file, and the operator who edited the file and then
wrote a campaign route will see the *old* global set in the answer. That is not a stale read; it is
the freeze rule seen from the other side. `jason plugin reload` is what picks the edit up.

**There is no global `route set`.** The API writes campaign routes only, and the CLI refuses the command
before it sends anything — a usage error, exit 2 — saying where a global route lives instead:

```text
--campaign is required: only a campaign's routes are written through the API. A global route is
written under "Routes" in settings.json and activated with 'jason plugin reload'.
```

The reason is worth stating rather than accepting: a reload freezes plugins and routes together, so a
global route becomes active by the same explicit act as the packages it names.

Three smaller rules, each of which somebody will meet:

- **A route is checked before its row is written.** A `route set` that could never activate is refused
  as a 400 naming the route, rather than written and then refusing its own activation.
- **An archived campaign is refused** with the same `campaign_archived` it answers everywhere else.
- **`route unset` of a route that is not there succeeds and changes nothing** — it is the state the
  caller asked for, and nothing is activated because nothing changed.

### `route resolve` has two different answers

`route resolve` is the diagnostic verb: it asks the active snapshot — the same one the claim reads —
where this campaign's work would go and whether the plugin there could take it.

- **Nothing resolves** → `no_route`, a 404 (CLI exit 1). That is a configuration fact: no route sends
  this operation anywhere for this campaign.
- **A route resolves and cannot run** → a 200 with `"usable": false` and the problems listed.

The problems in that second answer carry **attempt-error codes, not route-problem codes**, because
resolve is predicting what the claim would decide and therefore speaks the claim's vocabulary. §7 has
both lists.

**`usable: true` is about the route, not about an item.** Of the twelve reasons a claim can refuse work
(§7), resolve answers the first two in its own words — an operation this build does not publish is a
400, nothing resolving is the 404 above — and asks the five that need nothing but a route and a plugin
set, numbers 3 to 7. The last five are questions about a work item, which resolve does not have:
whether the operation needs a person's approval, whether a contact is named, whether that person is
reachable on the channel the operation consumes, whether that value is suppressed, and whether the
composed input satisfies the schema. So `route resolve --operation campaign.enroll` answers
`usable: true` against a plugin that implements it, and every item of that operation still fails
`approval_required` at the claim.

The most common real `usable: false` is a **default** route, and it is worth showing. A default route
is validated only against the operations its plugin already claims, so a global default to a provider
that implements two of the three published operations activates cleanly — and then answers this for
the third:

```text
Operation:  campaign.enroll v1
Campaign:   cmp_01M2M7JBD1D9MEMMJQAJCWZV3H
Plugin:     other-provider 1.0.0 · valid
Scope:      global_default
Binding:    -
Snapshots:  routes rts_01M2M7JAP1E2T14EFE0XG7MGVA · plugins snp_01M2M7JAE3KB7Q2MXN266SDKQQ
Usable:     no
problems:
Routes:Default: plugin_operation_unsupported — Plugin 'other-provider' does not perform 'campaign.enroll', and work is never handed to a plugin the route did not name.
```

**`route list` always answers the global set**, even when it is filtered to one campaign, because the
global set is what that campaign falls back to; leaving it out would leave out half the answer. A
campaign nobody can name is `campaign_not_found`, never an empty list.

### What a global binding cannot express

The configuration system is a string store: a settings file is flattened into string keys and string
values before anything reads it. Objects, arrays, booleans and numbers are rebuilt faithfully. The one
thing that cannot survive the round trip is **a string that is spelled like a number or like a
boolean** — by the time the `Routes` section is read, `12345` and `"12345"` are the same five
characters, and the value reaching the plugin is the number.

The escape hatch is real and is in the same place: set that binding **per campaign** with
`jason route set --binding`, which carries JSON and never passes through the configuration system.

## 5. The binding handshake

A plugin's manifest may declare `binding:` — a schema, in the dialect of `docs/contracts/`, for what
an installation must tell it before it can act. A route to a plugin that declares one must satisfy it;
a route to a plugin that declares none may carry any JSON object, up to 64 KiB (a larger one is
refused at the invocation with `plugin_binding_too_large`, which is permanent). The value reaches the
plugin as `context.binding`. **Nothing in the runtime interprets what it means** — that is the plugin's
business — though the runtime does hold it to the plugin's schema, refuse a credential-shaped name in
it, hash it into each attempt's provenance, journal it and show it in `route list`.

**A binding selects an identity the plugin's own credential store already holds; it never carries the
credential.** A binding is journaled when it is written and shown in full by `route list`, so a field
named like a credential is refused at activation, at any depth:

```text
campaign:cmp_…/routes/default: route_binding_secret_like — a binding must not carry 'api_key'. A binding
selects an identity the plugin's own credential store already holds; it is journaled and listed, so it
never carries the credential itself.
```

A credential reaches a plugin the way every secret does: through a variable its manifest declares under
`capabilities.env`, granted by the user and read with `host.env`.

## 6. Activation, and what refuses it

A reload builds the whole candidate route set — the global half from `settings.json`, the campaign half
from the rows of every campaign that is not archived — and checks every route in it against the plugin
set that load produced and against the operation catalog. Only if nothing objected are the two
registries swapped, plugins first and routes immediately after.

**A single bad route rejects the whole reload.** The answer is 409 `plugin_reload_rejected` with
`retryable: false`, both snapshots stay exactly as they were, and each detail names the route the way
the operator wrote it:

```json
{"error":{"code":"plugin_reload_rejected","message":"The plugin reload was rejected: 1 problem(s) in the candidate set; the previous snapshot stays active.","retryable":false,
  "details":[{"field":"Routes:Default","code":"route_plugin_unknown","message":"no plugin with id 'no-such-plugin' is in the candidate set, so nothing would perform this work."}]}}
```

| Where the route is | How it is named |
|---|---|
| the global default | `Routes:Default` |
| a global operation override | `Routes:Operations:<operation>` |
| a campaign route | `campaign:<cmp_…>/routes/<operation>`, or `campaign:<cmp_…>/routes/default` |

The load at startup takes the same path: a runtime started with a bad route reports it in the startup
report and leaves **both** registries empty, rather than running with plugins and no routes.

**An archived campaign's routes are ignored.** History must never stand between an operator and an
uninstall: the campaign will never dispatch again, so what its route named no longer has to exist.

An activated load journals `plugins_reloaded` with the routes it froze beside the packages:
`routes: {snapshot_id, global_default, override_count, campaign_route_count}`. A route write journals
`routes_updated` against the campaign, keyed by the operation or by `default`, with the old and new
values.

## 7. Fail-closed: every reason, and what to change

Nothing falls back. An operation the routed plugin cannot perform ends the work item with the reason;
handing the work to whichever other plugin happens to implement it would mean a campaign acting against
an account nobody named.

### At the claim — the twelve, in the order they are checked

Every one of these is decided **before a child process exists**, and the first that fails is the one the
attempt records. The attempt is kept either way, with the context it was claimed with and the provenance
as far as the decision got, so a manager can see what would have run. None of them is retried.

| # | Code | Class | What it means, and what to change |
|---|---|---|---|
| 1 | `operation_unknown` | permanent | this build publishes no contract for the operation the item names. The item was written by a build that did; nothing but a different build will run it |
| 2 | `no_route` | permanent | nothing sends this operation anywhere for this campaign. Write a route — `jason route set`, or the `Routes` section plus a reload |
| 3 | `plugin_not_loaded` | permanent | the route names a plugin the active set does not hold. Install the package and reload, or re-point the route |
| 4 | `plugin_unavailable` | permanent | the package is installed and listed, but its environment holds it back — usually a program it declares is not on the machine. Repair what `jason plugin list --human` names, then reload |
| 5 | `plugin_operation_unsupported` | permanent | the plugin does not perform this operation, or is not a kind of plugin that performs any. Route this operation to a plugin that claims it |
| 6 | `contract_incompatible` | permanent | the plugin speaks no version of this operation's contract that this runtime publishes. Upgrade the plugin |
| 7 | `binding_invalid` | permanent | the route's binding does not satisfy the schema the plugin declares — including a route with no binding to a plugin that requires one. Fix the binding |
| 8 | `approval_required` | permanent | the operation needs a person's approval, and a dispatcher may not stand in for the person who approves it. Nothing in this version can give one (§12) |
| 9 | `contact_required` | permanent | the operation acts on somebody and the work item names nobody. Create the item with `--contact` |
| 10 | `no_channel_value` | permanent | the person is not reachable on the channel the operation consumes. Add the channel to the contact, or name a channel they have |
| 11 | `suppressed` | permanent | that channel value is on the do-not-contact register. It is not reached, and that is the register working |
| 12 | `input_invalid` | validation | the composed input does not satisfy the operation's own schema, or an argument contradicts an identifier Jason has already pinned. The details carry a JSON pointer each |

Two of these are worth a second sentence. `input_invalid` is the only one classed `validation` — it is
the one a planner can fix by editing the item; everything else is structural. And an argument that
disagrees with a pin is refused rather than resolved either way: the rule is to work by the pin, so the
message names the pinned value.

### At activation — seven route problems, plus one for the section

| Code | When |
|---|---|
| `route_plugin_unknown` | the route names a plugin the candidate set does not contain |
| `route_plugin_kind_not_invocable` | the plugin is a notification plugin, which performs no canonical operation |
| `route_operation_unknown` | a *campaign* route names an operation this build does not publish. A global override keyed by such a name never reaches this check: the section validator refuses it first, as the eighth code below |
| `route_operation_unsupported` | the plugin does not list the operation the route sends it |
| `route_contract_incompatible` | the plugin speaks no version of that operation's contract |
| `route_binding_secret_like` | the binding carries a field named like a credential, at any depth |
| `route_binding_invalid` | the binding does not satisfy the schema the plugin declares |

The eighth is not a problem with a route but with the **section**: `routes_settings_invalid` is the
`Routes` section itself failing the validator that reads it, which happens when the file is edited into
something no route could be built from. The options system reports that as prose rather than as
structure, so the runtime carries the validator's own sentence through verbatim and names the setting it
came from. It arrives as the same rejected reload, because an operator who mistypes a route must get one
answer — and never a 500, which would say the runtime broke rather than the edit.

**Two facts about the order that would otherwise surprise you:**

- **A notification plugin is reported as not invocable, not as not supporting the operation.** Such a
  plugin lists no operation at all, so asking "does it implement this one?" first would answer with what
  is missing rather than with what it is.
- **A credential-shaped binding is refused before a binding that merely fails the schema.** A plugin's
  own schema will usually refuse an unexpected field too, and `additional_properties` is not the sentence
  whoever pasted a credential into a settings file needs to read.

The two vocabularies are deliberate. A route problem is read by an operator repairing an installation; an
attempt error is read by a manager reading why one item failed. Five of the questions — is the plugin
there, is it a kind that performs operations, does it list this one, does it speak the contract's
version, does the binding satisfy its schema — are one implementation asked by both gates, so the two
can only differ in wording.

**Two questions belong to exactly one gate, deliberately.** `plugin_unavailable` is asked only at the
claim: a package whose program is missing from this machine is an environment's problem, and refusing
every route to it would answer that with a rejected reload. `route_binding_secret_like` is asked only at
activation: that is where a binding is written, and nothing at claim time could still refuse one.

## 8. What "ambiguous" costs, in operator terms

A failed provider attempt carries one of four classes, and the class decides what happens next:

- **`transient`** — nothing happened; the item comes back and is attempted again, within its own
  `max_attempts`.
- **`permanent`** and **`validation`** — it would fail the same way again; the item is finished.
- **`ambiguous`** — *the provider may already have acted*, and the answer never arrived.

For an ambiguous end the runtime reads the operation's own contract rather than guessing. `campaign.get`
says a repeat is `safe`, because asking for a campaign a second time costs nothing but a round trip. `list_membership.add` says
`after_recovery_read`, so a repeat is allowed only because the contract obliges the plugin to read the
ledger under the idempotency key, and the membership it writes, before writing anything — which is what
makes one crash cost one effect rather than one bill. An operation that said `never` would end for a
person rather than risk a second send; none of the published three does.

Every end where nobody answered is ambiguous: the invoker's timeout, a lease that ran out, a missed
heartbeat. A missing answer says nothing about whether a provider acted, and the honest class is the one
that says so. An attempt a restart finds still `scheduled` is not such an end — the handler commits the
attempt started before it launches anything, so that one never ran and goes back uncounted.

**`result_invalid` is never repeated.** An answer that arrived and does not satisfy the operation's output
schema is a defect in the plugin: the next attempt would run the same code over the same answer and could
only spend budget while somebody waits. It is recorded as ambiguous — the call did reach the provider —
with `retriable: false`, whatever the operation's repeat rule says. The failing pointers are on the error,
and the answer that was refused is kept on the attempt's provenance so its author can see what was sent.

## 9. Budgets, and what they cost the handler pool

Three budgets, and they are not the same number:

- **The child's budget** is the operation's own `timeout_ms`, from its contract — 60 s for
  `campaign.get`, 120 s for `list_membership.add`, 300 s for `campaign.enroll` — lowered by the
  plugin's ceiling below where that is smaller. It is never what is left of the lease: the same
  operation must not behave differently because the claim before it was slow.
- **The lease** is `Dispatcher:ProviderOp:TimeoutSeconds`, **600 s** by default, and it has to outlast the
  child. A configuration where it does not is refused when the runtime starts, naming the operation that
  forces the floor.
- **The plugin's ceiling** is `limits.timeout_ms` from its manifest when it declares one, and
  `Plugins:Limits:TimeoutMs` — **300 s**, the budget of the slowest operation this build publishes —
  when it does not; either is itself capped by `Plugins:Limits:MaxTimeoutMs`. The ceiling can only
  lower the child's budget, never raise it, so a package that says nothing about limits leaves every
  published operation the budget its contract asks for, and one that declares 20 s caps a 300 s
  operation at 20 s. Lowering the setting below the slowest published operation is refused when the
  runtime starts, for the same reason and in the same words as a lease that cannot hold one. The
  manifest is not held to anything of the kind: a plugin implementing a slow operation has to declare
  a limit at least as large as that operation's contract, and nothing checks that for it (see
  [docs/plugins.md](plugins.md) §7).

An item may ask for a shorter lease of its own — `--timeout` — but **not shorter than the operation
needs**, and the refusal names the floor rather than quietly raising the number:

```text
timeout_seconds: too_short — timeout_seconds must be at least 305 for 'campaign.enroll', which declares
300000 ms, plus the 5000 ms a child is given to stop.
```

**What the 600 s lease costs you:** a stuck provider item now holds a handler slot for up to ten minutes,
and `Dispatcher:MaxParallel` (4 by default) bounds **both** kinds of work — there is one handler pool, not
one per kind. A run of slow provider items can therefore starve agent work, and `MaxParallel` is the
number to raise.

## 10. Provenance: what ran, pinned to the attempt that ran it

Every provider attempt records what was decided for it, written at the claim and completed when the
invocation ends. Nothing later rewrites it: a route changed, a plugin reloaded or a package edited
afterwards leaves a finished attempt exactly as it stands.

```text
ATTEMPT 1 (att_01M2M7H924BZ5FS6CSZ794J1R8) RAN
Plugin:       fake-provider 1.0.0 · 9a357c7bfe4e
Operation:    list_membership.add v1
Route:        global_default · binding a728b92b6cdc
Snapshots:    plugins snp_01M2M7H79JANSWPE9EFGTGG7DH · routes rts_01M2M7H79NXYND9TAPQFDEQTHY
Invocation:   pin_01M2M7H97ED18X6V82B19FA85A · 478 ms · exec 2 · http 0 · log 0
```

Read it as: which package and at which content digest, which operation at which contract version, which
level of routing chose it and **which account by identity**, the two snapshots the decision was made
against, and what the run cost. `jason workitem get <id>` has all of it as JSON, including the
identifiers the plugin returned and — after a `result_invalid` — the answer that was refused.

**The binding travels as a hash, not as a value.** `binding a728b92b6cdc` is the head of a SHA-256 over
the binding's canonical JSON, which is what makes "was this retried against a different account?"
answerable without putting an account descriptor on every attempt row. The value itself is in the journal
and in `route list`, where an operator can read it.

An attempt that never ran still carries what the decision reached: an item refused for `plugin_unavailable`
names the plugin it would have used, and one refused for `no_route` names none — the nulls say exactly how
far the decision got.

## 11. External identifiers: what the provider calls our people

A plugin answers with the provider's own identifiers, and Jason writes them down:

```text
EXTERNAL IDS
PLUGIN         KIND     VALUE   DISPUTED  DISPUTED BY
-------------  -------  ------  --------  -----------
fake-provider  contact  p_1001  -         -
```

- A pin is per plugin and per kind, on a contact or on a campaign, and it is written by a plugin's answer
  and by nothing else. An `external_ids` field posted to `contact.update` is ignored; no caller can write
  a pin.
- **A pin is never overwritten.** The same value again changes nothing. A *different* value is recorded
  beside the pin as a **divergence**, with the attempt that returned it — the original stands, both values
  are visible, and later attempts keep receiving the value Jason recorded rather than the one it disputes.
- The attempt **still succeeds** when a pin diverges. The plugin's answer is the plugin's; the pin is
  Jason's record. Failing the attempt would throw away work that really happened at the provider, and
  silently overwriting would destroy the only evidence that two records disagree.
- A plugin only ever sees **its own** pins. What another provider calls the same person is never its
  business.
- The journal carries both events: `external_id_pinned` and `external_id_diverged`, keyed
  `contact/<plugin>/<kind>` or `campaign/<plugin>/<kind>`.

**Resolving a divergence is deliberately not implemented.** Deciding which record is right is
reconciliation — matching a person again, against two providers' answers — and this version has none
(DEF-RECON-001). A human sees both values and decides.

## 12. Known limitations of this version

- **No operation-family wildcards.** A route names one operation or is the default; there is no
  `list_membership.*` (DEF-ROUTE-001).
- **No named, reusable bindings.** Every route carries its own binding object, so two campaigns working the
  same account repeat it (DEF-ROUTE-001).
- **No repair path for a pin.** A pin cannot be corrected, moved or deleted through the API, and a
  divergence stays visible until reconciliation exists (DEF-RECON-001).
- **A retry is claimed against whatever is active then.** A running attempt keeps the package and route it
  was claimed with — that is what invocation pinning is for — but an item that comes back after a
  retriable failure is claimed afresh, so a later attempt may run against a different package or a
  different account than the one before it, and nothing warns about that. What *should* happen to queued
  work across a reload is open (DEF-ROUTE-002).
- **No approvals.** `campaign.enroll` requires one, so it fails closed with `approval_required` every time.
  The contract is published complete; only the gate is missing.
- **Three operations, not a catalog.** `campaign.get`, `list_membership.add` and `campaign.enroll` are the
  whole published set; an operation outside it cannot be routed, created or named, and adding one is a
  deliberate act with its own document, fixtures and version (DEF-OPS-001).
- **No compatibility window and no deprecation policy.** Every published operation is at version 1, and a
  plugin declares which contract family versions it implements. What happens when version 2 arrives — how
  long version 1 keeps being served, and how a plugin is told — is not settled and is deliberately not
  implied anywhere (DEF-OPS-002).
- **One installed version per plugin id**, because the id is the directory name. Two plugins may implement
  the same operation, and routing is how you choose between them.
- **Routes are not exported or imported.** The global half is a settings file you can copy; the campaign
  half is database rows, readable through `route list` and writable only one route at a time.

The design these follow from — routing and binding as independent concepts, fail-closed with no implicit
fallback, and the registers the limitations above name — is in
[docs/architecture.md](architecture.md), §13.
