# The wiring check

This page checks the wiring — the runtime, the plugin, its grant and its route, one contact and one enrolment —
and reads it back. It is not the real work, which begins with a campaign planned with the business skills,
[`campaign-planning`](../skills/business/campaign-planning/SKILL.md) first, and worked by
[Jason's roles](../skills/runtime/roles). Every `jason` command below is a command the end-to-end tests execute,
and the three guarantees at the end are the three tests that prove them.

The provider here is [Reply](https://reply.io) through [`plugins/reply/`](../plugins/reply/README.md), because
that is the plugin this repository ships. Nothing in the runtime knows about it: a different provider is a
different package and a different route, and the steps are the same.

## What you need

- **The `jason` executable**, installed from a release as [`docs/INSTALL.md`](INSTALL.md) §2 describes, and on
  your `PATH`. Everything below then reads as written. *(The tests put nothing on a `PATH`: they run the same
  program as `dotnet jason.dll`, built from the runtime's source.)*
- **The Reply CLI**, installed and signed in, if you are following this against a real account. It owns the
  credential; Jason never sees one. *(The tests run against a stand-in that answers like it, so nothing in CI
  touches a real account.)*

### If an agent runs it

Read this whole page first, word for word, and follow it in order. It assumes no repository and no particular
folder: start in any folder of yours, and nothing in this run writes there — the one file step 6 makes goes in the
system's temporary directory.

Before anything, ask the person one question in plain words: what you will install and from where — the Reply CLI
from npm's registry, and Node if it is missing; what this run writes — the package into Jason's data directory,
the grant and the route into its `settings.json`, and one draft campaign in their account at the provider; which
acts are theirs to do; and which need their yes. Then do the rest yourself, in order.

An act this page marks **Ask first.** needs the person's word, as on [the install page](INSTALL.md): ask, and do
it only on their yes; with nobody to ask, leave it undone and say so. An act marked **Yours to do.** is the
person's own and never yours: say what it is, and wait until they say it is done. Signing in to the Reply CLI is
theirs because the key must not pass through this conversation — never ask for it, and never read the CLI's
profile store.

- The contact is the person's own address, which they type.
- The provider's campaign is the one step 6 makes on their yes, never one that was there before.
- `campaign.get` must answer `draft` before anything is written to it. Any other status stops the run, and you say
  why.
- `enrollment.list` must answer a `total` of 0 before the enrolment. Any other count, null included, stops the
  run.
- The enrolment refuses a collision (`collision: refuse`), and `total` is 1 after it.
- A provider's campaign nobody started sends nothing, and this run never starts one: never run
  `campaign.activate`.
- A refusal by your harness, by npm or by the provider is reported once, as it stands, and never worked around,
  as [the install page](INSTALL.md) says.
- When you stop, name the next step. At the end, give the person the way back in two lines: the stop, and the
  pause after it.

### The Reply CLI

It is a Node program, installed with npm, and it needs Node 20 or newer:

```
npm install -g reply-cli
```

**Ask first.** Where Node is missing, or older than 20, install it first: it is a system package, so it is the
person's to allow.

**Yours to do.** Sign in, in your own terminal, as the person whose account it is. It may open your browser:

```
reply auth login
```

In Windows PowerShell, type `reply.cmd auth login`: its default execution policy refuses the `reply.ps1` that npm
installs beside the command.

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

Installing a plugin is copying its directory into `~/.jason/plugins/<id>/`. There is no install verb. The package
comes from the tree `jason skills install` fetched when the installation was taught — this repository at the tag
of the release you run, kept in Jason's data directory — where `<version>` is what `jason --version` prints, up to
any `+`:

```
cp -r ~/.jason/skills/staged/v<version>/plugins/reply ~/.jason/plugins/reply
```

So the package is the one released with your runtime, and nothing is fetched a second time. Where that tree is not
there — the skills came from a directory, or from another ref — fetch the release's tree into a temporary directory
first, and copy the package from it:

```
git clone --depth 1 --branch v<version> https://github.com/reply-team/jason-ai <temporary directory>
cp -r <temporary directory>/plugins/reply ~/.jason/plugins/reply
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

The one person in this run is you, by your own address. First the file, on its own: `contacts.json`, in the
system's temporary directory, holds your address and nothing else. An agent writes it with its own file tool, not
from a shell line:

```json
[{"channels":[{"channel":"email","value":"<your address>"}]}]
```

Then the commands, one at a time: Jason's campaign, the person, from the file where you wrote it, and the start of
Jason's campaign:

```
jason campaign create --name "Wiring check" --idempotency-key wiring-check
jason campaign add-contacts cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --file <temporary directory>/contacts.json --match-by email
jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

`jason campaign start` starts Jason's own campaign, so the work below can run; nothing at the provider is started.

**Ask first.** The next command makes the provider's campaign, the first thing this installation writes to your
account at the provider. Its contract asks no question, because a draft that holds nobody reaches nobody; this page
asks, because it is the first write.

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation campaign.create --input '{"name":"Jason wiring check"}' --idempotency-key wiring-check-campaign
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason campaign get cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

This `campaign.create` is the provider operation: it makes the provider's campaign for Jason's campaign Wiring
check — at Reply, a sequence — and links the two. It is not `jason campaign create` above, which made Jason's own
campaign: an operation and a verb can share their words ([two namespaces](contracts/README.md)). Read the item
until its `status` is `succeeded`. Its `result` holds one item, which says what was made: `state` is `draft`,
`enrolled` is 0 and `steps_added` is 1; its `campaign_external_id` is the provider's identifier for it, which
Jason's campaign now carries as its pin — `jason campaign get` lists it under `external_ids`. Every command below
works by that pin and names no provider identifier. A provider's campaign of that name already there, from a wiring
check run before on this account, fails `campaign_name_taken`, and nothing is made: ask the person for another name,
and create it with a key of its own.

Then two reads of the provider's campaign, before anything is written to it:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation campaign.get --input '{}' --idempotency-key wiring-check-read
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation enrollment.list --input '{"limit":10,"offset":0}' --idempotency-key wiring-check-people
jason workitem get wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

Both reads run without a question: reading changes nothing at the provider. The first must say the provider's
campaign is a `draft`; the second must say its `total` is 0. Anything else stops the run here: a provider's
campaign that has started, or that already holds somebody, is not one this run enrols into.

Then the enrolment, which refuses rather than decides if you already take part:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation campaign.enroll --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --input '{"channel":"email","collision":"refuse","start":{"position":"first_step"},"first_touch":"authored_delay"}' --idempotency-key wiring-check-enrol
```

A provider's campaign nobody started sends nothing, and this run never starts one.

Each create carries a key you chose. If an answer never comes back, send the same command with the same key: it
answers with what the first one made rather than making it twice.

The arguments under `--input` are measured against the operation's own published document
([`docs/contracts/operations/`](contracts/operations)) when the item is created, so a mistake is caught here
rather than at the provider.

They are single-quoted JSON. Windows PowerShell 5.1 strips the quotes inside them, and the CLI then refuses the
line with exit 2 before anything is made. There, write the arguments into a file as `{"context":{"input":…}}`,
with your own file tool if you are an agent, and pass it with `--file` in place of `--input`.

## 7. Answer the question it raises

`campaign.enroll` reaches a real person at a provider, and its contract says somebody must confirm it. The
dispatcher therefore **parks** the work instead of performing it: the item's status becomes `awaiting_approval`,
no attempt is made, and nothing is sent.

**Ask first.** The decision is the person's: show them what `approval get` says, and approve only on their yes, in
their name.

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
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation enrollment.list --input '{"limit":10,"offset":0}' --idempotency-key wiring-check-people-after
```

The attempt carries what actually ran: the package and its content digest, the operation and its contract
version, both snapshots the decision was made against, the account by identity, and the approval that released
it. The result is the contract's own neutral word — `enrolled`, `already_enrolled` — not the provider's phrasing.
The read after it says the provider's campaign's `total` is 1: you, and nobody else.

So the provider's campaign this run made was never started, holds nobody but you, and has one task step, added
only so that it can exist.

### The way back

**Ask first.** Take yourself out of the provider's campaign, which asks its own question, as the enrolment did in
step 7:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation enrollment.stop --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --input '{"channel":"email","reason":{"code":"other","note":"the end of a wiring check"}}' --idempotency-key wiring-check-stop
```

Then leave it: the provider's campaign this run made holds nobody and sends nothing. Archiving or deleting it is
yours to choose, whenever you like, and no step of this page.

Last, once the stop's work item has finished — or at once, if you said no and the stop was never made: you then stay
in the provider's campaign, which was never started and sends nothing — pause Jason's campaign:

```
jason campaign pause cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

With Jason's campaign paused, Jason stops reading the provider's campaign, and nothing at the provider changes: the
pause is Jason's own, `jason campaign start` undoes it, and step 8's chronicle is kept. It waits for the stop to
finish because work in a paused campaign waits too, the stop's included; and whatever you later do with the
provider's campaign, no read of Jason's fails over it.

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

- [`campaign-planning`](../skills/business/campaign-planning/SKILL.md) — the real work after this check: a campaign
  planned with the business skills and worked by [Jason's roles](../skills/runtime/roles).
- [`docs/plugins.md`](plugins.md) — writing a package for another provider.
- [`docs/contracts/`](contracts/README.md) — the operations themselves, one document each, and what a plugin has to
  demonstrate.
- [`skills/runtime/`](../skills/runtime) — the same ground, written for an AI agent to drive. Five skills,
  one per moment somebody asks for something: `managed-campaign-work` turns an objective into durable work;
  `approvals-and-questions` presents what is waiting on a person; `operating-the-installation` covers the
  runtime, its plugins, its routes and its updates; `troubleshooting-jason` says why work stopped; and
  `reporting-outside-effects` tells Jason about an effect produced somewhere else.
