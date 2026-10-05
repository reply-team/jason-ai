# The Reply plugin

Jason's canonical provider operations, performed against a [Reply](https://reply.io) account through the
Reply CLI.

## What it does

Eleven operations — the whole set this build publishes: six that write and five that only read.

| Operation | What it is at Reply |
|---|---|
| `campaign.get` | Reads one sequence: its name, its state, and Reply's own words for both. |
| `list_membership.add` | Puts one person on one of the account's contact lists. |
| `campaign.enroll` | Puts one person into a sequence. Into a live sequence, that is a send. |
| `enrollment.list` | Reads one page of the people a sequence holds, and Reply's count of them. No address is returned. |
| `engagement.summarize` | Reads a sequence's counts over one window, a row per channel. Counts only: no rate is returned. |
| `conversation.list` | Reads one page of inbox threads: one person's across the account, or one sequence's. A person is their identifier and the name Reply shows, never an address. |
| `conversation.get` | Reads one page of one thread's messages, the first sent first: who wrote each, to whom, and the body as Reply holds it. |
| `campaign.pause` | Pauses one sequence: the incident response. A paused sequence stops sending; pausing one already paused changes nothing. |
| `campaign.activate` | Starts one sequence sending, or resumes a paused one, for the population a person approved. A different count starts nothing. |
| `optout.record` | Records that somebody said stop: sets Reply's opt-out flag on the contact it holds under the address, made only when it holds nobody. What they said stays Jason's record. |
| `enrollment.stop` | Takes one person out of a sequence, once a person approves it. Nobody is created to do it, and the reason stays Jason's record. |

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

In Windows PowerShell, type `reply.cmd auth login`: its default execution policy refuses the `reply.ps1`
that npm installs beside the command. PowerShell 7 and Command Prompt take `reply auth login` as written.

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

A failure's message is all of it that reaches the work item's last error. What it carries beyond its code, class and
message — Reply's status and code, the package's own reason, the numbers a decision was about — is in the failed
attempt's trace ([docs/plugins.md](../../docs/plugins.md) §5, "Failing"): `jason workitem get <id>` prints it as
`attempts[].error.trace` in its JSON, the default, and the `--human` view does not show it. The tables below say "the
trace" for it.

### Which call it was

Whether a lost answer is cheap or expensive is decided by the call it was lost on, so every call this
package makes is marked once. The mark is per call rather than per verb: an import is a `POST` that
creates people.

| Call | Mark | What it is for |
|---|---|---|
| `GET /v3/sequences/{id}` | read | The sequence itself: what `campaign.get` answers from, the live-state read before an enrollment, and the state read before a pause or a start. |
| `GET /v3/sequences/{id}/contacts/{contact_id}` | read | Whether this person already takes part: `campaign.enroll`'s first recovery read, and what `enrollment.stop` reads before it removes anything. |
| `GET /v3/sequences/{id}/contacts` | read | One page of the people a sequence holds: what `enrollment.list` answers from. |
| `GET /v3/sequences/{id}/contacts/count` | read | How many people the sequence holds: `enrollment.list`'s total, and the population `campaign.activate` checks before it starts. |
| `POST /v3/sequences/{id}/stats` | read | A sequence's counts over one window: what `engagement.summarize` answers from. A `POST` that changes nothing. |
| `POST /v3/inbox/threads/filter` | read | One page of inbox threads, by person or by sequence: what `conversation.list` answers from. A `POST` that changes nothing. |
| `GET /v3/inbox/threads/{id}` | read | One thread with its whole history: `conversation.get`'s header. |
| `GET /v3/inbox/threads/{id}/messages` | read | One page of a thread's messages: what `conversation.get` answers from. |
| `GET /v3/contacts/{id}/statuses` | read | The opt-out register, and by the same call whether a pin still resolves. |
| `POST /v3/contacts/filter` | read | The people one list holds, a page of them: `list_membership.add`'s recovery read; and, by a search term, the contacts whose address holds one: the lookup `optout.record` makes before it creates anybody, and the only way `enrollment.stop` finds a person it has no pin for. |
| `POST /v3/contacts/import` | write | Matches or creates the contact by email, in one call. |
| `POST /v3/contacts` | write | Creates a contact the import will not take, because it carries no first name. |
| `POST /v3/contacts/set-opted-out` | write | The opt-out itself: Reply's flag, set for one contact. This package never clears it. |
| `POST /v3/contact-lists/{id}/add-contacts` | write | The add itself. |
| `POST /v3/sequences/{id}/contact-links/bulk` | write | The enrollment itself. Into a live sequence, a send. |
| `DELETE /v3/sequences/{id}/contact-links/{contact_id}` | write | The removal itself, with no body: the person is taken out, never marked finished. |
| `POST /v3/sequences/{id}/pause` | write | The pause itself, with no body. Reply documents it as idempotent. |
| `POST /v3/sequences/{id}/start` | write | The start itself, with no body; it also resumes a paused sequence. Step 1 goes to everybody enrolled, so it is a send. |

### Endings every operation can meet

| When | Code | Class | What to do about it |
|---|---|---|---|
| The CLI could not be started at all, so nothing was sent. | `provider_call_failed` | permanent | Install the CLI where the runtime can find it, check that the grant names it, reload. |
| The CLI refused the call before making it — its own usage error. | `provider_call_failed` | permanent | Nothing reached the account and a repeat builds the same call. Report it: either this package or the CLI's surface has moved. |
| A **read** timed out, answered 5xx, or exited with nothing that could be parsed — or, made inside a write, was cut off at `Plugins:Exec:OutputBytes`. | `provider_unavailable` | transient | Nothing. The call changed nothing, so the attempt is repeated. |
| A **write** timed out, answered 5xx, exited with nothing that could be parsed, or its answer was cut off at `Plugins:Exec:OutputBytes`. | `provider_answer_lost` | ambiguous | The item stops for a person. Look at the account — the contact, the list, the sequence — and decide; a blind repeat can create somebody twice or send to them twice. Whatever the attempt had learned travels with the failure, so there are identifiers to look by. |
| 401 or 403, and the per-item word `forbidden` inside a 200. | `unauthorized` | permanent | Sign that profile in again, or issue a key carrying the scope the call needs. |
| 429. | `rate_limited` | transient | Nothing. The runtime waits and repeats the attempt. |
| Any other status, or a business code no row below names. | `provider_call_failed` | permanent | The status and Reply's own code are in the trace. Reply's per-item error set is published as an open one, so there will be some — and a refusal that recurs belongs in the table. |

A call that runs out of time is the package's to answer, so each call is given what is left of the invocation's
budget less five seconds (never more than half of what is left) rather than all of it. A call given everything
runs out at the moment the host's own engine ends the invocation, and on a busy machine the host's work after
a cut-off call — ending the CLI's process tree, waiting for its pipes — can outlast what the engine has left;
the host then answers `plugin_timeout`, and the identifiers the attempt had learned do not travel with it.
A call cut off sooner is one the package hears about, and it answers from the two timeout rows above.

### `campaign.get`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier the work item carries. A link the provider has denied is not recorded, so there is nothing to undo. |
| Reply's answer was longer than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. | `provider_answer_too_large` | permanent | Raise `Plugins:Exec:OutputBytes`. The same read would be cut at the same place, so it is not repeated. |

This operation is one read and nothing else, so the ambiguous ending above cannot arise for it.

### `enrollment.list`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| Reply answers 403 with its own code `sequenceContact.forbidden`: the account's role may not read a sequence's people. | `feature_unavailable` | permanent | A role or a plan that includes it. Signing in again changes nothing. Any other 403 is `unauthorized` above. |
| Reply's answer was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program, so it was cut off. | `provider_answer_too_large` | permanent | Ask for a smaller `limit`, or raise `Plugins:Exec:OutputBytes`. |

A refusal of the count alone is not a failure: the page is answered and `total` is null.

### `engagement.summarize`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier. |
| Reply answers 403 with its own code `sequenceStats.forbidden`: the account's role may not read a sequence's statistics. | `feature_unavailable` | permanent | A role or a plan that includes it. Any other 403 is `unauthorized` above. |
| Reply refuses the window — a 400 carrying `errors[]`, such as a span that ends before it starts. | `window_invalid` | validation | Correct `args.window`; the same window is refused every time. |
| Reply's answer was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program. | `provider_answer_too_large` | permanent | Raise `Plugins:Exec:OutputBytes`. |

### `conversation.list`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The work item names a person and no Reply contact identifier is pinned for them. Nothing is called. | `contact_not_linked` | permanent | A write that ensures the contact — `list_membership.add` or `campaign.enroll` — is what links them. A read creates nobody. |
| The pinned contact identifier, or the campaign's, is not a Reply number at all. Nothing is called. | `provider_call_failed` | permanent | The pin is wrong; the trace says which. |
| Reply answers 403 with its own code `inboxThread.forbidden`: the inbox is not available to this user or team. | `feature_unavailable` | permanent | A plan or a role that includes the inbox. Any other 403 is `unauthorized` above. |
| Reply's answer was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program. | `provider_answer_too_large` | permanent | Ask for a smaller `limit`, or raise `Plugins:Exec:OutputBytes`. |

A person or a sequence with no threads is an empty page, not a failure. A contact identifier Reply does not hold is
answered with a validation problem that names no code, so it arrives as `provider_call_failed`.

### `conversation.get`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply thread number, or Reply holds no thread under it that this account may see. | `conversation_not_found` | permanent | Read the thread by an identifier `conversation.list` returned. |
| Reply answers 403 with its own code `inboxThread.forbidden`. | `feature_unavailable` | permanent | A plan or a role that includes the inbox. Any other 403 is `unauthorized` above. |
| Reply's answer was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program. Reply sends a thread's whole history with the thread, so a long enough history does this whatever page is asked for. | `provider_answer_too_large` | permanent | Raise `Plugins:Exec:OutputBytes`, up to about twice its default: past that, the two answers a thread read holds outgrow the package's memory. A smaller page cannot help. |

### `list_membership.add`

| When | Code | Class | What to do about it |
|---|---|---|---|
| `args.list.external_id` is not a Reply list number, or the account holds no such list. | `list_not_found` | permanent | Correct the identifier. Nothing about repeating the call can change it. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there. | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again, and this version has no repair path for one. |
| Reply's own opt-out register holds this person. | `suppressed` | permanent | Nothing, on purpose. Only the person themselves can ask to be reinstated. |
| Reply will not accept the address on the channel the call named. | `invalid_channel_value` | validation | Correct the contact's data; no later attempt would be accepted either. |
| The account will hold no further contact. | `limit_reached` | permanent | Free capacity at Reply. A **list's** own capacity publishes no code of its own, so a full list arrives as `provider_call_failed` instead of this one. |

### `campaign.enroll`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier. Nobody is ensured for a campaign that cannot exist. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there — including as the per-item word `contactNotFound`. | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again. |
| The sequence is archived, or holds no step to send. | `campaign_not_enrollable` | permanent | Un-archive it at Reply, or give it a step. The item fails rather than waiting for either. |
| `collision: refuse` was asked for and Reply says this person already takes part. | `collision_refused` | permanent | Choose another collision policy if a second participation is what was meant. A repeated attempt answers differently — see below. |
| Reply's own opt-out register holds this person. | `suppressed` | permanent | Nothing, on purpose. |
| Reply will not accept the address on the channel the call named. | `invalid_channel_value` | validation | Correct the contact's data. |
| The account will hold no further contact or enrollment. | `limit_reached` | permanent | Free capacity at Reply. A **sequence's** own capacity publishes no code, so it arrives as `provider_call_failed`. |
| The arguments name a call that cannot be built: a start position the shape of this sequence's steps cannot answer, or a first touch this version has no word for. Nothing is sent. | `provider_call_failed` | permanent | The trace says which reading failed. Ask for the first step, or name a position the sequence really has. |
| Reply answers the per-item word `invalidInput` for this person. | `provider_call_failed` | permanent | Reply read the request and would not take it; its own word is in the trace. A request built wrong is built the same way every time. |

### `campaign.pause`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| The sequence is archived, or never started — read from its state, so no pause is asked for — or Reply refuses the pause with 409 or 400. | `campaign_not_pausable` | permanent | Nothing to pause. The trace says which, with Reply's own code where it gave one. |

The pause is idempotent at Reply, so the ambiguous ending above is repeated rather than stopped for a person: the
next attempt reads the state and answers `already_paused` if the pause landed. A pause runs even while its Jason
campaign is paused, because the provider's sequence must still stop after somebody paused Jason's first.

### `campaign.activate`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| The sequence is archived — read from its state, so no start is asked for — or Reply refuses the start: 409 archived, or 400 with no contacts, no connected sending account or no sending hours. | `campaign_not_activatable` | permanent | Change at Reply what the trace names — Reply's own code, where it gave one — then propose the activation again. |
| Reply's count, read at execution, is not the population the person approved. Nothing starts. | `population_changed` | permanent | Read the count again and propose a new activation: the trace carries both numbers. |

A start whose answer was lost may already be sending, so the next attempt reads the sequence's state and its count
first, and answers `already_live` rather than starting twice. A person approves every activation: it waits for them
before it runs, as `campaign.enroll` does.

### `optout.record`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The pinned contact identifier is not a Reply contact number, or no longer resolves there — including as the word `notFound` against this person in the opt-out's answer. | `contact_not_found` | permanent | The pin has to be cleared before the opt-out can be recorded at Reply. |
| The lookup found more than one contact under the address. Nothing is set. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the opt-out again. |
| The lookup read five pages of a hundred and Reply said there were more, so one match could not be told from two. Nothing is made and nothing is set. | `provider_call_failed` | permanent | Record the opt-out at Reply by hand: a repeat would read the same pages. Where `jason suppression add` recorded the address, Jason's own register already stops Jason's own reach. The trace says why. |
| Reply will not accept the address, so no contact could be made to record the opt-out on. | `invalid_channel_value` | validation | Correct the contact's data. |
| The account will hold no further contact, so the person could not be made there. | `limit_reached` | permanent | Free capacity at Reply. Reply never held them under this address, so it cannot reach them there either. |

The person is found before anything is written: by the pin, or by looking the address up — Reply's free-text search,
whose matches the package compares with the address exactly and whatever the case, read to its end. Only when Reply
holds nobody under it is a contact made, through the import or, without a first name, the create path. The flag is
then read, and a person Reply already holds as opted out answers `already_recorded` with nothing written; that read is
also what makes a lost answer safe to repeat. The opt-out runs without anybody approving it — not running is the
harm — and it runs even while its Jason campaign is paused. Jason's own register is the other half:
`jason suppression add` stops Jason's reach at once for that address, and is written first.

### `enrollment.stop`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or — read first when the work item names the campaign for the first time — Reply holds no sequence under it. | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. A campaign already pinned is not read again: if Reply no longer holds it, its one lumped 404 reads `already_stopped`. |
| The pinned contact identifier is not a Reply contact number at all. Nothing is called. | `contact_not_found` | permanent | The pin is wrong and has to be cleared. A well-formed pin Reply no longer holds is not this: it reads `already_stopped`. |
| The lookup found more than one contact under the address. Nobody is taken out. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the stop again. |
| The lookup read five pages of a hundred and Reply said there were more, so one match could not be told from two, or from none. Nobody is taken out. | `provider_call_failed` | permanent | Take the person out at Reply by hand, then check with `enrollment.list`: a repeat would read the same pages. It is never read as a person Reply does not hold. The trace says why. |
| The person carries no value on the channel the call named. Nothing is called. | `invalid_channel_value` | validation | Name a channel they have. |

The person is found without creating anybody: by the pin, or by looking the address up as `optout.record` does. A
person Reply does not hold takes part in nothing, so the stop answers `already_stopped` and asks nothing more. Then the
participation is read — a person who takes no part, or whose part is finished, is already stopped and nothing is
removed — and only then is the person taken out, never marked finished. That read is also what makes a lost answer
safe to repeat. Reply's own opt-out is never read: a stop is for exactly the people it would refuse. A person approves
every stop before it runs, and its work waits while its Jason campaign is paused.

## What it cannot do

- **An opt-out at Reply is a yes or a no.** What they said, where it arrived and when are Jason's record, carried
  by the work item; none of it reaches Reply, whose contact holds only its opt-out flag.
- **A person Reply holds under another address is not found by this one.** The lookup compares the address the
  work item carries, exactly; somebody who wrote from a second address is a second contact at Reply. The opt-out is
  recorded on the contact for this address — made for it if need be — and the other is untouched: record the stop
  for that address as well. A stop finds nobody under this address and answers `already_stopped`, while the
  participation under the other one stays; `enrollment.list` shows who the sequence still holds.
- **A pin Reply no longer holds reads as a completed stop.** Reply answers a participation read for a contact or a
  sequence it no longer holds with the same 404 as for a person who takes no part, so `enrollment.stop` cannot tell
  them apart and answers `already_stopped`. A campaign the work item names for the first time is read before
  anything else, so an identifier Reply does not hold is `campaign_not_found` and is never pinned. The reason for a
  stop is Jason's record; Reply holds none.
- **The count and the start are two calls.** `campaign.activate` reads Reply's count, compares it with the
  population the person approved, and then starts the sequence. Somebody enrolled in between is a race Reply offers
  no way to close: the start is not conditional on a count.
- **`campaign.enroll` waits for a person before it runs.** That operation's contract asks for an
  approval, so the claim parks the item as `awaiting_approval` with a record of exactly what would be
  enrolled, and nothing reaches Reply until somebody answers — `jason approval list`, then
  `jason approval approve`. Editing the work after the decision parks it again rather than sending
  something nobody approved. [docs/work-execution.md](../../docs/work-execution.md) §Approvals is the
  contract; the operation can also be driven through the plugin host by hand, which
  [docs/plugins.md](../../docs/plugins.md) §7 and §12 say how to do.
- **`campaign.get` answers no counts.** `counts` is `{}`, which is the contract's way of saying the
  provider reports none: a Reply sequence carries no people counts of its own, and the one count Reply
  publishes beside it — `GET /v3/sequences/{id}/contacts/count`, which `enrollment.list` reads for its
  `total` — counts everybody at once rather than by state. An empty object is a different fact from a zero,
  and the alternative was paging every contact in the sequence inside the operation's own budget.
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
- **Whether somebody is already on a list is read from the list, and only a page of it.** Reply publishes
  a read from the person's side — `GET /v3/contacts/{id}/lists` — and against a live account it answered
  with an empty array for a contact that was provably on the list, checked twice a minute apart, for a
  list that was not shared. So `list_membership.add` does not use it: the recovery read is
  `POST /v3/contacts/filter` scoped by `listId`, which answered for that same list at once, and the person
  is on it when Reply's own identifier for them is among the people it returns. Two consequences follow.
  Nothing about the person travels to ask — only two numbers — and a match is exact rather than a
  resemblance. But the read is over the list, and its largest page holds a thousand: on a longer list a
  person who is not in that page cannot be told from a person who is not on the list, so the attempt
  writes a `warn` line saying the reading could not be finished and makes the add anyway. The answer is
  then `added`, which is what that attempt did. Adding somebody who was already there left exactly one
  membership at the live account, so nothing is done twice — but that is recorded rather than relied on:
  it is not published anywhere, and the reason the add is made is that this operation's own contract costs
  nothing and its duplicate is reversible.
- **A hand-driven invocation gets no recovery read.** The repeat path is reached by
  `context.attempt_number`, which the runtime raises; an invocation made by hand through the plugin host
  ([docs/plugins.md](../../docs/plugins.md) §12) that leaves it out is a first attempt, and a first attempt
  writes. Say `"attempt_number": 2` in the envelope to exercise the reading.
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
  reported as `provider_call_failed` with Reply's own word in the trace, rather than mapped to a code
  nobody published.

- **The reads page by `limit` and `offset`, never by a cursor**, because Reply pages by `top` and `skip`. A
  page may come back shorter than `limit` with `has_more: true`: the plugin stops before the result would
  pass 768 KiB, counted as the runtime counts a result, so the next page starts at `offset` plus what came
  back. That is the documents' own paging rule, and nothing about it is Reply's.
- **`enrollment.list` reads from the campaign's end only.** "Where is this person across everything?" is
  the same read from the person's end, and this version does not serve it. Nor does it filter by status:
  every person carries one, and a page filtered after reading could not say where the next one starts.
- **`engagement.summarize` returns counts, never a rate, and only the counts the document names.** Reply
  sends percentages beside its counts without saying what they divide by, so a rate is left to whoever
  holds two counts and knows the denominator. Reply also reports counts no document names yet — not
  reached, connection notes, regular messages, likes, follows, endorsements, comments — and those are not
  returned in this version.
- **`conversation.list` takes no unread-only filter and reads no classification.** Every thread says
  whether it is unread, and the order is the provider's, the one that moved last first; what needs
  answering first is a judgement this read does not make, and Reply's categories travel under `vendor`
  untranslated.
- **A thread whose whole history is longer than `Plugins:Exec:OutputBytes` cannot be read at all.** Reply
  sends the history with the thread, so `conversation.get` fails `provider_answer_too_large` for it however
  small a page it asks for.
- **The package asks for 256 MiB of memory.** The budget counts what an invocation allocates, and a read
  of a thread reads two answers that can each be as large as the exec capture. 256 MiB holds two answers
  up to about twice the default capture of 4 MiB; a larger `Plugins:Exec:OutputBytes` lets a longer answer
  arrive and then runs a thread read out of memory. It is under the installation's default ceiling of
  512 MiB (`Plugins:Limits:MaxMemoryMb`). An installation that lowers that ceiling below 256 has a reload
  that loads this package refused as `field_invalid` — the whole reload, so every package stays on the
  previous snapshot until the ceiling is raised.

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
