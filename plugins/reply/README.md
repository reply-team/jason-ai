# The Reply plugin

Jason's canonical provider operations, performed against a [Reply](https://reply.io) account through the
Reply CLI.

## What it does

Three operations — the whole set this build publishes:

| Operation | What it is at Reply |
|---|---|
| `campaign.get` | Reads one sequence: its name, its state, and Reply's own words for both. |
| `list_membership.add` | Puts one person on one of the account's contact lists. |
| `campaign.enroll` | Puts one person into a sequence. Into a live sequence, that is a send. |

A campaign is a **sequence** at Reply and a person is a **contact**; every call is made by starting the
Reply CLI — `reply api <path>` — with the request body on its standard input.

**The CLI owns the credential, and Jason never sees it.** Signing in is something you do in your own
shell, and what it leaves behind stays in the CLI's own profile store: this package declares no
environment variable, is granted no key, passes none on a command line and reads none. Nothing about a
person reaches an argument either — bodies travel on stdin — so what `~/.jason/logs/` records of a call
is the path, the exit code and how long it took.

## What you need

- **The Reply CLI, installed and signed in.** It is a Node program installed with npm, on Node 20 or
  newer; [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) is where it comes from and
  what documents it. Running `reply auth login` is yours to do, as the person whose account it is.
- **Version 0.5.0 or newer.** The manifest asks for that, and a reload runs `reply --version` and reads
  the answer. An older CLI makes this plugin `unavailable` with `executable_incompatible`, and work
  routed to it fails at the claim with `plugin_unavailable` rather than running against an `api`
  subcommand that answers differently.
- **A runtime that can find it.** The name `reply` is resolved on the search path the *runtime* sees, at
  every reload — which is not always the path your shell sees, least of all for a runtime started in the
  background. `executable_missing` in `jason plugin list --human` means that and nothing else.
- **On Windows, `node.exe` on the same search path.** npm installs the CLI as `reply.cmd`, and the
  runtime starts no program through a shell: `cmd.exe` would parse the arguments, which is the thing the
  argument array exists to prevent. So the runtime reads that shim, cross-checks the entry script it
  names against the package's own `bin`, and starts `node.exe <entry script> …` directly. What it
  actually started is on the record — `launch` in `jason plugin list`, and on every `exec` line in the
  log. The rules a shim is held to, and what each refusal means, are in
  [docs/plugins.md](../../docs/plugins.md) §13.

## Installing it today

In this order, and none of it implied by anything else. Steps 4 and 6 are the two kinds of route there
are; one of them is what makes work reach this plugin at all.

**1. Copy the package.** The directory name is the plugin id, so it has to be `reply`:

```sh
cp -r plugins/reply ~/.jason/plugins/reply
```

**2. Sign in**, in your own shell:

```sh
reply auth login
```

**3. Grant the program**, in `~/.jason/config/settings.json`. A manifest that requests an executable is
not permission to start it, and without the grant every call fails at `host.exec`:

```json
{
  "Plugins": { "Grants": { "reply": { "Exec": ["reply"] } } }
}
```

**4. Write a global route, if a global route is what you want** — in the same file. `Default` sends every
operation to this plugin; a key under `Operations` names one:

```json
{
  "Routes": {
    "Default": { "Plugin": "reply", "Binding": { "profile": "outbound" } }
  }
}
```

**5. Reload.** One act freezes the package, the grants and the global routes together; nothing edited
into either section has any effect until it happens:

```sh
jason plugin reload --human
jason plugin list --human          # status, granted over requested, the version it answered
```

**6. Or route one campaign**, which is a verb rather than a setting and takes effect at once. The plugin
has to be in the active snapshot already, which is why this comes after the reload:

```sh
jason route set --campaign cmp_… --plugin reply --binding '{"profile":"outbound"}'
jason route resolve --campaign cmp_… --operation list_membership.add --human
```

**There is deliberately no global `route set`.** A global route is a setting, frozen by the same explicit
act that freezes the plugins it names, so the only way to write one is to edit `settings.json` and
reload; the CLI refuses `route set` without `--campaign` and says where a global route lives instead.
[docs/routing.md](../../docs/routing.md) §3 and §4 are the two halves of that.

**And three things that are not true yet.** There is **no install verb**: installing is copying a
directory, and nothing reads it until a reload. There is **no signing and no publisher identity**: the
digest in `plugin list` says what the files are, never who wrote them, so a package is exactly as trusted
as the machine you copied it onto. There is **no update awareness**: nothing checks whether a newer
version exists, and because the id is the directory name there is **one version per id** — upgrading is
replacing the directory and reloading.

## The account a route names

The manifest declares a binding of two optional fields, and the schema is closed:

| Field | What it selects |
|---|---|
| `profile` | Which of the CLI's stored profiles the calls act as — one of those `reply profile list` shows, each with its own credential. |
| `team_id` | Which team the account acts in, for a credential that spans more than one. |

`profile` absent means whichever profile the CLI itself is currently set to; `team_id` absent means
whatever that profile resolves to on its own. **A route with no binding at all is a valid route**, and it
runs against the CLI's own defaults — whatever typing `reply` in your shell would do. On a machine with
one account that is the truth; on any other it is a guess, and naming the profile is the whole difference.

A field the schema does not declare is refused when the route is activated, as `route_binding_invalid`; a
field that reads like a credential is refused before that, because a binding selects an identity the
CLI's store already holds and is journaled and listed in full.

**A `team_id` spelled like a number cannot be written in `settings.json`.** That file is read through a
string store in which `"12345"` and `12345` are the same five characters by the time anything reads them,
so the value reaching the binding is a number and the schema refuses it. Write that route per campaign
instead: `jason route set --binding` carries JSON and never passes through the configuration system.

### What does not reach it

`REPLY_CONFIG_DIR`, `REPLY_PROFILE`, `REPLY_TEAM_ID` and `REPLY_API_KEY` exported in your own shell reach
the CLI when you type `reply` yourself and reach nothing when Jason starts it. **That is the design
rather than an omission.** The plugin host's environment is built from nothing instead of inherited: a
short published list of machine configuration is copied, no vendor's name is on that list, and this
package requests no `capabilities.env` at all, so there is no variable it could be granted either. The
profile store and the route's binding are the two ways an account is chosen, and both are written
somewhere a person can read them.

What the child does get is the user's own configuration directory — `APPDATA` on Windows, `HOME` and
`XDG_CONFIG_HOME` everywhere else — because that is where the CLI keeps its profile store. The one
variable this package sets is `REPLY_NO_UPDATE_CHECK=1`, on the CLI it starts, so an operation's budget
is not spent on a version check nobody asked for.

## What can go wrong

Every failure carries a code the operation's own contract publishes and a class the runtime acts on:
`transient` is retried inside the work item's budget, `permanent` and `validation` are final, and
`ambiguous` stops the item for a person, because Reply may already have acted. The tables below are
[`modules/errors.js`](modules/errors.js), which is their single source — a test reads that file and holds
every code in it to the operation's published contract, the same code under the same class.

### Which call it was

Whether a lost answer is cheap or expensive is decided by the call it was lost on, so every call this
package makes is marked once. The mark is per call rather than per verb: an import is a `POST` that
creates people.

| Call | Mark | What it is for |
|---|---|---|
| `GET /v3/sequences/{id}` | read | The sequence itself: what `campaign.get` answers from, and the live-state read before an enrollment. |
| `GET /v3/sequences/{id}/contacts/{contact_id}` | read | Whether this person already takes part: `campaign.enroll`'s first recovery read. |
| `GET /v3/contacts/{id}/statuses` | read | The opt-out register, and by the same call whether a pin still resolves. |
| `GET /v3/contacts/{id}/lists` | read | Which lists hold this person: `list_membership.add`'s recovery read. |
| `POST /v3/contacts/import` | write | Matches or creates the contact by email, in one call. |
| `POST /v3/contacts` | write | Creates a contact the import will not take, because it carries no first name. |
| `POST /v3/contact-lists/{id}/add-contacts` | write | The add itself. |
| `POST /v3/sequences/{id}/contact-links/bulk` | write | The enrollment itself. Into a live sequence, a send. |

### Endings every operation can meet

| When | Code | Class | What to do about it |
|---|---|---|---|
| The CLI could not be started at all, so nothing was sent. | `provider_call_failed` | permanent | Install the CLI where the runtime can find it, check that the grant names it, reload. |
| The CLI refused the call before making it — its own usage error. | `provider_call_failed` | permanent | Nothing reached the account and a repeat builds the same call. Report it: either this package or the CLI's surface has moved. |
| A **read** timed out, answered 5xx, or exited with nothing that could be parsed. | `provider_unavailable` | transient | Nothing. The call changed nothing, so the attempt is repeated. |
| A **write** timed out, answered 5xx, or exited with nothing that could be parsed. | `provider_answer_lost` | ambiguous | The item stops for a person. Look at the account — the contact, the list, the sequence — and decide; a blind repeat can create somebody twice or send to them twice. Whatever the attempt had learned travels with the failure, so there are identifiers to look by. |
| 401 or 403, and the per-item word `forbidden` inside a 200. | `unauthorized` | permanent | Sign that profile in again, or issue a key carrying the scope the call needs. |
| 429. | `rate_limited` | transient | Nothing. The runtime waits and repeats the attempt. |
| Any other status, or a business code no row below names. | `provider_call_failed` | permanent | Read `details`: the status and Reply's own code are in it. Reply's per-item error set is published as an open one, so there will be some — and a refusal that recurs belongs in the table. |

### `campaign.get`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier the work item carries. A link the provider has denied is not recorded, so there is nothing to undo. |

This operation is one read and nothing else, so the ambiguous ending above cannot arise for it.

### `list_membership.add`

| When | Code | Class | What to do about it |
|---|---|---|---|
| `args.list.external_id` is not a Reply list number, or the account holds no such list. | `list_not_found` | permanent | Correct the identifier. Nothing about repeating the call can change it. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there. | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again, and this version has no repair path for one. |
| Reply's own opt-out register holds this person. | `suppressed` | permanent | Nothing, on purpose. Only the person themselves can ask to be reinstated. |
| Reply will not accept the address on the channel the call named. | `invalid_channel_value` | validation | Correct the contact's data; no later attempt would be accepted either. |
| The account will hold no further contact. | `limit_reached` | permanent | Free capacity at Reply. A **list's** own capacity publishes no code of its own, so a full list arrives as the last shared row instead of this one. |

### `campaign.enroll`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier. Nobody is ensured for a campaign that cannot exist. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there — including as the per-item word `contactNotFound`. | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again. |
| The sequence is archived, or holds no step to send. | `campaign_not_enrollable` | permanent | Un-archive it at Reply, or give it a step. The item fails rather than waiting for either. |
| `collision: refuse` was asked for and Reply says this person already takes part. | `collision_refused` | permanent | Choose another collision policy if a second participation is what was meant. A repeated attempt answers differently — see below. |
| Reply's own opt-out register holds this person. | `suppressed` | permanent | Nothing, on purpose. |
| Reply will not accept the address on the channel the call named. | `invalid_channel_value` | validation | Correct the contact's data. |
| The account will hold no further contact or enrollment. | `limit_reached` | permanent | Free capacity at Reply. A **sequence's** own capacity publishes no code, so it arrives as the last shared row. |
| The arguments name a call that cannot be built: a start position the shape of this sequence's steps cannot answer, or a first touch this version has no word for. Nothing is sent. | `provider_call_failed` | permanent | `details.reason` says which reading failed. Ask for the first step, or name a position the sequence really has. |
| Reply answers the per-item word `invalidInput` for this person. | `provider_call_failed` | permanent | Reply read the request and would not take it; its own word is in the details. A request built wrong is built the same way every time. |

## What it cannot do

- **`campaign.enroll` is refused at the claim, with `approval_required`, in this build.** That operation
  needs a person's approval and nothing here can ask for one, so every item of it fails closed before a
  child process exists. The operation itself is implemented and tested, and it can be driven through the
  plugin host by hand — [docs/plugins.md](../../docs/plugins.md) §7 and §12 say how.
- **`campaign.get` answers no counts.** `counts` is `{}`, which is the contract's way of saying the
  provider reports none: a Reply sequence carries no people counts at all, and the one count endpoint in
  the published description still says it is coming. An empty object is a different fact from a zero, and
  the alternative was paging every contact in the sequence inside the operation's own budget.
- **A campaign name longer than 500 characters is truncated.** That is what the contract allows a name
  and Reply agrees to no limit, so the neutral half carries what it can hold and Reply's whole name
  survives beside it under `vendor.name`. Passing the longer one through unchanged would make the answer
  invalid and cost the read of that campaign for good, over a display string.
- **`immediately` and `next_open_window` are the same request.** Reply always sends inside the sequence's
  own schedule and publishes no way to bypass a sending window, so those two first touches ask it for
  exactly the same thing. `authored_delay` is the one that differs: it leaves the step's own delay alone.
- **Reply has no idempotency key and no per-run ledger.** The recovery read before a repeat therefore
  reads the membership or the participation itself, which is the nearest answer the provider can give: it
  cannot tell this work item's own earlier write from somebody else's addition or enrollment of the same
  person an hour ago. It is a substitution for the reading the contract asks for rather than that reading.
- **A repeat with `collision: refuse` against an existing participation answers `already_enrolled`, not
  `collision_refused`.** The recovery read comes first and the document's instruction is to answer from it
  when the effect already happened, so that instruction wins over the collision policy — and it has to,
  because after a lost answer the participation the read finds may well be the one this work item made.
  `collision_refused` is what a first attempt reports.
- **A person who opts out between two attempts is not told the add or the enrollment failed.** The
  recovery read runs before the opt-out register is consulted, so work that already happened is reported
  as having happened. Reading it the other way round would turn a finished add into a permanent failure
  over something that arrived after it.
- **`start.position: "step"` works only for a sequence whose steps form a single chain.** Reply publishes
  no ordering field on a step at all — the steps are a graph of parents, and a branch is labelled `"2A"` —
  so "the third step" has a sound meaning only while there is one way through. The plugin walks the chain
  from the root and takes the Nth link; a condition step in the way, a fork, or an N past the end is
  refused with the reason named, because taking the Nth element of the array instead would be a guess
  whose cost is a real person receiving the wrong message.
- **List, sequence and contact identifiers have to be Reply's own numbers.** Anything else names nothing
  the account could hold and is refused without a call being made. The published contract illustrates
  `list_membership.add` with `"external_id": "L-1129"` — that is an example of the argument's shape, and
  this plugin refuses it.
- **`time_zone` is not sent.** Reply's `timeZoneId` accepts a vocabulary published nowhere and an
  unrecognised value fails the whole import item, so sending a person's own time zone would cost the
  import rather than improve it.
- **A person with no first name is created rather than imported**, because Reply's import refuses an item
  without one and inventing a first name would write data nobody supplied into a customer's own records.
  That second path publishes no code for an address the account already holds, so a duplicate there is
  reported as `provider_call_failed` with Reply's own word in the details, rather than mapped to a code
  nobody published.

## Versioning

This package carries its own version and its own [changelog](CHANGELOG.md), and versions independently of
the runtime: SemVer, with the manifest's `contracts` block saying which operation and protocol families it
speaks. A change at Reply's end — a new field, a moved endpoint, a refusal this table has no word for — is
absorbed by releasing the package, with no runtime release involved.

## Where to look next

- [docs/plugins.md](../../docs/plugins.md) — the mechanism: the manifest and every rule it is held to, the
  Host SDK and its limits, capabilities and grants, the invocation protocol, testing a package locally,
  and what this version of the mechanism cannot do.
- [docs/contracts/](../../docs/contracts/README.md) — the operations themselves: one document each, with
  the schemas, the published fixtures and the conformance list a plugin has to demonstrate.
- [docs/routing.md](../../docs/routing.md) — which plugin performs an operation for a campaign, which
  account it works in, and every reason a claim refuses provider work.
