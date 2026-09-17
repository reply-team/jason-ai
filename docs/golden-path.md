# The golden path

One run of the whole product, from an installation that knows nothing to a provider operation that has been
approved by a person, performed by a plugin, and recorded where you can read it afterwards. Every command below
is a command the end-to-end tests execute, and the three guarantees at the end are the three tests that prove
them.

The provider here is [Reply](https://reply.io) through [`plugins/reply/`](../plugins/reply/README.md), because
that is the plugin this repository ships. Nothing in the runtime knows about it: a different provider is a
different package and a different route, and the sequence is the same.

## What you need

- **The `jason` executable.** Build it from this repository (`dotnet publish runtime/src/Jason.App -c Release -r
  <rid> --self-contained -p:PublishSingleFile=true`) and put it somewhere on your `PATH`. Everything below then
  reads as written. *(How a binary reaches your `PATH` is your platform's business, and the tests do not do it:
  they run the same program as `dotnet jason.dll`, which is the repository's own development command.)*
- **The Reply CLI**, installed and signed in, if you are following this against a real account. It owns the
  credential; Jason never sees one. *(The tests run against a stand-in that answers like it, so nothing in CI
  touches a real account.)*

## 1. Start the runtime

```
jason runtime start
jason runtime status
```

The runtime is a local background process that owns the database, the schedule and the dispatcher. `status`
answers with the instance it started and the endpoint it published.

## 2. See that it knows no provider

```
jason plugin list
jason route list
```

Both are empty on a fresh installation, and that is deliberate: **a standalone install never implies a provider.**
Until you install a package and write a route, provider work fails with `no_route` rather than going somewhere
you did not choose.

## 3. Install the package

Installing a plugin is copying its directory into `~/.jason/plugins/<id>/`. There is no install verb:

```
cp -r plugins/reply ~/.jason/plugins/reply
```

Nothing is loaded yet. A package on disk is a candidate; what makes it active is the reload in step 5.

## 4. Grant it what it asks for, and write the route

The package declares that it needs one program — the vendor's CLI — and a route says which of your accounts its
work goes through. Both live in `~/.jason/config/settings.json`:

```json
{
  "Plugins": {
    "Grants": {
      "reply": { "Exec": ["reply"] }
    }
  },
  "Routes": {
    "Default": {
      "Plugin": "reply",
      "Binding": { "profile": "default" }
    }
  }
}
```

`Grants` is the answer to what the manifest asked for: declaration is not permission. `Binding` names the account
by the CLI profile that holds its credential — never a credential itself.

**A global route is written here rather than through the CLI**, because an installer has to be able to write one
before a runtime has ever run. `jason route set` writes a *campaign's* route and refuses a global one with a
usage error that says so. A campaign override, when you want one, is written with the CLI:

```
jason route set --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --plugin reply
```

## 5. Activate both with one explicit act

```
jason plugin reload --reason "installed the reply plugin"
jason plugin list
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation campaign.enroll
```

The reload reads every package, recomputes each content digest, freezes the routes against them, and swaps both
snapshots together — or refuses the lot and keeps what was working. `plugin list` shows the package as `valid`
with its digest and what it was granted; `route resolve` says which plugin would perform one operation for one
campaign, and why it could not.

## 6. Create the work

```
jason campaign create --name "Q3 LatAm founders"
jason campaign add-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --file contacts.json --match-by email
jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation campaign.enroll --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --input "{\"campaign\":{\"external_id\":\"7\"},\"channel\":\"email\",\"collision\":\"skip\",\"start\":{\"position\":\"first_step\"},\"first_touch\":\"authored_delay\"}"
```

The arguments under `--input` are measured against the operation's own published document
([`docs/contracts/operations/`](contracts/operations)) when the item is created, so a mistake is caught here
rather than at the provider.

## 7. Answer the question it raises

`campaign.enroll` reaches a real person at a provider, and its contract says somebody must confirm it. The
dispatcher therefore **parks** the work instead of performing it: the item's status becomes `awaiting_approval`,
no attempt is made, and nothing is sent.

```
jason approval list
jason approval get apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason approval approve apr_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --actor human:ada --reason "checked the list"
```

`approval get` is what a person reads before deciding: the intent, who would be reached and where, what it would
cost, how reversible it is, and which account it would act through. The decision must name a person — the runtime
refuses one that does not — and it covers exactly what was shown: change the input afterwards and the work parks
again with a fresh question.

## 8. Read what happened

```
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason journal list --work-item wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

The attempt carries what actually ran: the package and its content digest, the operation and its contract
version, both snapshots the decision was made against, the account by identity, and the approval that released
it. The result is the contract's own neutral word — `enrolled`, `already_enrolled` — not the provider's phrasing.

## What this guarantees

Three things are proven by tests that start and kill real processes, rather than by this page.

**Your state survives a restart.** Stop the runtime and start it again and the campaign, the work, the attempt
with its provenance and the approval with the name of whoever gave it read exactly as they did before. The
journal gains the lines the restart itself wrote, and nothing else changes.

**A crash invents no answers.** If the runtime dies while a provider call is in flight, the work is not marked
failed and is not silently repeated. The attempt stays as it was until its lease runs out — at the shipped
settings that is the operation's whole timeout, because a provider attempt reports no heartbeat — and it then
ends **ambiguous**, which is the honest word: nobody can say whether the provider acted. What happens next is the
operation's own contract's to decide. `campaign.enroll` declares a recovery read, so the next attempt asks the
provider what happened before it writes anything, and an enrolment interrupted by a crash costs one read rather
than a second enrolment.

**Take the route away and the work stops where you can see it.** With no route, work fails with `no_route` before
any child process starts, the provider is never asked, and nothing falls back to whichever plugin happens to be
installed. An edit whose binding the package's own schema refuses is rejected whole, and the routes that were
working keep working.

## Where to go next

- [`docs/routing.md`](routing.md) — routes, bindings, and the twelve checks a claim makes in one published order.
- [`docs/work-execution.md`](work-execution.md) — the work item's lifecycle, its statuses and its attempts.
- [`docs/plugins.md`](plugins.md) — writing a package for another provider.
- [`skills/runtime/managed-campaign-work/SKILL.md`](../skills/runtime/managed-campaign-work/SKILL.md) — the same
  path, written for an AI agent to drive.
