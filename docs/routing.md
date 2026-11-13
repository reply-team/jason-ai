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
accounts, and one campaign occasionally needs a different provider for a single operation, or for one channel of
it: email through one plugin, LinkedIn messages through another.

Nothing is routed anywhere by default. A fresh runtime routes no operation to any plugin, so every
`provider_op` work item fails at the claim with `no_route` until somebody writes a route — and a
product that shipped a default route would make its vendor-neutrality a claim its own configuration
contradicted.

## 2. Two scopes, six levels

Routes live at two scopes — **global**, in `settings.json`, and **campaign**, in the database — and
each scope has a **default** route, per-**operation** overrides and, for the few operations that take them,
per-**channel** routes under an operation. That is six levels, and the most specific one wins:

```text
campaign operation and channel  →  campaign operation override  →  campaign default
  →  global operation and channel  →  global operation override  →  global default
```

The answer names the level it came from as its scope: `campaign_channel`, `campaign_operation`,
`campaign_default`, `global_channel`, `global_operation` or `global_default`.

**A channel level answers only a call that names a channel.** Which operations take channel routes is data in the
operation's own document (`route_channel_argument`, [docs/contracts/](contracts/README.md)): `message.send` and
`conversation.reply` read the channel from `channel`, and `sender.list` from its optional `sender_channel` filter,
so a `sender.list` without the filter is routed by the operation alone, as it always was. An operation whose
`channel` is the address a person is found by takes none, and neither does an operation with one channel only. A
call that names a channel no route is set for falls through to the operation's own route and the levels under it;
nothing is routed by a channel that was not named.

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
jason route set --campaign cmp_A --operation campaign.get --plugin provider-c --actor human:you@example.com
```

The same holds for channels: a campaign default or a campaign operation override hides a global channel route, and
keeping one inside the campaign takes a campaign route that names the channel.

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

An operation whose document names the argument a route reads its channel from may also carry **channel routes**, under
`Channels`, one route per channel, each with its own `Plugin` and optional `Binding`:

```json
{
  "Routes": {
    "Operations": {
      "message.send": {
        "Plugin": "acme-provider",
        "Channels": {
          "linkedin": { "Plugin": "social-provider", "Binding": { "seat": "sales-1" } }
        }
      }
    }
  }
}
```

The channel is a key of its own, never part of the operation's: a colon is what separates the keys of a settings path.
Where `Channels` names a route, the operation's own `Plugin` and `Binding` may be left out, and then the operation has
no route of its own at the global scope. A key under `Channels` that is no channel is refused with the section, as
`routes_settings_invalid`; `Channels` under `Default` or at the top of `Routes`, an operation that takes no channel
routes, a channel its argument does not allow, and a plugin that reads no events are refused at the reload (§7). A file
without `Channels` means what it always meant.

**A global route is frozen into the active route snapshot by `jason plugin reload`, and by the load at
startup — editing the file changes nothing until then.** It is the same discipline plugin grants
already follow, and for the same reason: what is running should change when somebody says so, not when
a file is saved. `jason runtime status --human` names the snapshot that is actually active — here on a
runtime whose global default is one plugin and which also holds one campaign route:

```text
Routes:     snapshot rts_01M2M7H79NXYND9TAPQFDEQTHY | default fake-provider | 0 overrides | 1 campaign route
```

The override and campaign counts include channel routes; a runtime that holds any adds `| N of them by channel`.

## 4. A campaign's own routes

```sh
jason route set   --campaign cmp_… --plugin acme-provider --binding '{"workspace":"latam"}' --actor human:you@example.com
jason route set   --campaign cmp_… --operation campaign.get --plugin other-provider --actor human:you@example.com
jason route set   --campaign cmp_… --operation message.send --channel linkedin --plugin social-provider --actor human:you@example.com
jason route unset --campaign cmp_… --operation campaign.get --actor human:you@example.com
jason route unset --campaign cmp_… --operation message.send --channel linkedin --actor human:you@example.com
jason route list  --campaign cmp_…
jason route resolve --campaign cmp_… --operation list_membership.add --human
jason route resolve --campaign cmp_… --operation message.send --channel linkedin --human
```

`--channel` needs `--operation`, and an operation whose document lets a route name a channel. `route list` shows a
channel route beside the operation's own and reads it `<operation> on <channel>`; `route resolve` answers for the
channel when it is given one, and for a call that names none when it is not. Asked of a runtime older than channel
routes, the CLI refuses the command before it sends anything: that runtime would ignore the channel and answer for, or
change, the operation's own route instead.

`route set` and `route unset` take no default actor: the CLI will not put this account's name on a change of route
by itself, so a command typed outside a run names who makes it with `--actor human:<Name>`, and one an executor runs
is its attempt. The API takes the write from any caller, as before.

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

Five smaller rules, each of which somebody will meet:

- **A route is checked before its row is written.** A `route set` that could never activate is refused
  as a 400 naming the route, rather than written and then refusing its own activation.
- **A set replaces the route, whole.** `--binding` left out is *not* "keep the one that is there": the
  route it writes has no binding, and whatever account the old one selected is gone. The change is
  journaled with the old binding beside the new, and the answer shows the route that was written — so
  nothing is lost, but nothing warns you either.
- **An archived campaign is refused**, for `route set` and `route unset` alike, with the same
  `campaign_archived` it answers everywhere else: archived work never dispatches again, so it is never
  re-routed in either direction — including the direction that only takes something away.
- **`route unset` of a route that is not there succeeds and changes nothing** — it is the state the
  caller asked for, and nothing is activated because nothing changed.
- **An operation's own route and its channel routes are separate routes.** Removing the operation's own route leaves
  its channel routes, and removing a channel route leaves the operation's own route. A channel route with no route of
  the operation beside it is a normal state, not a half-removed one: that channel is routed, and the operation's other
  calls fall to the levels below. An older executable on the same database can change or remove an operation's own
  route while channel rows stay, because it never reads them as its own; the newer one reads the state as it stands.

**The read verbs still answer for an archived campaign**, because it can still be named — and what they
answer about is a campaign whose own routes have left the snapshot: `route resolve` falls back to the
global route, and `route list` shows no rows for it. Archiving is not itself an activation, so those
rows leave at the next one — a reload, or any `route set` — and until then both verbs still show the
route the campaign had.

### `route resolve` has three different answers

`route resolve` is the diagnostic verb: it asks the active snapshot — the same one the claim reads —
where this campaign's work would go and whether the plugin there could take it.

- **Nothing resolves** → `no_route`, a 404 (CLI exit 1). That is a configuration fact: no route sends
  this operation anywhere for this campaign.
- **A route resolves and cannot run** → a 200 with `"usable": false` and the problems listed.
- **A route whose binding no pattern could finish measuring** → `validation_timed_out`, a 503 with
  `retryable: true`: whether it can run is not known, and the claim would end such an item transient rather than
  refuse it, so the answer is to ask again rather than `usable: false`.

The problems in that second answer carry **attempt-error codes, not route-problem codes**, because
resolve is predicting what the claim would decide and therefore speaks the claim's vocabulary. §7 has
both lists.

**`usable: true` is about the route, not about an item.** Of the twenty-seven answers a claim can give (§7),
resolve answers the first two in its own words — an operation this build does not publish is a 400,
nothing resolving is the 404 above — and asks the five that need nothing but a route and a plugin set,
numbers 3 to 7. The last twenty are questions about a work item, which resolve does not have: whether a
contact is named, whether that person is reachable on the channel the operation consumes, whether that
value is suppressed, whether the routed plugin has pinned the person an operation that works by the pin
alone acts on, whether the send a read or a take-back names is this campaign's, whether it kept the provider's reference, whether it was
made under the route the item is routed to, the same three questions of the search a status read or a stop names, whether an audience named
by its number is one the campaign holds, whether the composed input satisfies the schema, whether the definition it carries satisfies the
schema the routed plugin declares for one, whether the routed plugin takes an argument
the operation names per channel on the channel the input names, whether it writes a template on that channel where the message is one, whether an earlier attempt that lost its answer may be repeated under the routed plugin, whether the campaign saw the thread the operation names at the plugin it is routed to, whether another search of the campaign has not ended, whether the operation needs a person's approval, and whether an open provider block holds it — which turns on the sending account the input names. So `route resolve --operation campaign.enroll` answers `usable: true` against a
plugin that implements it, and every item of that operation is still parked for a person at the
claim.

The most common real `usable: false` is a **default** route, and it is worth showing. A default route
is validated only against the operations its plugin already claims, so a global default to a provider
that implements only some of the published operations activates cleanly — and then answers this for
the rest:

```text
Operation:  campaign.enroll v1
Campaign:   cmp_01M2M7JBD1D9MEMMJQAJCWZV3H
Plugin:     other-provider 1.0.0 | valid
Scope:      global_default
Binding:    -
Snapshots:  routes rts_01M2M7JAP1E2T14EFE0XG7MGVA | plugins snp_01M2M7JAE3KB7Q2MXN266SDKQQ
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
a route to a plugin that declares none may carry any JSON object, up to 64 KiB. A route with no binding
is measured against the plugin's schema as the empty object, so a plugin requires a binding exactly when its schema
refuses `{}`. It is still a route of its own: it has no identity where `{}` has one, so trading one for the other is a change
of account in provenance, in event sources and in reads by the account a person was learned in. The
value reaches the plugin as `context.binding`, and no binding reaches it as `null`. **Nothing in the
runtime interprets what it means** — that is the plugin's business — though the runtime does hold it to
the plugin's schema, refuse a credential-shaped name in it, hash it into each attempt's provenance,
journal it and show it in `route list`.

**The 64 KiB is measured where the binding is written**, at activation, over the same canonical JSON the
binding's `sha256:` identity is taken from — so both write paths are covered, `route set` and the `Routes`
section, and a larger one is `route_binding_too_large`. It has to be that way round: a route is journaled
into an append-only table, listed by `route list` and answered `usable: true` by `route resolve` before
anything tries to carry it anywhere, so a binding first refused at the invocation would already be
permanent, and every item through that route would die with nothing naming the route that did it. The
invocation keeps its own cap — `plugin_binding_too_large`, permanent — as the protocol's guard on the
envelope it writes; nothing a route can carry should ever reach it.

**A binding selects an identity the plugin's own credential store already holds; it never carries the
credential.** A binding is journaled when it is written and shown in full by `route list`, so a field
named like a credential is refused at activation, at any depth:

```text
campaign:cmp_…/routes/default: route_binding_secret_like — a binding must not carry 'api_key'. A binding
selects an identity the plugin's own credential store already holds; it is journaled and listed, so it
never carries the credential itself.
```

The name is read with its separators ignored, so the spelling makes no difference: `x-api-key`, `API.KEY`
and `api key` are refused exactly as `api_key` is. The published list of fragments (`docs/plugins.md` §3)
is how the question is asked; "reads like a credential" is what it says.

A credential reaches a plugin the way every secret does: through a variable its manifest declares under
`capabilities.env`, granted by the user and read with `host.env`, or through what the person wrote under
`Plugins:Config:<id>`, read with `host.config` (`docs/plugins.md` §6).

The package under [`plugins/reply/`](../plugins/reply/README.md) is the whole handshake in one place. Its
manifest declares two optional fields, and a campaign somebody routed to it acts as the profile the
binding names — one of the profiles that package's own vendor CLI already holds a credential for:

```sh
jason route set --campaign cmp_… --plugin reply --binding '{"profile":"outbound"}' --actor human:you@example.com
```

Nothing here knows which provider `reply` is or what a `profile` selects. Both are what an operator
wrote: the id is the directory the package was copied into, and the field is one that package's manifest
declared.

## 6. Activation, and what refuses it

A reload builds the whole candidate route set — the global half from `settings.json`, the campaign half
from the rows of every campaign that is not archived, and of an archived one while a search of it is still followed
(below) — and checks every route in it against the plugin
set that load produced and against the operation catalog. Only if nothing objected are the two
registries swapped, plugins first and routes immediately after. The reload reads `settings.json`
itself before it builds anything, so a global route written straight before it is frozen by that
reload; a file that is there and cannot be read rejects the reload with `settings_file_unreadable`,
and one that is not there leaves the global routes in force, as it leaves the grants
([docs/plugins.md](plugins.md) §9).

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
| a global channel route | `Routes:Operations:<operation>:Channels:<channel>` |
| a campaign route | `campaign:<cmp_…>/routes/<operation>`, or `campaign:<cmp_…>/routes/default` |
| a campaign channel route | `campaign:<cmp_…>/routes/<operation>/<channel>` |

The load at startup takes the same path: a runtime started with a bad route reports it in the startup report and
leaves **both** registries empty, rather than running with plugins and no routes. A pattern that could not finish
is judged by where it happened. At a busy **start**, a binding probe that timed out twice, or a binding no pattern
could finish measuring (§7), is not yet known to be bad — the engine may simply have been cold — and nobody is
there to send a reload again: a load whose only problems are of these two kinds is built once more, whole, on the
now warm engine, and a package whose probe still cannot finish on that second load loads `unavailable` with
`probe_timed_out` (docs/plugins.md), while the rest of the load stands. Routes to such a plugin are frozen with
their bindings unmeasured: everything before the measurement is still checked — the plugin is known, performs the
operation, speaks its contract, and the binding names no credential and fits its bound — and every claim on them
ends `plugin_unavailable` at check 4, before a binding is measured at check 7, until the next load measures them. A
binding of an available plugin no pattern could finish measuring on the second load still leaves the registries
empty, reported under `pattern_timed_out` and repaired by `jason plugin reload`: that plugin passes check 4, so a
route activated with it unmeasured would have its binding measured again at every claim, each a double timeout,
until the item's attempts ran out — the start fails closed instead. At a **live reload** the engine is warm, so a
binding pattern whose probe times out twice is at fault: the package is refused, as any package problem is, and the
previous snapshot stays active.

**An archived campaign's routes are ignored**, but for its searches. History must never stand between an operator and
an uninstall: the campaign will never dispatch again, so what its route named no longer has to exist. Until each of its
searches has ended, though, the stop its archive made still runs, and a stop goes where the search ran: so while a
search of an archived campaign is still followed, the campaign's routes stay in the set, and each one that no longer
holds — its plugin uninstalled, say — is left out and refuses nothing. They are routes in force like any other until
then: `jason route list` and `route resolve` show them, the events reader reads the accounts they bind, a person's reads
on request may go through them, and the route counts count them; the claim still runs nothing in an archived campaign
but the stop. The next activation after its last search ends leaves them out again.

**A change that would leave a search without its account is refused.** A read or a stop of a search goes where the
search ran (§7, check 17), and a route is where a binding is kept. So when the routes in force name the plugin and the
account a search still followed ran under — running, held at the provider, or of an archived campaign whose stop has
not ended it yet — and the candidate set would name them nowhere, the set is refused with `route_orphans_search`, one
detail per search, named `campaign:<cmp_…>/searches/<wi_…>`, saying what to do: stop it with `audience.search_cancel`,
or let it end, then change the route. A paused campaign's searches are not read, so one ends only once the campaign is
started again; an archived campaign's search is ended only by a stop: the one its archive made, which is not made again
if it fails, so a person then makes one with `audience.search_cancel`, the one operation an archived campaign takes. A
read or a stop that finds no search under the reference for the account the binding signs in as (`search_not_found`:
deleted there, or that account changed) ends it `cancelled`, and it then holds no account. `route.set` and `route.unset` answer
`409 route_orphans_search`, `retryable: false`, and write nothing; a reload answers `409 plugin_reload_rejected` with that
detail and keeps the routes in force, as for any problem it cannot apply, and `plugin.list` says why under
`last_reload`. A change that leaves another of the search's campaign's routes, or a global one, naming the account goes
through — a route of another campaign does not count, since a search is routed only by its own campaign's routes and
the global ones — and so does any change once the search has ended. A search the provider gave no reference is read
and stopped nowhere (§7, check 16), so no route is kept for it. A search whose start is under way has no record yet
and is not counted: a change made while it starts can leave it without its account, and it is said as below.

**A change that would strand a send the provider still holds for later is refused.** A cancel and a status read of
a send go where the send ran (§7, check 14), by the same rule. A send holds its route while the provider holds it: a send
of a campaign that is not archived succeeded with the provider's reference, its own result says `cancelable: true` (read
as the send's operation document defines it, where an absent flag reads as false), and its moment, the `accepted_for` its
result names or else the `send_after` its arguments named, is known and after the runtime's clock.
When the routes in force name the plugin and the account such a send ran under, and the candidate set would name them
nowhere, the set is refused with `route_orphans_send`: one detail per campaign and account, named by the latest of its
sends, `campaign:<cmp_…>/sends/<wi_…>`, saying how many sends hold the route, their work items (the latest five, and how
many more), the latest moment among them in UTC rounded up to the second, and the ways out: cancel them with
`message.cancel`, or change the route after that moment. The answer's message counts the sends. It says nothing a send
says to a person: not who it is to, not its sender, subject or text. This holds for an
operation route, a channel route and a default alike, at the same three doors: `route.set` and `route.unset` answer
`409 route_orphans_send`, `retryable: false`, and write nothing, and a reload answers `409 plugin_reload_rejected` with
that detail and keeps the routes in force. A change that leaves another of the send's campaign's routes, or a global
one, naming the account goes through, and so does any change once its moment has passed: the guard lets go by itself,
with no cancel made. It lets go as soon as the provider has said it holds the send no longer, in one of these ways:

- a `message.cancel` of the send succeeded;
- a `message.cancel` of the send failed with an answer its document declares as nothing left to cancel,
  `send_not_pending` or `send_not_found`;
- a `message.status` of the send failed with the answer its document declares as no such send, `send_not_found`;
- a `message.status` of the send succeeded and says it is final (`sent`, `cancelled`, `failed` or `unknown`), whichever
  read said it;
- the latest `message.status` of the send that succeeded says it can no longer be cancelled (`cancelable: false`). An
  older read that said so frees nothing once a later one says it can be cancelled again.

A cancel or a status read still to run, and one that failed for any other reason (a provider out of reach, a blocked
account), leaves the send held. A send whose result does not say the provider can cancel it, `cancelable` false or
absent, is held for nothing a cancel could reach, so no route is kept for it. An estimate a provider gives of when it will
send is never read as a moment. Everything asked is the runtime's own record: the
send's arguments and result, the provenance of its succeeded attempt, and the campaign's cancels and status reads of it.
The moment is compared with the runtime's clock, in the database, so only the sends still held are read.

The guard has two limits, and within them a send can be left with no route to the account it ran under:

- **A send with no moment known is not guarded.** A send whose result names no `accepted_for` and whose arguments named
  no `send_after` holds nothing, though its result says it can be cancelled: a provider answers a send it will make as
  soon as it can the same way as one it keeps queued for a sending schedule of its own, and a hold for the first would
  outlast the send for as long as nobody read it. If its route has moved, cancel such a send at the provider.
- **A send on its way is not guarded.** A change made while a send is still on its way to the provider, before it has
  succeeded, finds nothing held, and the send may be left without its account once it succeeds.

Every door a change comes through refuses one that would strand a held send, so outside those two limits a send no route
reaches was left so by a start (which has no routes in force to keep), or by settings edited while the runtime was
stopped. A start says each such
send once in the log, as a warning naming its work item, its campaign and the plugin it ran at, and nothing it says to a
person; its cancel and its status read are refused at the claim (§7, check 14). After the start, while the routes in force name nothing,
the guard reads nothing: there is nothing it could refuse.

**The start is not refused, only reported.** A start has no routes in force to keep, and a start whose routes are
refused leaves no plugin active at all. So routes changed outside the three doors above — the settings edited while the
runtime was stopped — are activated as they are, and each search still followed that they leave with no route naming
its account is said in the log at warning, with its campaign and the way out: put a route naming that account back,
then stop the search or let it end. It is said again at each later activation, a `route.set`, a `route.unset` or a
reload, for as long as it lasts. Until then its reads and its stop are refused at the claim (§7, check 17). A search
that was already in that state before a `route.set`, `route.unset` or reload refuses nothing either: those refuse only a
change that would itself take the account away.

An activated load journals `plugins_reloaded` with the routes it froze beside the packages:
`routes: {snapshot_id, global_default, override_count, campaign_route_count}`. A route write journals
`routes_updated` against the campaign, keyed by the operation, by `default`, or by `<operation>/<channel>` for a
channel route — `message.send/linkedin` — with the old and new values. A line keyed by an operation and a channel is
about that channel's route only, never the operation's own.

## 7. Fail-closed: every reason, and what to change

Nothing falls back. An operation the routed plugin cannot perform ends the work item with the reason;
handing the work to whichever other plugin happens to implement it would mean a campaign acting against
an account nobody named.

### At the claim — the twenty-seven, in the order they are checked

Every one of these is decided **before a child process exists**, and the first that fails is the one the
attempt records. The attempt is kept either way, with the context it was claimed with and the provenance
as far as the decision got, so a manager can see what would have run. None of them is retried. One answer three of
them can give does come back, because it refuses nothing: a schema pattern that did not finish matching, twice —
rows 7, 19 and 20 below.

The twenty-fourth, `search_unfinished`, is not a refusal: a campaign runs one search at a time, so a search claimed
while another search of its campaign has not ended — one the provider still runs or holds, its row not ended, one
running now, or one open again after a start whose answer was lost, which may be running at the provider — **waits**
behind it, with no attempt and nobody asked: its last error names the search in the way, its
line names it under `behind_search`, and it is claimed again a search cadence (`Dispatcher:SearchCadenceSeconds`) later, or
`Dispatcher:SearchHeldCadenceSeconds` later where the provider holds the search in the way.
It is asked before the approval, so nobody is asked to approve a spend that cannot start now. Two campaigns searching
one definition are not this check's: that is the provider's own refusal, `search_in_progress`.

The twenty-fifth is not a refusal at all: it is the gate the runtime owes a person, and an item that reaches
it is **parked** rather than failed — no attempt, nothing spent, and `approval.list` is where it waits. Where a
person decided in advance — a standing approval for the campaign and the category of the verdict a drafted reply
answers — and nothing has been decided about the item yet, it is not parked: it runs in the same claim, under an
approved row that names that person.
It is asked after every refusal on purpose. What a person is asked to approve is the composed input, so it has to be
composed and to satisfy the operation's own schema before anybody can be shown it; and nobody is ever
asked to approve reaching somebody the suppression register already protects, or acting on somebody the
routed plugin does not know, or sending at a moment the routed plugin would not keep, or a template it does not write.

The twenty-sixth, `provider_blocked`, is not a refusal either, and it is the one answer that says nothing about the
work: the routed plugin's provider has said it refuses such work on this account, or on one sending account under
it, for now. It holds the work whose contract says `dispatch.under_provider_block: waits`: every operation of
`reach: act`, which reaches a person, and two controls that must each happen once, `campaign.pause` and `optout.record`.
Every other `control` operation — `message.cancel` among them, whose send's moment passes while it would wait — and
every read says `runs`, goes through it and succeeds or fails on its own, because a control operation may still
succeed and a read tells the truth. An item it holds **waits** on the
block, with no attempt, until the block clears or a trial gets past it. It is asked after the approval, so a person
is asked about work while the provider refuses it, and work a person releases still waits for the block.

A row's number is its name and stays the same from release to release, and the order the checks are asked in is the
order of this table: so row 27 stands between rows 23 and 24, so that nobody is asked to approve, and no block holds, a
reply that could only be refused.

| # | Code | Class | What it means, and what to change |
|---|---|---|---|
| 1 | `operation_unknown` | permanent | this build publishes no contract for the operation the item names. The item was written by a build that did; nothing but a different build will run it |
| 2 | `no_route` | permanent | nothing sends this operation anywhere for this campaign. Write a route — `jason route set`, or the `Routes` section plus a reload |
| 3 | `plugin_not_loaded` | permanent | the route names a plugin the active set does not hold. Install the package and reload, or re-point the route |
| 4 | `plugin_unavailable` | permanent | the package is installed and listed, but its environment holds it back — usually a program it declares is not on the machine. Repair what `jason plugin list --human` names, then reload |
| 5 | `plugin_operation_unsupported` | permanent | the plugin does not perform this operation, or is not a kind of plugin that performs any. Route this operation to a plugin that claims it |
| 6 | `contract_incompatible` | permanent | the plugin speaks no version of this operation's contract that this runtime publishes. Upgrade the plugin |
| 7 | `binding_invalid` | permanent | the route's binding does not satisfy the schema the plugin declares — including a route with no binding to a plugin that requires one, which is a plugin whose schema refuses `{}`. Fix the binding. A pattern of that schema that did not finish matching, twice, is not `binding_invalid`: nothing was found wrong, so the attempt ends `validation_timed_out`, `transient`, and comes back |
| 8 | `contact_required` | permanent | the operation acts on somebody and the work item names nobody. Create the item with `--contact`. An operation whose contact is `optional` runs with one or without, so it never meets this check |
| 9 | `no_channel_value` | permanent | the person is not reachable on the channel the operation consumes. Add the channel to the contact, or name a channel they have |
| 10 | `suppressed` | permanent | that channel value is on the do-not-contact register. It is not reached, and that is the register working. An operation that only takes a person out, or takes back a send to them, declares `preflight.suppression: not_applicable` — today `optout.record`, `enrollment.stop` and `message.cancel`, and `audience.search_cancel`, which stops a search and reaches nobody — and meets neither the check nor the read: a suppressed person is exactly who it is for |
| 11 | `contact_not_linked` | permanent | the operation works by the routed plugin's pin alone — `preflight.provider_contact: pinned_only`, today `contact.delete` and `conversation.list` about a person — and that plugin has pinned nobody for the person. A pin held only by another plugin names nobody here, and nobody is looked up by an address to find out. A `find_or_create` or `find_only` operation of the routed plugin links the person, or route the operation to the plugin whose pin they carry; then create a new item (while the item carries an unsettled send, only once the provider has said the earlier attempt did not act) |
| 12 | `send_unknown` | permanent | the operation reads or takes back one send — its input schema names `send`, today `message.status` and `message.cancel` — and the work item `args.send` names is not one of this campaign's, or is not an operation that makes a send: `message.send` or `social_invitation.send`. Name the work item that made the send, in this campaign; then create a new item. An operation that names no send never meets this check, nor the two after it |
| 13 | `send_reference_absent` | permanent | the send named has no reference to act by: it has not succeeded, or the provider gave it none. The runtime composes `send.reference` from the send's own attempt and from nothing a caller writes, so there is nothing to read or take back yet. Wait for the send to succeed, or create a new item for another one |
| 14 | `send_route_mismatch` | permanent | no route active now names the plugin and the binding the send was made under. A cancel or a status read of a send is routed where the send ran, whatever its own operation resolves to — the rule a search has (row 17): the operation's own route where it names that plugin and binding, then the campaign's route for the send's own operation by the channel the send was routed by, then any of the campaign's routes and the global ones, channel routes included, in the order of precedence — so a campaign that sends email through one plugin and LinkedIn messages through another cancels and reads each send where it went. A route change that would take the last route naming that account away from a send still held for a later moment is refused at activation, `route_orphans_send` (§6). A reference names a send only at the provider and account that took it. Plugin and binding are compared by the plugin's id and the binding's identity, never the package's digest, so an upgraded package still reads its own sends. Route an operation to that plugin and account again, then create a new item |
| 15 | `search_unknown` | permanent | the operation reads or stops one search — its input schema names `search`, today `audience.search_status` and `audience.search_cancel` — and the work item `args.search` names is not one of this campaign's, or is not an `audience.search`. Name the work item that made the search, in this campaign; then create a new item. An operation that names no search never meets this check, nor the two after it |
| 16 | `search_reference_absent` | permanent | the search named has no reference to act by: it has not succeeded, or it answered `finished` inside its own call — a provider that searches synchronously answers so, with every person found, and its search is never read or stopped — or the provider gave it none. The runtime composes `search.reference` from the search's own attempt and from nothing a caller writes, so there is nothing to read or to stop |
| 17 | `search_route_mismatch` | permanent | no route active now names the plugin and the binding the search was made under. A read or a stop of a search is routed where the search ran, whatever its own operation resolves to: the operation's own route where it names that plugin and binding, then the campaign's route for `audience.search`, then any of the campaign's routes and the global ones, in the order of precedence, the binding found by its identity — so an override of the read alone, or a default moved to another account while another route still names the first, leaves it readable and stoppable at the account that is spending. A route is where a binding is kept, so a route change that would take the last route naming that account away from a search still followed is refused at activation, `route_orphans_search` (§6). A search reaches this check only where its account's last route went outside those doors — the settings edited while the runtime was stopped, then a start, which says the search in its log — and then its reads and its stop are refused here, and the search goes on at the provider unread until a route names that account again. A reference names a search only at the provider and account that started it; compared by the plugin's id and the binding's identity, never the package's digest. Route an operation to that plugin and account again, then create a new item |
| 18 | `audience_unknown` | permanent | the operation carries an audience's definition — today `audience.search` and `audience.preview` — and the work item names an audience by its number under `args.audience` that the campaign does not hold: a campaign numbers its audiences from 1, in the order the claim made them ([campaign manager](campaign-manager.md) §10). Name an audience the campaign holds, or the audience by its definition, then create a new item. An item that names its audience by its definition never meets this check; one that names an audience the campaign holds is composed with the definition that audience was made from, which row 20 holds to the routed plugin's schema as it holds any |
| 19 | `input_invalid` | validation | the composed input does not satisfy the operation's own schema, or an argument contradicts an identifier Jason has already pinned. The details carry a JSON pointer each. A pattern of that schema that did not finish matching, twice, is not `input_invalid`: nothing was found wrong and nothing has run, so the attempt ends `validation_timed_out`, `transient`, and comes back under the ordinary retry rule |
| 20 | `definition_invalid` | validation | the composed `definition` does not satisfy the schema the routed plugin declares for one under `audience_definition` (`docs/plugins.md` §3). The envelope around it is the operation's, held to its schema at `workitem.create` and at row 19; the definition is the provider's, so it is held to the routed plugin's schema here, where the route is resolved, and never at creation. The details carry a JSON pointer each, under `/definition`; `jason operation describe audience.search --campaign <id>` prints the schema. A pattern of that schema that did not finish matching, twice, is `validation_timed_out`, `transient`, as at rows 7 and 19. A search the claim lets through past this row, to run or to wait for a person, runs under one of the campaign's audiences from then on: the claim finds the audience its definition already is, or makes the next one, and records it on the item ([campaign manager](campaign-manager.md) §10); a search refused at any row makes none |
| 21 | `argument_unsupported` | permanent | the input carries an argument the operation's document names per channel — its `channel_arguments`, today `send_after` on `message.send` and `social_invitation.send` — on a channel the routed plugin's manifest does not list it for under `arguments` (`docs/plugins.md` §3). Leave the argument out and give the item `not_before` at the moment instead, or route the operation to a plugin that takes it on that channel; then create a new item (while the item carries an unsettled send, only once the provider has said the earlier attempt did not act). An item that does not carry the argument never meets this check |
| 22 | `template_not_offered` | permanent | the message is a template — `compose: template` — on a channel the operation's document names under `channel_templates`, today `email` and `linkedin` on `message.send`, and the routed plugin's manifest does not list that channel for the operation under `templates` (`docs/plugins.md` §3). A manifest without the key writes a template nowhere. Write the message as final text, or route the operation to a plugin that writes a template on that channel; then create a new item (while the item carries an unsettled send, only once the provider has said the earlier attempt did not act). A final message never meets this check |
| 23 | `provider_answer_lost` | ambiguous | the item carries an unsettled send — an earlier attempt of it started and ended ambiguous, whatever ended after it — so it may have reached the person, and the operation's document settles a lost answer by the work item's key — `recovery_read.by_key`, today `message.send` and `social_invitation.send` — while the routed plugin's manifest does not list the operation under `settles_by_key` (`docs/plugins.md` §3), or the routed package, by its id and digest, or its binding is not the one the latest such attempt's provenance names, or that provenance names none, or the item's arguments are not the ones that attempt's snapshot kept, or the input composed now is not the one that attempt was composed with — compared by that provenance's `input_digest`, the canonical `sha256:` (keys in ordinal order, so member order alone is no difference) of everything in the composed input but the campaign's status, and so neither the binding, compared by its identity, nor the invocation's context: the arguments, the send, the person with the projected fields, the consumed channel's value and the pins, the campaign with its name and pins, and the work item's key. A pin the compared attempt and every later attempt of the item recorded, from the answers they ended with, is left out of the input composed now before it is named, since the input that attempt was composed with could not carry it and every later attempt ran only because its claim found it the same request; a pin an earlier attempt, another item or another plugin recorded is not left out. A digest names the input and carries none of it, no address included, and an attempt claimed before attempts named their input carries none and is compared on its arguments alone. A route, a binding or a package changed in between makes the ending final, since the key settles a send only at the provider and the account that took it, and so does an item edited since, a person whose address, field or pin changed since, or a campaign renamed or pinned since, since the key settles only the request it went with and the provider answers a repeat with the earlier send; a campaign paused and resumed in between asks nothing else of the provider. Nothing says the repeat would be settled by the key, so it is not made: the item is as unsettled as the attempt that may have acted, and it is final. Find out from the provider whether the first attempt acted, and create a new item only if it did not. An item no earlier attempt left ambiguous never meets this check. **The same hold covers every operation whose `cost` is `metered` and whose `repeat_after_ambiguous` is `after_recovery_read`** — today `audience.search`, `campaign.enroll` and `campaign.activate` beside the two sends — but for the manifest's `settles_by_key` and the package's digest: its blind repeat would spend again, and its recovery read finds what the earlier attempt may have started only at the provider and account that took it, for the request it went with, whichever version of the package started it (`docs/plugins.md` §7). So a route moved to another plugin or account, or a count or a definition edited while the item waited, makes the ending final, rather than start a second search that nothing could find, read or stop; a package updated in between, under the same id, does not |
| 27 | `thread_route_mismatch` | permanent | the operation names a thread — its input schema names `thread`, today `conversation.get`, `conversation.reply`, `conversation.classify`, `conversation.close` and `conversation.reopen` — and the campaign's own `inbound_reply_observed` lines name that thread, but none of them at the plugin the item is routed to. A thread is held only by the provider it was seen at. **The check compares the plugin, never the account.** An account is known only by the identity of the whole binding, which carries more than the account — a behaviour flag beside the profile, say — so a route edited in such a field would make every thread the campaign knows one seen elsewhere, and refuse every reply to an existing conversation until that thread moved again; another account of the same plugin is the provider's to refuse, as a thread it does not hold, which acts on nobody. A read or a filing of a thread is routed where the campaign saw it, whatever its own operation resolves to — the rule a send's cancel has (row 14): a route that names a plugin and account the lines name, newest first, then a route that names the plugin under any binding, each asked of the operation's own route, then the campaign's route for `conversation.list`, then any of the campaign's routes and the global ones, channel routes included, in the order of precedence — so it is refused here only where no route names the plugin any more. **An id seen at more than one plugin** names neither thread by itself: a read or a filing goes where the operation's own route says, never to the newest sighting, and is refused here, saying the id is held at more than one plugin, where that route names none of them. A reply is routed by its channel, as any `conversation.reply` is, and refused here where that channel's route names another plugin than the thread was seen at, before a person is asked to approve it. A line names its plugin; its account is the binding the read that found the thread ran under — the inbound read's attempt, or the events source the stored event was read from, a source with no binding naming a route with none — and a line whose read left no record of an account is a sighting of its plugin all the same. A thread no line of the campaign names, another campaign's included, never meets this check. Route the operation, or its channel, to the plugin that saw the thread, then create a new item |
| 24 | `search_unfinished` | — | **not a failure**: the operation is `audience.search`, and another search of the campaign has not ended — a search whose row is not ended, running or held at the provider, another `audience.search` item of the campaign running now, or one open again with no row after an attempt of it started and ended ambiguous, whose start may have acted although its answer was lost, so that it may be running at the provider. A campaign runs one search at a time: a second one would spend beside the first, and the provider refuses only a second search of the same definition. The item waits as `waiting`, with no attempt, its last error naming the search in the way, and is claimed again a search cadence later, the held cadence where the provider holds that search; nobody is asked to approve it meanwhile. Of two searches whose starts both lost their answers, the older goes first and the newer waits behind it. A search item that has ended — failed, cancelled or expired — is in nobody's way, even one whose start may have acted: what it may have started is a person's to find at the provider (row 23). Let that search end, or stop it with `audience.search_cancel`; one whose answer was lost settles at its own next attempt, which reads the provider's searches first and starts none of its own if one was started |
| 25 | `approval_required` | — | **not a failure**: the operation needs a person's approval, and a dispatcher may not stand in for the person who approves it. The item is parked as `awaiting_approval` with a record of exactly what is being approved, unless a standing approval covers it: then it runs in the same claim, under a row the person who set the standing approval decided. `docs/work-execution.md` §Approvals |
| 26 | `provider_blocked` | — | **not a failure**: an open block of the routed plugin and binding holds the work — the account's, which covers every sending account under it, or the block of the sending account the input names at `/args/sender/external_id`. Only the operations whose contract says `dispatch.under_provider_block: waits` are held: every `reach: act` operation, `campaign.pause` and `optout.record`. The item waits as `waiting`, its `wait.blocked_by` naming the block, with no attempt; one item at a time is the block's trial and runs, an act wherever one can run, and what an act answers decides the block; a control's answer decides nothing but `blocked`. Nothing is to change on the item: what clears a block is on the provider's side. `docs/work-execution.md` §Provider blocks |

Two of these are worth a second sentence. `input_invalid` and `definition_invalid` are the only ones classed
`validation` — they are the ones a planner can fix by editing the item; `provider_answer_lost` is the only one classed `ambiguous`, because
it keeps what the attempt before it left unknown; everything else is structural. Structural, that is, while nothing
before it may have reached the person: while the item carries an unsettled attempt of an operation row 23 holds — one the
work item's key settles, or a metered one its recovery read settles — every refusal from check 2 to check 22 keeps its
code, and so does check 27, and is recorded `ambiguous` and final, its message saying that an earlier attempt of the item may have reached the
person, or may have acted and be spending. A route moved since the lost answer to a plugin
without the operation is `plugin_operation_unsupported` so. Whatever a row above says to change, new work for that
send is created only once the provider has said the earlier attempt did not act. And an argument that
disagrees with a pin is refused rather than resolved either way: the rule is to work by the pin, so the
message names the pinned value.

### At activation — eleven route problems, plus one for the section, one for the searches and one for the sends

| Code | When |
|---|---|
| `route_plugin_unknown` | the route names a plugin the candidate set does not contain |
| `route_plugin_kind_not_invocable` | the plugin is a notification plugin, which performs no canonical operation; it is reached through the `Notifications` settings instead |
| `route_operation_unknown` | a *campaign* route names an operation this build does not publish. A global override keyed by such a name never reaches this check: the section validator refuses it first, as `routes_settings_invalid` below |
| `route_channel_not_routable` | the route names a channel under an operation whose document names no argument a route reads its channel from — or under no operation: a campaign default, or `Channels` under `Routes:Default` or at the top of `Routes`, which is refused at the reload whatever plugin it names. Asked only of a route that names a channel |
| `route_channel_unknown` | the route names a channel the argument the operation's document names for routes does not allow |
| `route_operation_unsupported` | the plugin does not list the operation the route sends it |
| `route_contract_incompatible` | the plugin speaks no version of that operation's contract |
| `route_channel_plugin_reads_no_events` | the route names a channel, and the plugin it names reads no events: its manifest declares no `contracts.events`. Every operation that takes channel routes is about what is sent on the channel — `message.send` and `conversation.reply` send it, `sender.list` names the accounts that will — and a reply to what is sent on a channel route reaches the campaign only through the events of the account it went from, so it would go unseen. Asked of a channel route only, at `route.set` and at every reload, so a reload whose new package stops reading events while a channel route names it is refused whole, and the previous snapshot stays active. A restart in that state starts with no plugins and no routes (§6), since the package is checked against the route at the start as at any reload |
| `route_binding_secret_like` | the binding carries a field named like a credential, at any depth |
| `route_binding_too_large` | the binding's canonical JSON is larger than the 64 KiB the protocol carries |
| `route_binding_invalid` | the binding does not satisfy the schema the plugin declares — including a route with no binding to a plugin whose schema refuses `{}` |

The twelfth is not a problem with a route but with the **section**: `routes_settings_invalid` is the
`Routes` section itself failing the validator that reads it, which happens when the file is edited into
something no route could be built from. The options system reports that as prose rather than as
structure, so the runtime carries the validator's own sentence through verbatim and names the setting it
came from. It arrives as the same rejected reload, because an operator who mistypes a route must get one
answer — and never a 500, which would say the runtime broke rather than the edit.

The thirteenth is not a problem with one route either, but with the whole set: `route_orphans_search`, a set that would take
the last route naming the plugin and account a search still followed ran under away from it (§6). It is asked only
when no route in the set is wrong, and `route.set` and `route.unset` answer it under its own code rather than as a
rejected reload.

The fourteenth is its twin for a send: `route_orphans_send`, a set that would take the last route naming the plugin and
account a send ran under away from it while the provider still holds it for a moment to come and its result says it
can be cancelled, until the provider says it holds the send no longer (§6). It is asked when the searches are, and `route.set` and `route.unset` answer it under its own code
when it is all that refused.

**A binding no pattern could finish measuring is none of these.** Where a pattern of the plugin's binding schema did not
finish matching, twice, nothing was found wrong with the binding: it is not `route_binding_invalid`. Alone, it leaves
nothing written or activated, and `route.set`, `route.unset` and `plugin.reload` answer `validation_timed_out`, 503,
`retryable: true` — send the request again; the detail names the route the way the operator wrote it, under the
dialect's own code, `pattern_timed_out`, rather than a route problem. A route to a plugin that loaded `unavailable` with
`probe_timed_out` (§6) is the one exception: its binding is never measured, so a route write naming that plugin, or one
made while such a route is active, is accepted rather than answered 503. Beside a route that really is wrong, it is left
out of the 409, which names only what is wrong. The start builds such a set once more instead (§6). Usually it is a busy
machine's cold engine, but not always: a plugin's binding patterns are probed on a warm engine when its package loads —
a pattern that cannot finish there has already refused its package, reported as `field_invalid` at
`binding#/properties/<name>/pattern` with a message naming `pattern_timed_out` — and its probes are a short string and
the same string repeated to 64 KiB, the longest binding a route can carry, so a pattern slow only on a long value is
refused at load; a value of other characters can still cost a pattern more than either probe did, and that times out
here. Either way the answer is the same: never a refusal of the binding.

**Two facts about the order that would otherwise surprise you:**

- **A notification plugin is reported as not invocable, not as not supporting the operation.** Such a
  plugin lists no operation at all, so asking "does it implement this one?" first would answer with what
  is missing rather than with what it is; it is reached through the `Notifications` settings instead.
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

**A third is asked twice, at neither of those two places.** How large a binding is is refused at
activation, as `route_binding_too_large`, and again by the invocation, as `plugin_binding_too_large` —
one number, stated in each side's own vocabulary. The claim in between does not ask it at all, because by
then it can only be answered one way: a binding over the cap never became a route.

## 8. What "ambiguous" costs, in operator terms

A failed provider attempt carries one of five classes, and the class decides what happens next:

- **`transient`** — nothing happened; the item comes back and is attempted again, within its own
  `max_attempts`.
- **`permanent`** and **`validation`** — it would fail the same way again; the item is finished.
- **`blocked`** — the provider refused the account or a sender, not this item, and did nothing; the item is
  finished, and the [block](contracts/block/provider_block.md) it carries says what a person clears.
- **`ambiguous`** — *the provider may already have acted*, and the answer never arrived.

For an ambiguous end the runtime reads the operation's own contract rather than guessing. `campaign.get`
says a repeat is `safe`, because asking for a campaign a second time costs nothing but a round trip. `list_membership.add` says
`after_recovery_read`, so a repeat is allowed only because the contract obliges the plugin to read the
ledger under the idempotency key, and the membership it writes, before writing anything — which is what
makes one crash cost one effect rather than one bill. `conversation.reply` says `never`, `social_invitation.send`
says `never`, and `message.send` says `never` too: no read tells this work item's lost reply, invitation or message
from another one sent to the same person, so the item ends for a person rather than risk a second message in somebody's
inbox — the same end a lease that ran out or a restart gives it. Every read says `safe`, and so do the
writes that set a state outright, pausing a campaign and filing, closing or reopening a thread.

Every end where nobody answered is ambiguous: the invoker's timeout, a lease that ran out, a missed
heartbeat, a command that could not be started, a command that was stopped. A missing answer says nothing
about whether a provider acted, and the honest class is the one that says so. An attempt a restart finds still `scheduled` is not such an end — the handler commits the
attempt started before it launches anything, so that one never ran and goes back uncounted.

**`result_invalid` is never repeated.** An answer that arrived and does not satisfy the operation's output
schema is a defect in the plugin: the next attempt would run the same code over the same answer and could
only spend budget while somebody waits. It is recorded as ambiguous — the call did reach the provider —
with `retriable: false`, whatever the operation's repeat rule says. The failing pointers are on the error,
and the answer that was refused is kept on the attempt's provenance so its author can see what was sent.
A failure is held to the operation's declared codes the same way: a code the contract does not declare, or a declared
code under another class, is `result_invalid` with the pointer at `/error/code` or `/error/class`, its block is not
kept and nothing it carries is pinned, and what the plugin said is kept on the attempt's trace. The plugin host's own
codes pass under the class the host writes them with, everywhere but on an events read.
An answer a pattern of that schema could not finish measuring, twice, is not `result_invalid`: nothing is known to
be wrong with it, so it is a lost answer — `validation_timed_out`, ambiguous — and the operation's own rule above
decides whether it is repeated. The unmeasured answer is kept on the attempt's trace.

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
Plugin:       fake-provider 1.0.0 | 9a357c7bfe4e
Operation:    list_membership.add v1
Route:        global_default | binding a728b92b6cdc
Snapshots:    plugins snp_01M2M7H79JANSWPE9EFGTGG7DH | routes rts_01M2M7H79NXYND9TAPQFDEQTHY
Invocation:   pin_01M2M7H97ED18X6V82B19FA85A | 478 ms | exec 2 | http 0 | log 0 | contact created
```

Read it as: which package and at which content digest, which operation at which contract version, which
level of routing chose it and **which account by identity**, the two snapshots the decision was made
against, and what the run cost. `jason workitem get <id>` has all of it as JSON, including the
identifiers the plugin returned, `external_id_origins` — whether the provider found or created what they name, where
the plugin said — and, after a `result_invalid`, the answer that was refused. The invocation line ends with the
origins, as `contact created`.

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
  and by nothing else: the identifiers an operation declares, and the people a page of a search lands, each pinned
  under `contact` by the attempt whose answer carried the page ([campaign-manager](campaign-manager.md) §10). An `external_ids` field posted
  to `contact.update` is ignored; no caller can write a pin. An answer whose validation timed out pins nothing: a pin is
  never overwritten, so it is written only from an answer that was checked, and that one never was. Its result stays on the attempt's trace and the
  identifiers it returned in the attempt's provenance, as `external_ids_returned`.
- **A pin is never overwritten.** The same value again changes nothing. A *different* value is recorded
  beside the pin as a **divergence**, with the attempt that returned it — the original stands, both values
  are visible, and later attempts keep receiving the value Jason recorded rather than the one it disputes.
- The attempt **still succeeds** when a pin diverges. The plugin's answer is the plugin's; the pin is
  Jason's record. Failing the attempt would throw away work that really happened at the provider, and
  silently overwriting would destroy the only evidence that two records disagree.
- A plugin only ever sees **its own** pins. What another provider calls the same person is never its
  business.
- The journal carries three events: `external_id_pinned`, `external_id_diverged` and `external_id_origin`, keyed
  `contact/<plugin>/<kind>` or `campaign/<plugin>/<kind>`. An `external_id_origin` line follows a pin or a divergence
  in the same change set, under the same key, work item and attempt, by the same actor, and its `new` is `"found"` or
  `"created"`, nothing else: whether the provider held the entity before the call or the call created it — after a
  divergence, the one the attempt returned. It is written only where the plugin said which; a plugin that says nothing
  leaves no line, which means unknown, and an unchanged pin writes none. The word is about that attempt's own call: an
  attempt after a lost answer may find what an earlier attempt of the same item created, and its line then says
  `found`.

**Resolving a divergence is deliberately not implemented.** Deciding which record is right is
reconciliation — matching a person again, against two providers' answers — and this version has none
(DEF-RECON-001). A human sees both values and decides.

### A read on request

Two operations are read when a person asks, outside any work item: `contact.threads` (the provider's
`conversation.list` for one person) and `contact.thread` (its `conversation.get` for one thread). Such a read takes
only the route's half of the claim's decision: the plugin is loaded and available, it performs the operation, and the
route's binding passes its checks. It skips the claim's other half, and with it the attempt, provenance, the journal,
pins, approval and retry: nothing is recorded about the read, no identifier is pinned from what comes back, and a
failure is the request's answer rather than a state to repeat. The account is the one the person's identifier was
learned in: the route used is a current route to the pin's plugin whose binding has the identity recorded in the
provenance of the attempt that learned the pin, so one account's identifier is never sent to another. A person with no
such route is refused with `no_route`; a person whose matching route fails a plugin check is refused with that check's
own code, as a conflict (409).

These two are a person's reads on request, `jason contact threads` and `jason contact thread` from the command line, and
another operation joins the reads on request only by being named here. A read's route is each campaign's for the operation, then the
global one, resolved as §2 resolves any, and held to the checks of §7 that are about the route, the plugin and its
binding; a person known to several providers names the plugin to read from. The input is composed as a work item's
would be, from the person's own identifiers at that plugin, and checked against the operation's schema; the answer is
checked against the document too, and one that does not satisfy it is `result_invalid`. A failure the operation
declares comes back with its own code and class, retryable only when it is `transient`, and a thread that is not this
person's is refused as `thread_not_this_contact`.

Three more are a campaign's reads, made when a person or an agent asks about the campaign rather than about a person:
`audience.preview` (how many people an audience holds, and a sample of them without contact details),
`audience.filter_values` (the values one of the provider's filters takes) and `audience.availability` (whether the
account may search now, and what it has left) — `jason audience preview`, `jason audience filter-values` and
`jason audience availability`. **A campaign's read is routed by the campaign's route for `audience.search`**, the search
it serves, whatever the read's own operation resolves to: a definition is previewed, a filter's values offered and the
account asked at the provider and account that would run the search, never one provider's audience previewed and another
one's search approved. It is resolved as the claim resolves it (§2) and held to the same route's half of §7 for the read's
own operation: nothing resolving is `no_route` (404), a route that fails a
plugin check is refused with that check's own code as a conflict (409), and a binding no pattern could finish measuring is
`validation_timed_out` (503), to ask again. The input is composed as a work item's would be — the arguments, the campaign
with the routed plugin's own pins, and for the preview the root `definition` — and names nobody. The preview's definition
is held to the schema the routed plugin declares under `audience_definition` before anything runs, and refused
`definition_invalid` (400, a pointer each under `/definition`), the claim's own code. An audience named by its number is
read from the campaign's audiences and previewed by the definition it was made from, held to the same schema, and a
number the campaign holds none under is `audience_unknown` (404), as at the claim. The answer is held to the document as
a person's read is, and nothing is recorded, pinned or journaled: a preview makes no audience.

The same route answers what an operation takes for a campaign: `operation.describe` (`jason operation describe
<operation> --campaign <id>`) resolves the campaign's route for the operation now — for a campaign's read on request,
its route for `audience.search`, where the read goes — holds it to the same route's half, and
answers the operation's argument schema and, for an operation carrying a definition, the routed plugin's
`audience_definition` as declared — its schema and its keys — so a definition is written against the schema the claim will
hold it to. It invokes nothing.

## 12. Known limitations of this version

- **No operation-family wildcards.** A route names one operation, or one channel of an operation that takes channel
  routes, or is the default; there is no `list_membership.*` (DEF-ROUTE-001).
- **No named, reusable bindings.** Every route carries its own binding object, so two campaigns working the
  same account repeat it (DEF-ROUTE-001).
- **No repair path for a pin.** A pin cannot be corrected, moved or deleted through the API, and a
  divergence stays visible until reconciliation exists (DEF-RECON-001).
- **A retry is claimed against whatever is active then.** A running attempt keeps the package and route it
  was claimed with — that is what invocation pinning is for — but an item that comes back after a
  retriable failure is claimed afresh, so a later attempt may run against a different package or a
  different account than the one before it, and nothing warns about that. What *should* happen to queued
  work across a reload is open (DEF-ROUTE-002).
- **One decision about one work item, or one word in advance about one kind of reply.** An approval names a subject
  and nothing wider. A standing approval is a person's word for one campaign and one category of verdict, asked only
  where nothing has been decided about the work yet, and it covers only the draft the verdict's own work made
  ([docs/work-execution.md](work-execution.md), "Standing approvals"). There are no bulk decisions over a population and
  no anomaly stop rules (DEF-APPROVAL-001). What waits is notified as a pointer
  ([docs/notifications.md](notifications.md)), and `jason approval list` is where a person finds it and decides.
- **Thirty operations, not a catalog.** The documents under `docs/contracts/operations/` — eighteen that write,
  `campaign.create`, `list_membership.add`, `campaign.enroll`, `campaign.pause`, `campaign.activate`, `optout.record`,
  `enrollment.stop`, `conversation.reply`, `conversation.classify`, `conversation.close`, `conversation.reopen`,
  `contact.update`, `contact.delete`, `social_invitation.send`, `message.send`, `message.cancel`, `audience.search` and
  `audience.search_cancel`, and twelve reads —
  are the whole published set; an operation outside it cannot be routed, created or named, and adding one is a
  deliberate act with its own document, fixtures and version (DEF-OPS-001).
- **No compatibility window and no deprecation policy.** Every published operation is at version 1, and a
  plugin declares which contract family versions it implements. What happens when version 2 arrives — how
  long version 1 keeps being served, and how a plugin is told — is not settled and is deliberately not
  implied anywhere (DEF-OPS-002).
- **One installed version per plugin id**, because the id is the directory name. Two plugins may implement
  the same operation, and routing is how you choose between them.
- **A read on request has no bound of its own.** It runs outside the handler pool, one child each;
  `Dispatcher:MaxParallel` bounds work items only.
- **Routes are not exported or imported.** The global half is a settings file you can copy; the campaign
  half is database rows, readable through `route list` and writable only one route at a time.

The design these follow from — routing and binding as independent concepts, fail-closed with no implicit
fallback, and the registers the limitations above name — is in
[docs/architecture.md](architecture.md), §13.
