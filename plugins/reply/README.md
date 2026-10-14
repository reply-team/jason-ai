# The Reply plugin

Jason's canonical provider operations, performed against a [Reply](https://reply.io) account through the
Reply CLI.

## What it does

Eleven operations — the whole set this build publishes: six that write and five that only read.

| Operation | What it is at Reply |
|---|---|
| `campaign.get` | Reads one sequence: its name, its state, and Reply's own words for both. |
| `list_membership.add` | Puts one person on one of the account's contact lists. A person Reply holds as opted out is refused. |
| `campaign.enroll` | Puts one person into a sequence. Into a live sequence, that is a send. A person already taking part is never moved or added again. |
| `enrollment.list` | Reads one page of the people a sequence holds, and Reply's count of them. No address is returned. |
| `engagement.summarize` | Reads a sequence's counts over one window, a row per channel. Counts only: no rate is returned. |
| `conversation.list` | Reads one page of inbox threads: one person's across the account, or one sequence's. A person is their identifier and the name Reply shows, never an address. |
| `conversation.get` | Reads one page of one thread's messages, the first sent first: who wrote each, to whom, and the body as Reply holds it. |
| `campaign.pause` | Pauses one sequence: the incident response. A paused sequence stops sending; pausing one already paused changes nothing. |
| `campaign.activate` | Starts one sequence sending, or resumes a paused one, for the population a person approved. A different count starts nothing. |
| `optout.record` | Records that somebody said stop: sets Reply's opt-out flag on the contact it holds under the address, made only when it holds nobody. What they said stays Jason's record. |
| `enrollment.stop` | Takes one person out of a sequence, once a person approves it. Nobody is created to do it, and the reason stays Jason's record. |

A campaign is a **sequence** at Reply and a person is a **contact**. Each operation runs the Reply CLI command made
for it — `reply sequence add-contact`, `reply inbox list` and the rest, listed under
[Which command it was](#which-command-it-was) — and translates: the operation's input into the command's arguments,
the command's result into the operation's answer, and the CLI's error code into the operation's failure. Which of
Reply's endpoints to call, in what order, and how to tell its answers apart is the CLI's to know.

**The CLI owns the credential, and Jason never sees it.** Signing in is something you do in your own
shell, and what it leaves behind stays in the CLI's own profile store: this package declares no
environment variable, is granted no key, passes none on a command line and reads none. Nothing about a
person reaches an argument either — a person's details travel on stdin — so what `~/.jason/logs/` records
of a call is the command line, which carries only Reply's identifiers and flags, the exit code and how long it took.

## What you need

- **The Reply CLI, installed and signed in.** It is a Node program installed with npm, on Node 20 or
  newer; [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) is where it comes from and
  what documents it. Running `reply auth login` is yours to do, as the person whose account it is.
- **Version 0.11.0 or newer.** The manifest asks for that, and a reload runs `reply --version` and reads
  the answer. An older CLI makes this plugin `unavailable` with `executable_incompatible`, and work
  routed to it fails at the claim with `plugin_unavailable` rather than running commands an older CLI
  does not have, or answers differently.
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

**And three things that are not true yet.** There is **no install verb**: installing is copying a
directory, and nothing reads it until a reload. There is **no signing and no publisher identity**: the
digest in `plugin list` says what the files are, never who wrote them, so a package is exactly as trusted
as the machine you copied it onto. There is **no update awareness**: nothing checks whether a newer
version exists, and because the id is the directory name there is **one version per id** — upgrading is
replacing the directory and reloading.

## The account a route names

The manifest declares a binding of three optional fields, and the schema is closed:

| Field | What it selects |
|---|---|
| `profile` | Which of the CLI's stored profiles the calls act as — one of those `reply profile list` shows, each with its own credential. |
| `team_id` | Which team the account acts in, for a credential that spans more than one. |
| `derive_first_name` | Whether a person with no first name whom Reply does not hold is imported under a derived one. Absent means yes. |

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

**A person without a first name.** Reply requires a first name for every contact it holds, and Jason does not.
Making one fit the other is this package's job. For `list_membership.add`, `campaign.enroll` and `optout.record`, a
person with no pin and no first name is looked up by address first, exactly and whatever its case. A contact Reply
already holds is worked by its identifier, and nothing is written to its record. When Reply holds nobody under the
address, the person is imported under a first name derived from their last name, which is then not sent as a last
name too, or else from the text of their address before the `@`, exactly as it is spelled. With neither, the work
fails `provider_call_failed`: "a first name could not be derived". A first name of nothing but white space counts as
none. The derived name is what Reply holds from then on, and what a sequence's first-name variable puts in front of
the person. An approval shows the person as Jason holds them, without it. If that is not acceptable for an account,
set `"derive_first_name": false` in the route's binding. Such a person is then refused by a list add or an
enrolment, "the route forbids deriving a first name", and is not created at Reply. The switch does not apply to an
opt-out: it is protective, greets nobody, and derives a first name as above whatever the binding says, because
refusing it would leave the person unflagged at Reply. Which field a name was derived from, `last_name` or
`email_local_part`, is written to the invocation's diagnostics, never the name. `enrollment.stop` creates nobody and
derives nothing.

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
variable this package sets is `REPLY_NO_UPDATE_CHECK=1`, on the CLI it starts. reply-cli 0.11.0 looks for
a newer release only under `--version`, which this package never runs, so the variable only keeps a later
release from spending an operation's budget on a version check nobody asked for.

## What can go wrong

Every failure carries a code the operation's own contract publishes and a class the runtime acts on:
`transient` is retried inside the work item's budget, `permanent` and `validation` are final, and
`ambiguous` stops the item for a person, because Reply may already have acted. The tables below are
[`modules/errors.js`](modules/errors.js), which is their single source — a test reads that file and holds
every code in it to the operation's published contract, the same code under the same class.

A failure's message is all of it that reaches the work item's last error. What it carries beyond its code, class and
message — the CLI's own error code, Reply's status, code and sentence where the CLI passed them on, the wait Reply
asked for, the package's own reason, the numbers a decision was about — is in the failed attempt's trace
([docs/plugins.md](../../docs/plugins.md) §5, "Failing"): `jason workitem get <id>` prints it as
`attempts[].error.trace` in its JSON, the default, and the `--human` view does not show it. The tables below say "the
trace" for it.

### Which command it was

Whether a lost answer is cheap or expensive is decided by the command it was lost on, so every command this
package runs is marked once. Every command line starts `reply --json -q`, then `--profile` and `--team-id` where the
route's binding names them.

| Command | Mark | What it is for |
|---|---|---|
| `sequence get <sequence>` | read | The sequence as Reply holds it: what `campaign.get` answers from, and the read `enrollment.stop` makes of a campaign it names for the first time. |
| `sequence contacts <sequence> --limit --offset` | read | One page of the people a sequence holds, and Reply's count of them: what `enrollment.list` answers from. |
| `sequence stats <sequence> --preset`, or `--from --to` | read | A sequence's counts over one window: what `engagement.summarize` answers from. |
| `inbox list --contact-id` or `--sequence-id`, `--source --since --limit --offset` | read | One page of one of Reply's thread buckets, by person or by sequence: what `conversation.list` answers from — the inbox alone for `inbound`, the inbox and the sent threads merged for `all`. |
| `inbox get <thread> --limit --offset` | read | One thread, its subject, and one page of its messages: what `conversation.get` answers from. |
| `api /v3/contacts/filter --method POST --body -` | read | Reply's contact search by address: the lookup `list_membership.add`, `campaign.enroll` and `optout.record` make for a person with no pin and no first name. A `POST`, and a read all the same. |
| `sequence add-contact <sequence>` with `--contact-id` or `--contact -`, `[--start-step] [--ignore-step-delay]` | write | Puts the person into the sequence, importing them first when they are given by their details. Into a live sequence, a send. |
| `sequence remove-contact <sequence>` with `--contact-id` or `--contact -` | write | Takes the person out of the sequence; their history stays. It never creates anybody. |
| `sequence pause <sequence>` | write | Pauses the sequence. |
| `sequence start <sequence> --expect-contacts <n>` | write | Starts the sequence, or resumes a paused one, only while it holds the population a person approved. Step 1 goes to everybody enrolled, so it is a send. |
| `contact-list add-contact <list>` with `--contact-id` or `--contact -`, `--refuse-opted-out` | write | Puts the person on the list, importing them first when they are given by their details, and refuses a person Reply holds as opted out. |
| `contact opt-out` with `--contact-id` or `--contact -` | write | Sets Reply's opt-out flag for the person, creating them only when Reply holds nobody under the address. This package never clears it. |

### Endings every operation can meet

| When | Code | Class | What to do about it |
|---|---|---|---|
| The CLI could not be started at all, so nothing was sent. | `provider_call_failed` | permanent | Install the CLI where the runtime can find it, check that the grant names it, reload. |
| The CLI refused the call before making it: its own usage error. | `provider_call_failed` | permanent | Nothing reached the account and a repeat builds the same call. Report it: either this package or the CLI's surface has moved. |
| Reply could not be reached, or a read failed after the CLI's own retries (`reply.unavailable`); or a **read** timed out, exited without an error the CLI explained, or printed something that is not a result; or a read made as part of a write — the contact search before a list add or an enrolment, the sequence read before a stop — was cut off at `Plugins:Exec:OutputBytes`. | `provider_unavailable` | transient | Nothing. Nothing changed at Reply — the CLI never resends a write — so the attempt is repeated. |
| Reply is still activating the sequence (`sequence.busy`). | `provider_unavailable` | transient | Nothing. The attempt is repeated once Reply has had time to finish. |
| A **write**'s outcome is unknown: the CLI answered `outcome.unknown`, or the write timed out, exited without an error the CLI explained, printed something that is not a result, or had its answer cut off at `Plugins:Exec:OutputBytes`. | `provider_answer_lost` | ambiguous | Where the operation's contract says a repeat is safe, the runtime repeats it; otherwise the item stops for a person. Look at the account — the contact, the list, the sequence — and decide; a blind repeat can create somebody twice or send to them twice. Whatever the attempt had learned, the CLI's own identifiers included, travels with the failure, so there are identifiers to look by. |
| The profile is not signed in, Reply refused the credential (`access.denied`), or Reply says the account lacks a feature where the operation has no code of its own for that (`access.feature_unavailable`). | `unauthorized` | permanent | Sign that profile in again, or issue a key carrying the scope the call needs. |
| Reply asked to wait longer than the CLI waits on its own (`rate_limited`). | `rate_limited` | transient | Nothing. The runtime waits and repeats the attempt; the wait Reply asked for is in the trace. |
| Reply refused the call (`reply.refused`), refused a field no row below names (`reply.invalid_request`), or the CLI answered a code this version does not know. | `provider_call_failed` | permanent | The CLI's code, Reply's own code and its sentence are in the trace. A refusal that recurs belongs in the table. |

A call that runs out of time is the package's to answer, so each call is given what is left of the invocation's
budget less five seconds (never more than half of what is left) rather than all of it. A call given everything
runs out at the moment the host's own engine ends the invocation, and on a busy machine the host's work after
a cut-off call — ending the CLI's process tree, waiting for its pipes — can outlast what the engine has left;
the host then answers `plugin_timeout`, and the identifiers the attempt had learned do not travel with it.
A call cut off sooner is one the package hears about, and it answers from the two timeout rows above.

### `campaign.get`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). | `campaign_not_found` | permanent | Correct the identifier the work item carries. A link the provider has denied is not recorded, so there is nothing to undo. |
| Reply's answer was longer than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. | `provider_answer_too_large` | permanent | Raise `Plugins:Exec:OutputBytes`. The same read would be cut at the same place, so it is not repeated. |

This operation is one read and nothing else, so the ambiguous ending above cannot arise for it.

### `enrollment.list`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| Reply says the account's plan or role may not read a sequence's people (`access.feature_unavailable`). | `feature_unavailable` | permanent | A role or a plan that includes it. Signing in again changes nothing. |
| Reply's answer was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program, so it was cut off. | `provider_answer_too_large` | permanent | Ask for a smaller `limit`, or raise `Plugins:Exec:OutputBytes`. |

When Reply would not count the sequence's people, the page is still answered and `total` is null.

### `engagement.summarize`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). | `campaign_not_found` | permanent | Correct the identifier. |
| Reply says the account's plan or role may not read a sequence's statistics (`access.feature_unavailable`). | `feature_unavailable` | permanent | A role or a plan that includes it. |
| The window is refused: one that does not end after it starts, which the CLI refuses before anything is sent, or one Reply does not accept (`reply.invalid_request`). | `window_invalid` | validation | Correct `args.window`; the same window is refused every time. |
| Reply's answer was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program. | `provider_answer_too_large` | permanent | Raise `Plugins:Exec:OutputBytes`. |

### `conversation.list`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The work item names a person and no Reply contact identifier is pinned for them. Nothing is called. | `contact_not_linked` | permanent | A write that ensures the contact — `list_membership.add` or `campaign.enroll` — is what links them. A read creates nobody. |
| The pinned contact identifier, or the campaign's, is not a Reply number at all. Nothing is called. | `provider_call_failed` | permanent | The pin is wrong; the trace says which. |
| `source: all` asks for a page past the first 1000 threads: `offset` plus `limit` over 1000. Nothing is called. | `provider_call_failed` | permanent | Ask for `source: inbound`, which has no such bound, or for a page within the first 1000. |
| The inbox is not available to this user or team (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes the inbox. |
| Reply's answer was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program. | `provider_answer_too_large` | permanent | Ask for a smaller `limit`, or raise `Plugins:Exec:OutputBytes`. |

A person or a sequence with no threads is an empty page, not a failure. A contact identifier Reply does not hold is
refused by Reply as an invalid request, so it arrives as `provider_call_failed`.

### `conversation.get`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply thread number, or Reply holds no thread under it that this account may see (`inbox.thread_not_found`). | `conversation_not_found` | permanent | Read the thread by an identifier `conversation.list` returned. |
| The inbox is not available to this user or team (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes the inbox. |
| The page of messages was longer than `Plugins:Exec:OutputBytes` lets a plugin read from a program. | `provider_answer_too_large` | permanent | Ask for a smaller `limit`, or raise `Plugins:Exec:OutputBytes`. |

### `list_membership.add`

| When | Code | Class | What to do about it |
|---|---|---|---|
| `args.list.external_id` is not a Reply list number, or the account holds no such list (`contact-list.not_found`). | `list_not_found` | permanent | Correct the identifier. Nothing about repeating the call can change it. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there (`contact.not_found`). | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again, and this version has no repair path for one. |
| Reply's own opt-out register holds this person (`contact.opted_out`). The package always asks the CLI to refuse such a person. | `suppressed` | permanent | Nothing, on purpose. Only the person themselves can ask to be reinstated. |
| Reply will not accept the address on the channel the call named (`contact.invalid_email`, or `reply.invalid_request` naming the email), or the person has no value on that channel. | `invalid_channel_value` | validation | Correct the contact's data; no later attempt would be accepted either. |
| The account will hold no further contact (`account.contact_limit_reached`). | `limit_reached` | permanent | Free capacity at Reply. A **list's** own capacity publishes no code of its own, so a full list arrives as `provider_call_failed` instead of this one. |
| The person has no pin, no first name and no last name, Reply holds nobody under the address, and the address has no text before its `@`. Nothing is added. | `provider_call_failed` | permanent | "A first name could not be derived." Give the person a first name in Jason and propose the work again: Reply creates nobody without one. |
| The person has no pin and no first name, Reply holds nobody under the address, and the route's binding says `derive_first_name: false`. Nothing is added. | `provider_call_failed` | permanent | "The route forbids deriving a first name." Give the person a first name in Jason, or allow the derivation in the route's binding, and propose the work again. |
| The person has no pin and no first name, and Reply holds more than one contact whose address is theirs. Nothing is added. | `provider_call_failed` | permanent | Resolve the duplicate at Reply, then propose the work again. |
| The person has no pin and no first name, and the lookup read five pages of a hundred and Reply said there were more. Nothing is added. | `provider_call_failed` | permanent | Give the person a first name in Jason, or a pin, and propose the work again. |

Reply keeps one membership however often a person is added and does not say whether they were on the list already,
so the answer is `added` either way, and a repeat after a lost answer runs the command again.

### `campaign.enroll`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). | `campaign_not_found` | permanent | Correct the identifier. Nobody is ensured for a campaign that cannot exist. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there (`contact.not_found`). | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again. |
| The sequence is archived (`sequence.archived`), or holds no step to send (`sequence.has_no_steps`). | `campaign_not_enrollable` | permanent | Un-archive it at Reply, or give it a step. The item fails rather than waiting for either. |
| `collision: refuse` was asked for, this is a first attempt, and the person already takes part. Nothing is written. | `collision_refused` | permanent | Choose another collision policy if a second participation is what was meant. A repeated attempt answers differently — see below. |
| Reply refused to add a person its opt-out register holds (`contact.opted_out`). | `suppressed` | permanent | Nothing, on purpose. |
| Reply will not accept the address on the channel the call named (`contact.invalid_email`, or `reply.invalid_request` naming the email), or the person has no value on that channel. | `invalid_channel_value` | validation | Correct the contact's data. |
| The account will hold no further contact (`account.contact_limit_reached`). | `limit_reached` | permanent | Free capacity at Reply. A **sequence's** own capacity publishes no code, so it arrives as `provider_call_failed`. |
| The start position names no single step of this sequence's chain (`sequence.step_not_found`), or a step number past the 10000 the CLI takes (`usage.range`), or the first touch is a value this version has no word for. Nothing is sent. | `provider_call_failed` | permanent | The trace says which reading failed. Ask for the first step, or name a position the sequence really has. |
| The person has no pin and no first name, and Reply holds more than one contact under the address, or more than the lookup reads; or Reply holds nobody and no first name could be derived, or the route's binding forbids deriving one. Nothing is enrolled. | `provider_call_failed` | permanent | As for `list_membership.add` above: a first name, a pin, the derivation allowed, or the duplicate resolved at Reply. |

The CLI reads whether the person already takes part before it adds anybody, so a person already in the sequence is
never moved or added again, and nothing is written for them. The package answers that `already_enrolled` under
`collision: skip`, and on any repeated attempt whatever the policy, because after a lost answer the participation
found may be the one this work item made. `campaign_live` says whether the sequence was live, which is whether the
enrolment was a send. Reply itself refuses a person its opt-out register holds, so the package reads no register
first.

### `campaign.pause`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| The sequence is archived (`sequence.archived`), or Reply refused the pause (`sequence.not_pausable`) — a sequence that never started is refused. | `campaign_not_pausable` | permanent | Nothing to pause. The trace says which, with Reply's own code where it gave one. |

A sequence already paused answers `paused` again: Reply documents the pause as idempotent, so the ambiguous ending
above is repeated rather than stopped for a person. A pause runs even while its Jason campaign is paused, because the
provider's sequence must still stop after somebody paused Jason's first.

### `campaign.activate`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| The sequence is archived (`sequence.archived`), or Reply will not start it (`sequence.not_startable`): no contacts, no connected sending account or no sending hours. | `campaign_not_activatable` | permanent | Change at Reply what the trace names — Reply's own code, where it gave one — then propose the activation again. |
| The sequence's count at execution is not the population the person approved (`sequence.contact_count_changed`). Nothing starts. | `population_changed` | permanent | Read the count again and propose a new activation: the trace carries both numbers. |
| The sequence is live already and Reply would not count its people, so the answer would have no `enrolled`. Nothing was started. | `provider_unavailable` | transient | Nothing. The attempt is repeated. |
| The start happened from a state other than draft or paused, which `was` has no word for. | `provider_call_failed` | permanent | The sequence was started. Reply's word for the state it left is in the trace; check the sequence at Reply. |

The CLI reads the sequence and its count first, and starts nothing unless the count is the population the person
approved. `was` is the state the start left — draft or paused — which the CLI reads before it starts, because Reply's
own answer to a start describes the sequence after it. A start whose answer was lost may already be sending, so the
next attempt meets a live sequence and answers `already_live` rather than starting twice. A person approves every
activation: it waits for them before it runs, as `campaign.enroll` does.

### `optout.record`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The pinned contact identifier is not a Reply contact number, or no longer resolves there (`contact.not_found`). | `contact_not_found` | permanent | The pin has to be cleared before the opt-out can be recorded at Reply. |
| More than one contact's address is this one, ignoring case (`contact.not_unique`, or this package's own lookup for a person with no first name). Nothing is set. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the opt-out again. |
| More than five hundred contacts hold the address, so the lookup could not be read to its end (`contact.lookup_incomplete`, or this package's own lookup for a person with no first name). Nothing is made and nothing is set. | `provider_call_failed` | permanent | Record the opt-out at Reply by hand: a repeat would read the same pages. Where `jason suppression add` recorded the address, Jason's own register already stops Jason's own reach. |
| The person has no pin, no first name and no last name, Reply holds nobody under the address, and the address has no text before its `@`. Nothing is made and nothing is set. | `provider_call_failed` | permanent | "A first name could not be derived." Reply holds nobody under this address, so it cannot reach them there either. Where `jason suppression add` recorded the address, Jason's own register already stops Jason's own reach. |
| Reply will not accept the address, so no contact could be made to record the opt-out on (`contact.invalid_email`, or `reply.invalid_request` naming the email). | `invalid_channel_value` | validation | Correct the contact's data. |
| Reply held nobody under the address, and the account will hold no further contact (`account.contact_limit_reached`). | `limit_reached` | permanent | Free capacity at Reply. Reply never held them under this address, so it cannot reach them there either. |

The CLI finds the person before anything is written: by the pin, or by the address, compared exactly and whatever the
case. Only when Reply holds nobody under it is a contact made, and then the flag is set. Setting a flag that is set
already changes nothing, so the answer is `recorded` either way, and a repeat after a lost answer runs the command
again. The opt-out runs without anybody approving it — not running is the harm — and it runs even while its Jason
campaign is paused. Jason's own register is the other half: `jason suppression add` stops Jason's reach at once for
that address, and is written first.

### `enrollment.stop`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). A campaign the work item names for the first time is read before anything else when the person is named by address; for a pinned person the CLI tells a missing sequence apart itself. | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| The pinned contact identifier is not a Reply contact number at all. Nothing is called. | `contact_not_found` | permanent | The pin is wrong and has to be cleared. A well-formed pin Reply no longer holds is not this: it reads `already_stopped`. |
| More than one contact's address is this one, ignoring case (`contact.not_unique`). Nobody is taken out. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the stop again. |
| More than five hundred contacts hold the address, so the lookup could not be read to its end (`contact.lookup_incomplete`). Nobody is taken out. | `provider_call_failed` | permanent | Take the person out at Reply by hand, then check with `enrollment.list`: a repeat would read the same pages. |
| The person carries no value on the channel the call named. Nothing is called. | `invalid_channel_value` | validation | Name a channel they have. |

The CLI finds the person without creating anybody: by the pin, or by the address, as `optout.record` does. A person
Reply does not hold takes part in nothing, so the stop answers `already_stopped`. Otherwise the CLI reads the person's
part in the sequence — a person who takes no part, or whose part is finished, is already stopped and nothing is
removed — and only then takes them out, never marking them finished. That read is also what makes a lost answer safe
to repeat. The CLI answers a person Reply does not hold without checking the sequence, so when the person is named by address,
a campaign the work item names for the first time is read first, and a mistyped identifier is `campaign_not_found`
rather than a completed stop. A pinned person needs no such read: the CLI tells a missing sequence apart itself.
Reply's own opt-out is never read: a stop is for exactly the people it would refuse. A person approves every stop
before it runs, and its work waits while its Jason campaign is paused.

## What it cannot do

- **An enrolment never moves or re-adds a person already in the sequence, and that guard is the CLI's.** Reply's own
  add, given a contact who already takes part, moves them forward to a later start step or refuses a re-add with a
  generic error. So the CLI reads the person's part in the sequence before it adds anybody, and answers that they
  take part with nothing written. A participation that appears between that read and the add is the one race left;
  Reply then does what is described above.
- **Three answers the contracts publish are never given.** `list_membership.add` never answers `already_member`,
  `optout.record` never answers `already_recorded`, and `campaign.pause` never answers `already_paused`. Reply does
  not say whether a person was on the list already, a flag set again changes nothing, and a paused sequence paused
  again is answered as a pause. The effect at Reply is the same either way, so each answers `added`, `recorded` or
  `paused`.
- **A person without a first name.** Reply requires a first name; see *A person without a first name* under *The
  account a route names* above.
- **`conversation.list` with `source: all` reads Reply's inbox and its sent threads, and merges them.** Reply's thread
  filter has no value for "every thread": its inbox holds the threads with an inbound message, and `sent` the threads
  whose latest message is outbound. The package reads both from the first thread, a hundred at a time, merges them by
  last activity and lists a thread found in both once, so a page with `source: all` can reach at most the first 1000
  threads of each (`offset + limit` up to 1000); a page further in is refused rather than guessed at.
  `source: inbound` is one read and has no such bound.
- **An opt-out at Reply is a yes or a no.** What they said, where it arrived and when are Jason's record, carried
  by the work item; none of it reaches Reply, whose contact holds only its opt-out flag.
- **A person Reply holds under another address is not found by this one.** The lookup compares the address the
  work item carries, exactly; somebody who wrote from a second address is a second contact at Reply. The opt-out is
  recorded on the contact for this address — made for it if need be — and the other is untouched: record the stop
  for that address as well. A stop finds nobody under this address and answers `already_stopped`, while the
  participation under the other one stays; `enrollment.list` shows who the sequence still holds.
- **A contact pin Reply no longer holds reads as a completed stop.** Reply answers a participation read for a contact
  it no longer holds with the same answer as for a person who takes no part, so `enrollment.stop` answers
  `already_stopped`. A sequence Reply no longer holds is told apart: the CLI reads it, and the stop fails
  `campaign_not_found`. The reason for a stop is Jason's record; Reply holds none.
- **The count and the start are two calls.** The CLI reads Reply's count, compares it with the population the person
  approved, and then starts the sequence. Somebody enrolled in between is a race Reply offers no way to close: the
  start is not conditional on a count.
- **A start from a state with no word.** `was` names the state the start left: draft or paused. Reply has a rare
  third state, `skipped`, from which a start also succeeds; the package then fails `provider_call_failed` with
  Reply's word in the trace, so a person sees that the sequence was started though the answer could not say from what.
- **`campaign.enroll` waits for a person before it runs.** That operation's contract asks for an
  approval, so the claim parks the item as `awaiting_approval` with a record of exactly what would be
  enrolled, and nothing reaches Reply until somebody answers — `jason approval list`, then
  `jason approval approve`. Editing the work after the decision parks it again rather than sending
  something nobody approved. The approval belongs to the operation's contract, not to this package; the
  operation can also be driven through the plugin host by hand, which
  [docs/plugins.md](../../docs/plugins.md) §7 and §12 say how to do.
- **`campaign.get` answers no counts.** `counts` is `{}`, which is the contract's way of saying the
  provider reports none: a Reply sequence carries no people counts of its own, and the one count Reply
  publishes beside it — which `enrollment.list` reads for its `total` — counts everybody at once rather than by
  state. An empty object is a different fact from a zero, and the alternative was paging every contact in the
  sequence inside the operation's own budget.
- **A campaign name longer than 500 characters is truncated.** That is what the contract allows a name
  and Reply agrees to no limit, so the neutral half carries what it can hold and Reply's whole name
  survives beside it under `vendor.name`. Passing the longer one through unchanged would make the answer
  invalid and cost the read of that campaign for good, over a display string.
- **`immediately` and `next_open_window` are the same request.** Reply always sends inside the sequence's
  own schedule and publishes no way to bypass a sending window, so both first touches ask the CLI to ignore the
  first step's own delay. `authored_delay` is the one that differs: it leaves the step's own delay alone.
- **Reply has no idempotency key.** A repeat after a lost answer runs the command again, and the command's own
  reading — the person's part in the sequence before an enrolment, the sequence's state and count before a start — is
  the nearest answer the provider can give. It cannot tell this work item's own earlier write from somebody else's
  addition of the same person an hour ago.
- **A repeat with `collision: refuse` against an existing participation answers `already_enrolled`, not
  `collision_refused`.** After a lost answer, the participation the CLI finds may well be the one this work item
  made, so a repeated attempt answers from it whatever the policy; `collision_refused` is what a first attempt
  reports. The attempt number decides it, and an invocation made by hand through the plugin host
  ([docs/plugins.md](../../docs/plugins.md) §12) that leaves `context.attempt_number` out is a first attempt: say
  `"attempt_number": 2` in the envelope to exercise the repeat.
- **A person who opts out between two attempts.** A repeated `campaign.enroll` answers `already_enrolled`: the CLI
  reads the participation before Reply's register is consulted, so work that already happened is reported as having
  happened. A repeated `list_membership.add` fails `suppressed`: the CLI reads the register before it adds, as this
  package always asks it to, and the membership the first attempt made stays.
- **`start.position: "step"` works only for a sequence whose steps form a single chain.** Reply publishes
  no ordering field on a step at all — the steps are a graph of parents, and a branch is labelled `"2A"` —
  so the CLI walks the chain from the root and takes the Nth link; a condition step in the way, a fork, or an N past
  the end is refused, because taking the Nth element of the array instead would be a guess whose cost is a real
  person receiving the wrong message.
- **List, sequence and contact identifiers have to be Reply's own numbers.** Anything else names nothing
  the account could hold and is refused without a call being made. The published contract illustrates
  `list_membership.add` with `"external_id": "L-1129"` — that is an example of the argument's shape, and
  this plugin refuses it.
- **`time_zone` is not sent.** The person the CLI reads carries an email, names, a company and a title. Reply's own
  time-zone field accepts a vocabulary published nowhere, and an unrecognised value fails the whole import.
- **The reads page by `limit` and `offset`, never by a cursor**, as the CLI does. A page may come back shorter than
  `limit` with `has_more: true`: the plugin stops before the result would pass 768 KiB, counted as the runtime counts
  a result, so the next page starts at `offset` plus what came back. That is the documents' own paging rule, and
  nothing about it is Reply's.
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
- **Only a contact's first reply is read.** Reply marks a contact finished in its sequence at its first
  reply and stops detecting that contact's later replies, so `conversation.list` never lists them and
  Jason's inbound sync says a contact's first reply only.
- **The package asks for 256 MiB of memory.** The budget counts what an invocation allocates, every answer it reads
  included, and an answer can be as large as the exec capture (`Plugins:Exec:OutputBytes`, 4 MiB by default). It is
  under the installation's default ceiling of 512 MiB (`Plugins:Limits:MaxMemoryMb`). An installation that lowers
  that ceiling below 256 has a reload that loads this package refused as `field_invalid` — the whole reload, so every
  package stays on the previous snapshot until the ceiling is raised.

## Versioning

This package carries its own version and its own [changelog](CHANGELOG.md), and versions independently of
the runtime: SemVer, with the manifest's `contracts` block saying which operation and protocol families it
speaks. A change at Reply's end — a new field, a moved endpoint — is absorbed by the CLI; a refusal this table has no
word for, or a command this package does not run yet, is absorbed by releasing the package, with no runtime release
involved.

## Where to look next

- [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) — the commands themselves: `reply <noun> <verb>
  --help` prints each one's flags, its result and the error codes it can answer.
- [docs/plugins.md](../../docs/plugins.md) — the mechanism: the manifest and every rule it is held to, the
  Host SDK and its limits, capabilities and grants, the invocation protocol, testing a package locally,
  and what this version of the mechanism cannot do.
- [docs/contracts/](../../docs/contracts/README.md) — the operations themselves: one document each, with
  the schemas, the published fixtures and the conformance list a plugin has to demonstrate.
- `jason route resolve --campaign <id> --operation <name> --human` — which plugin and which account a claim
  would use for a campaign's operation, and why; a claim refuses provider work with its reason's own code,
  `no_route` when nothing routes it.
