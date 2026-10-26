# The Reply plugin

Jason's canonical provider operations, performed against a [Reply](https://reply.io) account through the
Reply CLI.

## What it does

Twenty-two operations — the whole set this build publishes: fifteen that write and seven that only read.

| Operation | What it is at Reply |
|---|---|
| `campaign.get` | Reads one sequence: its name, its state, and Reply's own words for both. |
| `campaign.create` | Makes the sequence a Jason campaign works with: a draft that holds nobody, with the one step Reply needs it to have, a task for its owner. A name Reply holds already is refused. |
| `list_membership.add` | Puts one person on one of the account's contact lists. A person Reply holds as opted out is refused. |
| `campaign.enroll` | Puts one person into a sequence. Into a live sequence, that is a send. A person already taking part is never moved or added again. |
| `enrollment.list` | Reads one page of the people a sequence holds, and Reply's count of them. No address is returned. |
| `engagement.summarize` | Reads a sequence's counts over one window, a row per channel. Counts only: no rate is returned. |
| `conversation.list` | Reads one page of inbox threads: one person's across the account, or one sequence's, on one channel or under one of the account's categories where the work item asks. A person is their identifier and the name Reply shows, never an address. |
| `conversation.get` | Reads one page of one thread's messages, the first sent first: who wrote each, to whom, and the body as Reply holds it. |
| `campaign.pause` | Pauses one sequence: the incident response. A paused sequence stops sending; pausing one already paused changes nothing. |
| `campaign.activate` | Starts one sequence sending, or resumes a paused one, for the population a person approved. A different count starts nothing. |
| `optout.record` | Records that somebody said stop: sets Reply's opt-out flag on the contact it holds under the address, made only when it holds nobody. What they said stays Jason's record. |
| `enrollment.stop` | Takes one person out of a sequence, once a person approves it. Nobody is created to do it, and the reason stays Jason's record. |
| `conversation.reply` | Answers the person on an inbox thread, in that thread, once a person approves the text. Nothing is sent unless the thread is theirs and on the channel named; nobody else can be put on it. |
| `conversation.classify` | Files a thread under one of the account's inbox categories, by its identifier or its name, or takes it out of its category. Reply's Do not contact is refused: filing there opts the person out. |
| `conversation.close` | Marks a thread read: Reply has no closed state, and read is how its inbox shows a thread as worked. |
| `conversation.reopen` | Marks a thread unread again, so it needs attention. |
| `contact.update` | Changes a person's fields — first name, last name, company, title — and custom fields on the contact Reply holds for them, each custom field named as the account names it. A null clears a field; one left out stays. A person Reply holds nobody for is created with those fields, as an add creates one. |
| `contact.delete` | Deletes the contact Reply holds for a person, by the identifier Jason pinned, once a person approves it: they leave every sequence and list there. A person Reply holds as opted out is refused, and Jason's own record of the person and its register stay as they are. |
| `sender.list` | Reads the mailboxes and LinkedIn accounts a send can go from, and says whose each one is: the caller's own alone, or the whole team's when that is asked for. |
| `schedule.list` | Reads the account's sending schedules, ordered by Reply's identifier: each with its time zone, its windows by weekday as authored, the windows of later steps where Reply keeps them, and whether it is the caller's default. Reply holds a time zone as a Windows time zone identifier, and this package carries it as written under the scheme `windows`, always — never converted. No calendar is worked out from the windows. |
| `social_invitation.send` | Invites one person to connect on LinkedIn from one of the account's LinkedIn accounts, with a note or without one, once a person approves it. Nothing is sent unless the account is the one the person approving read, and Reply's contact for the person is reached at the profile the work item consumes and is not held as opted out. A person Reply holds nobody for is created first, as an add creates one. |
| `message.send` | Sends one person one message outside any sequence, once a person approves it: an email, or on LinkedIn a message to a connection, an InMail or a voice message Reply speaks from the text. Nothing is sent unless the account is the one the person approving read, and Reply's contact for the person is reached at the address or the profile the work item consumes and is not held as opted out. A person Reply holds nobody for is created first, as an add creates one. |

A campaign is a **sequence** at Reply and a person is a **contact**. Each operation runs the Reply CLI command made
for it — `reply contact find`, `reply sequence add-contact`, `reply inbox list` and the rest, listed under
[Which command it was](#which-command-it-was) — and translates: the operation's input into the command's arguments,
the command's result into the operation's answer, and the CLI's error code into the operation's failure. Which of
Reply's endpoints to call, in what order, and how to tell its answers apart is the CLI's to know, and the package
never makes the CLI's raw call, `reply api`: every request it makes has a command made for it.

**The CLI owns the credential, and Jason never sees it.** Signing in is something you do in your own
shell, and what it leaves behind stays in the CLI's own profile store: this package declares no
environment variable, is granted no key, passes none on a command line and reads none. Nothing about a
person reaches an argument either — a person's details travel on stdin, and so do the name a sequence is given,
the text of a reply, the note of an invitation and the subject and text of a message —
so what `~/.jason/logs/` records
of a call is the command line, which carries only Reply's identifiers and flags, the exit code and how long it took.

## What you need

- **The Reply CLI, installed and signed in.** It is a Node program installed with npm, on Node 20 or
  newer; [`reply-team/reply-cli`](https://github.com/reply-team/reply-cli) is where it comes from and
  what documents it. Running `reply auth login` is yours to do, as the person whose account it is.
- **Version 0.16.0 or newer.** The manifest asks for that, and a reload runs `reply --version` and reads
  the answer. 0.16.0 is the release with `reply schedule list`, which `schedule.list` answers from; 0.15.0 was the one
  whose direct sends and `reply contact update` take the person by their fields and import one Reply holds nobody
  for. An older CLI makes this plugin `unavailable` with `executable_incompatible`, and work
  routed to it fails at the claim with `plugin_unavailable` rather than running commands an older CLI
  does not have, or answers differently.
- **Jason 0.14.0 or newer.** The package keys a LinkedIn profile with the plugin SDK's `host.channelKey`, which a
  runtime older than 0.13.0 does not have; and 0.14.0 is the first release whose `message.send`,
  `social_invitation.send` and `contact.update` hand the package the person's own fields — their names, company, title
  and time zone — to create a person Reply holds nobody for. Given an input without those fields at all, which only an
  older runtime composes, the three refuse the work before anything is called, naming the fields.
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

**1. Install the package.** Every release of Jason from 0.11.0 on publishes it as `jason-plugin-reply.zip`,
listed in that release's `checksums.txt`. Download both from the release you run, check the package against
its line, and unpack it into `~/.jason/plugins/reply` only once it matches — the directory name is the plugin
id, so it has to be `reply`, and it must not be there yet; the runtime's first start makes `~/.jason/plugins`.
The lines, for macOS and Linux and for PowerShell, are step 3 of [the wiring check](../../docs/golden-path.md).
Where the release cannot be reached, or is older than 0.11.0 and does not carry the package, the tree
`jason skills install` fetched holds the same package, at the tag of the runtime you run, and copying it is the
same install:

```sh
cp -r ~/.jason/skills/staged/v<version>/plugins/reply ~/.jason/plugins/reply
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
Making one fit the other is this package's job, and only an import needs one: a person Reply already holds is found by
the exact lookup and worked by its identifier, and nothing is written to its record
([Contacts at Reply](#contacts-at-reply)). When Reply holds nobody under the address, the person is imported under a
first name derived from their last name, which is then not sent as a last name too, or else from the text of their
address before the `@`, exactly as it is spelled; a LinkedIn URL is no name. With neither, the work fails
`provider_call_failed`: "a first name could not be derived". A first name of nothing but white space counts as none.
The derived name is what Reply holds from then on, and what a sequence's first-name variable puts in front of the
person. An approval shows the person as Jason holds them, without it. If that is not acceptable for an account, set
`"derive_first_name": false` in the route's binding. Such a person is then refused by a list add or an enrolment, "the
route forbids deriving a first name", and is not created at Reply. The switch does not apply to an opt-out: it is
protective, greets nobody, and derives a first name as above whatever the binding says, because refusing it would leave
the person unflagged at Reply. Which field a name was derived from, `last_name` or `email_local_part`, is written to the
invocation's diagnostics, never the name. `enrollment.stop` creates nobody and derives nothing.

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

## Contacts at Reply

Every operation about a person finds Reply's own contact for them before it writes anything — the contact proxy rule
of [docs/plugins.md](../../docs/plugins.md) §7, and each operation's document says which way it finds them.

- **A pin is worked by.** Once Jason holds Reply's identifier for the person, every operation names them by it, and
  nobody is looked up by address again.
- **Without a pin, the exact lookup.** `reply contact find` asks Reply for the one contact under the value of the
  channel the operation consumes — an email address, matched whatever its case, or a LinkedIn URL — reads the answer
  to its end, and refuses two contacts under one address as `contact_not_unique` rather than guess between them. A
  LinkedIn URL identifies a person by the profile's name after `/in/`, decoded once, composed and lowercased, whatever
  host of linkedin.com's, scheme, slashes, query or fragment it carries; that is Jason's key, which the package asks for
  with `host.channelKey` rather than spell the rule again. The value Jason hands the plugin is already the key, and the
  CLI asks Reply for it in that form and then as given. A lookup the CLI cannot read to its end answers
  as Reply unavailable, and the attempt is made again; it is never taken for nobody or for one. Any other channel names
  a value Reply looks nobody up by, and is `invalid_channel_value` before anything is called.
- **A person Reply holds is named by its identifier,** so nothing Jason holds — names, company, title — is written to
  their record, and Reply needs no first name for them. A contact the lookup did not return but Reply's import matches
  is the one exception: the import fills that contact's empty fields from the ones given.
- **Only `find_or_create` imports.** `list_membership.add`, `campaign.enroll`, `optout.record`, `contact.update`,
  `social_invitation.send` and `message.send` import a person Reply holds nobody for, from the fields the operation's
  projection gives — the address or the LinkedIn URL, a first name, a last name, a company, a title, never a time
  zone — keyed by the email or, with none, by the LinkedIn URL, which the CLI writes as its lowercased slug. Each
  imports with the command that acts on the person, given `--contact -`: the add, the opt-out, the update, the send.
  The person is named by the value of the channel consumed alone, so a LinkedIn send imports by the profile. Before
  its import the CLI looks the person up once more for the opt-out, the update and a send, and for an add only where
  it is given a LinkedIn URL: an add given an email imports straight away, and Reply's import matches the email.
  `enrollment.stop`
  creates nobody: a person Reply does not hold takes part in nothing. `conversation.reply` finds the person through its
  thread, and creates nobody either. Each of the six says whether Reply found the contact or created it for that call, as reply-cli's
  `contact_created` says — `found` or `created` under `external_id_origins` — and says nothing where the CLI does not.
- **A delete works by the pin alone.** `contact.delete` names the contact by the identifier Jason pinned and by nothing
  else, reads it with `reply contact get` before anything is deleted, and refuses one Reply holds as opted out, because
  Reply keeps that stop on the contact itself. It pins nothing.
- **A send reads the contact before it goes.** `social_invitation.send` and `message.send` read the contact Reply holds
  for the person — found by the pin or by the lookup — with `reply contact get` before anything is sent: one Reply holds
  as opted out is refused as `suppressed`, and
  one whose address or LinkedIn profile is another than the work item's — or, for an email, one with no address — is
  refused as `contact_channel_conflict` and not changed. Reply's value is keyed whole by Jason's own rule, an address
  as Jason keys one rather than by JavaScript's lowercasing. An address is never written onto a contact. A LinkedIn
  send may record the person's profile on the provider's contact when it was missing, and that is the only thing a
  send ever writes to a contact; a value that is not Jason's key — one stored before the key, which Jason left as it
  stood — is refused instead of written. A contact answer without the LinkedIn profile key
  at all is read as lost and asked again, with nothing written or sent: a null or an empty value means the contact
  carries none. A person the lookup found nobody for has no contact to read: the send's own command imports them from
  the value consumed, so the contact it makes is reached there and holds no stop, and a profile that is not Jason's key
  is refused rather than imported. The command sends only to a contact it made. Where Reply already held one — its own
  lookup found the person, which it runs again before the import, or its import matched a contact Reply's lookup did
  not return — it sends nothing and answers `contact.already_held` with that contact's identifier; the package then
  reads that contact exactly as it reads one it found, and sends to it by its identifier. No send reaches a contact
  Reply held without that read.
- **The first name an import needs** is derived where the person has none, as *A person without a first name* under
  [The account a route names](#the-account-a-route-names) says, and the route's binding decides whether it may be.
- **Fields and lists travel with the operation that needs them.** Nothing is copied to Reply in the background, and
  nothing of Reply's contact is kept in Jason beyond its identifier.

## What can go wrong

Every failure carries a code the operation's own contract publishes and a class the runtime acts on:
`transient` is retried inside the work item's budget, `permanent` and `validation` are final,
`ambiguous` stops the item for a person, because Reply may already have acted, and `blocked` says Reply refused the
call for a reason outside the work item and did nothing for it, so the runtime holds the work that block concerns until
a trial gets past it. The tables below are
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
| `sequence contacts <sequence> --limit --offset` | read | One page of the people a sequence holds, and Reply's count of them: what `enrollment.list` answers from, and the read `campaign.create` makes of a sequence an earlier attempt may have made, to see that it holds nobody. |
| `sequence stats <sequence> --preset`, or `--from --to` | read | A sequence's counts over one window: what `engagement.summarize` answers from. |
| `inbox list --contact-id` or `--sequence-id`, `--source --since --channel --category-id --limit --offset` | read | One page of one of Reply's thread buckets, by person or by sequence: what `conversation.list` answers from — the inbox alone for `inbound`, the inbox and the sent threads merged for `all`. |
| `inbox get <thread> --limit --offset` | read | One thread, its subject, and one page of its messages: what `conversation.get` answers from, and the read `conversation.reply` makes of the thread's channel and person, one message long, before it sends anything. |
| `inbox categories` | read | The account's inbox categories, Reply's reserved ones included: what `conversation.classify` matches the category it was given against, by identifier or by name, before it files anything. |
| `contact get <contact>` | read | One contact by its id, as Reply holds it: the read `contact.delete` makes before it deletes anything, of whether Reply still holds the contact and whether it holds them as opted out, and the read a send makes of the same, and of the address or profile the contact is reached at. Nothing else is read and nothing is kept. |
| `contact find --contact -` | read | Reply's exact lookup of one address — an email, or a LinkedIn URL by its lowercased slug — read to its end and creating nobody: how every operation about a person with no pin finds Reply's contact for them before anything is written. The address is on stdin. |
| `sequence list --name -` | read | Every sequence whose whole name is this one, ignoring case, read to its end: what `campaign.create` compares with the name it is to give, before it makes anything. The name is on stdin. |
| `sequence add-contact <sequence>` with `--contact-id` or `--contact -`, `[--start-step] [--ignore-step-delay]` | write | Puts the person into the sequence, importing them first when Reply holds nobody under the address and they are given by their details. Into a live sequence, a send. |
| `sequence remove-contact <sequence>` with `--contact-id` or `--contact -` | write | Takes the person out of the sequence; their history stays. It never creates anybody. |
| `sequence pause <sequence>` | write | Pauses the sequence. |
| `sequence start <sequence> --expect-contacts <n>` | write | Starts the sequence, or resumes a paused one, only while it holds the population a person approved. Step 1 goes to everybody enrolled, so it is a send. |
| `contact-list add-contact <list>` with `--contact-id` or `--contact -`, `--refuse-opted-out` | write | Puts the person on the list, importing them first when Reply holds nobody under the address and they are given by their details, and refuses a person Reply holds as opted out. |
| `contact opt-out` with `--contact-id` or `--contact -` | write | Sets Reply's opt-out flag for the person, creating them only when Reply holds nobody under the address. This package never clears it. |
| `sequence create --name -` | write | Makes the sequence `campaign.create` answers with: a draft of the name, on stdin, with one task step. |
| `inbox reply <thread> --message -` | write | Replies to the thread's person on the thread's own channel, from the account that owns the thread, with the text on stdin. A send that Reply keeps no key on, so a lost answer is never repeated. |
| `inbox categorize <thread>` with `--category-id` or `--clear` | write | Files the thread under one category, or clears its category: set outright, so a repeat lands on the same state. |
| `inbox mark-read <thread>`, `inbox mark-unread <thread>` | write | Marks the thread read or unread: what `conversation.close` and `conversation.reopen` are at Reply, set outright. |
| `contact update <contact> --fields -` | write | Changes the fields and custom fields on stdin, by the contact's id; a null clears one and the rest stay. Set outright, so a repeat by the same contact lands on the same values. Before a LinkedIn send it also gives a contact that carries no profile the one the send consumes, and nothing else. |
| `auth whoami` | read | Who the credential signs in as: the Reply user whose accounts `sender.list` offers under `own`. Only the user's id is read; the login name beside it is the operator's and is not kept. |
| `account list --channel` | read | The mailboxes or the LinkedIn accounts a send can go from, one channel at a time, teammates' included, each with the Reply user whose it is: what `sender.list` answers from, and the read a send makes of its sender. |
| `schedule list` | read | Every sending schedule the team can use, in one answer, each with its Windows time zone and its windows by weekday: what `schedule.list` answers from. |
| `contact linkedin-connect --contact-id --linkedin-account-id [--message -]` | write | Invites the contact to connect on LinkedIn from the account named, with the note on stdin, or with none when there is no `--message`. A send that Reply keeps no key on, so a lost answer is never repeated. |
| `contact send-email --contact-id --email-account-id --message -` | write | Sends the contact one email from the mailbox named, outside any sequence, with the subject and the body on stdin — the text a person approved, written as HTML. A send that Reply keeps no key on, so a lost answer is never repeated. |
| `contact linkedin-message`, `contact linkedin-inmail`, `contact linkedin-voice`, each `--contact-id --linkedin-account-id --message -` | write | Sends the contact one LinkedIn message, InMail or voice message from the account named, outside any sequence, with the text on stdin — `{body}`, `{subject, body}`, or the `{script}` Reply speaks. A send that Reply keeps no key on, so a lost answer is never repeated. |
| `contact delete <contact>` | write | Deletes the contact by its id, with its history at Reply: it leaves every sequence and list. Run again after a lost answer, it answers that Reply holds the contact no longer. |

### Endings every operation can meet

| When | Code | Class | What to do about it |
|---|---|---|---|
| The CLI could not be started at all, so nothing was sent. | `provider_call_failed` | permanent | Install the CLI where the runtime can find it, check that the grant names it, reload. |
| The CLI refused the call before making it: its own usage error. | `provider_call_failed` | permanent | Nothing reached the account and a repeat builds the same call. Report it: either this package or the CLI's surface has moved. |
| Reply could not be reached, or a read failed after the CLI's own retries (`reply.unavailable`); or a **read** timed out, exited without an error the CLI explained, or printed something that is not a result; or a read made as part of a write — the exact lookup before a list add, an enrolment, an opt-out, a stop or an update, the read of the contact before a delete, the sequence read before a stop, the list of sequences before a create — was cut off at `Plugins:Exec:OutputBytes`. | `provider_unavailable` | transient | Nothing. Nothing changed at Reply — the CLI never resends a write — so the attempt is repeated. |
| Reply is still activating the sequence (`sequence.busy`). | `provider_unavailable` | transient | Nothing. The attempt is repeated once Reply has had time to finish. |
| A **write**'s outcome is unknown: the CLI answered `outcome.unknown`, or the write timed out, exited without an error the CLI explained, printed something that is not a result, or had its answer cut off at `Plugins:Exec:OutputBytes`. | `provider_answer_lost` | ambiguous | Where the operation's contract says a repeat is safe, the runtime repeats it; otherwise the item stops for a person. Look at the account — the contact, the list, the sequence — and decide; a blind repeat can create somebody twice or send to them twice. Whatever the attempt had learned, the CLI's own identifiers included, travels with the failure, so there are identifiers to look by. |
| The profile is not signed in, Reply refused the credential (`access.denied`), or Reply says the account lacks a feature where the operation has no code of its own for that (`access.feature_unavailable`). | `unauthorized` | permanent | Sign that profile in again, or issue a key carrying the scope the call needs. |
| Reply asked to wait longer than the CLI waits on its own (`rate_limited`). | `rate_limited` | transient | Nothing. The runtime waits and repeats the attempt; the wait Reply asked for is in the trace. |
| Reply refused the call (`reply.refused`), refused a field no row below names (`reply.invalid_request`), or the CLI answered a code this version does not know. | `provider_call_failed` | permanent | The CLI's code, Reply's own code and its sentence are in the trace. A refusal that recurs belongs in the table. |
| Not reported yet for the account: Reply refused the call for the account's money or plan or a revoked key, and did nothing for it. | `provider_blocked` | blocked | Clear it at Reply where the block says. Pending: the CLI publishes no code for this today, so nothing reaches the row `account_or_sender_blocked` in the table; when the CLI publishes one, its mapping in `rowOf` is where the switch lands, and the failure then carries the block. Until then such a refusal arrives as one of the rows above or below, under the class that row gives it. A sending account Reply refused for its own state is the sender's block, in the two sends' tables. |

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

### `campaign.create`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The Jason campaign already carries this plugin's campaign pin. Nothing is called. | `campaign_already_linked` | validation | Work with the sequence it is linked to, or make the new one for a new Jason campaign: a second would sit beside the first link. |
| A first attempt, and Reply holds a sequence whose whole name is this one, ignoring case. Nothing is made. | `campaign_name_taken` | validation | Choose another name, or link the sequence Reply holds with `campaign.get`. |
| An attempt after a lost answer, and Reply's sequences of the name are not exactly one draft that holds nobody, made no earlier than the work item less ten minutes: one is older, has been started or archived, or holds somebody, or there are more. Nothing is made. | `campaign_name_ambiguous` | permanent | Look at the sequences of this name at Reply — the trace says what was found — and link the one that is meant with `campaign.get`. |
| Reply answered the create with a sequence started or archived. Never expected; the sequence is pinned so it can be found. | `campaign_not_draft` | permanent | Look at the sequence at Reply before anything else is done with it. |
| Reply holds more than twenty thousand sequences whose names contain this one, so the CLI could not read the list to its end (`sequence.list_incomplete`). Nothing is made. | `provider_call_failed` | permanent | Make the sequence at Reply and link it with `campaign.get`. |

Every attempt lists the sequences of the name first, with `reply sequence list`, which reads Reply's partial name filter
to its end and keeps the whole name, ignoring case; the package compares the names again. A name that is free is given to a new sequence with one step, a task for its owner — "Added because a sequence
needs one step to exist; replace it before this sequence is ever started." — because Reply refuses a sequence with
none; `steps_added` is 1. No schedule, no sender and no settings are sent. Reply attaches the account's default
schedule and default mailbox by itself, even when an empty list of mailboxes is sent; the draft still sends nothing: it
is never started here, it holds nobody, and its one step is a task for its owner. Starting it is `campaign.activate`,
which waits for a person.

Reply keeps no key on a sequence, so after a lost answer the name is what finds the sequence the earlier attempt made.
The package cannot tell why an earlier attempt ended, so any attempt after the first takes the sequence only when it is
the one sequence of the name, in Reply's `new` state, not archived, created no earlier than ten minutes before the
moment the work item's identifier carries, and holding nobody — `sequence contacts` reads one person to tell. Finding
none, it makes one; finding anything else, it makes nothing and fails `campaign_name_ambiguous`.

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
| The pinned contact identifier, the campaign's, or the category's the read filters by, is not a Reply number at all. Nothing is called. | `provider_call_failed` | permanent | The pin or the category is wrong; the trace says which. |
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
| Reply will not accept the address or the LinkedIn URL on the channel the call named (`contact.invalid_email`, `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email), or the person has no value on that channel, or the channel is one Reply looks nobody up by. | `invalid_channel_value` | validation | Correct the contact's data; no later attempt would be accepted either. |
| The account will hold no further contact (`account.contact_limit_reached`). | `limit_reached` | permanent | Free capacity at Reply. A **list's** own capacity publishes no code of its own, so a full list arrives as `provider_call_failed` instead of this one. |
| The person has no pin, no first name and no last name, Reply holds nobody under the address, and the address has no text before its `@`. Nothing is added. | `provider_call_failed` | permanent | "A first name could not be derived." Give the person a first name in Jason and propose the work again: Reply creates nobody without one. |
| The person has no pin and no first name, Reply holds nobody under the address, and the route's binding says `derive_first_name: false`. Nothing is added. | `provider_call_failed` | permanent | "The route forbids deriving a first name." Give the person a first name in Jason, or allow the derivation in the route's binding, and propose the work again. |
| The person has no pin, and Reply holds more than one contact under their address (`contact.not_unique`). Nothing is added. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the work again. |

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
| Reply will not accept the address or the LinkedIn URL on the channel the call named (`contact.invalid_email`, `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email), or the person has no value on that channel, or the channel is one Reply looks nobody up by. | `invalid_channel_value` | validation | Correct the contact's data. |
| The account will hold no further contact (`account.contact_limit_reached`). | `limit_reached` | permanent | Free capacity at Reply. A **sequence's** own capacity publishes no code, so it arrives as `provider_call_failed`. |
| The start position names no single step of this sequence's chain (`sequence.step_not_found`), or a step number past the 10000 the CLI takes (`usage.range`), or the first touch is a value this version has no word for. Nothing is sent. | `provider_call_failed` | permanent | The trace says which reading failed. Ask for the first step, or name a position the sequence really has. |
| The person has no pin, and Reply holds more than one contact under their address (`contact.not_unique`). Nobody is enrolled. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the work again. |
| The person has no pin and no first name, Reply holds nobody under the address, and no first name could be derived, or the route's binding forbids deriving one. Nobody is enrolled. | `provider_call_failed` | permanent | As for `list_membership.add` above: a first name, or the derivation allowed. |

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
| More than one contact is held under the address (`contact.not_unique`). Nothing is set. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the opt-out again. |
| The person has no pin, no first name and no last name, Reply holds nobody under the address, and the address has no text before its `@`. Nothing is made and nothing is set. | `provider_call_failed` | permanent | "A first name could not be derived." Reply holds nobody under this address, so it cannot reach them there either. Where `jason suppression add` recorded the address, Jason's own register already stops Jason's own reach. |
| Reply will not accept the address or the LinkedIn URL, so no contact could be made to record the opt-out on (`contact.invalid_email`, `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email), or the channel is one Reply looks nobody up by. | `invalid_channel_value` | validation | Correct the contact's data. |
| Reply held nobody under the address, and the account will hold no further contact (`account.contact_limit_reached`). | `limit_reached` | permanent | Free capacity at Reply. Reply never held them under this address, so it cannot reach them there either. |

The person is found before anything is written: by the pin, or by the exact lookup of the address. Only when Reply
holds nobody under it is a contact made, and then the flag is set. Setting a flag that is set
already changes nothing, so the answer is `recorded` either way, and a repeat after a lost answer runs the command
again. The opt-out runs without anybody approving it — not running is the harm — and it runs even while its Jason
campaign is paused. Jason's own register is the other half: `jason suppression add` stops Jason's reach at once for
that address, and is written first.

### `enrollment.stop`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply sequence number, or Reply holds no sequence under it (`sequence.not_found`). A campaign the work item names for the first time is read when Reply holds nobody under the address; for a person found or pinned the CLI tells a missing sequence apart itself. | `campaign_not_found` | permanent | Correct the identifier. A link the provider has denied is not recorded. |
| The pinned contact identifier is not a Reply contact number at all. Nothing is called. | `contact_not_found` | permanent | The pin is wrong and has to be cleared. A well-formed pin Reply no longer holds is not this: it reads `already_stopped`. |
| More than one contact is held under the address (`contact.not_unique`). Nobody is taken out. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the stop again. |
| The person carries no value on the channel the call named, or the channel is one Reply looks nobody up by. Nothing is called. | `invalid_channel_value` | validation | Name a channel they have. |

The person is found without creating anybody: by the pin, or by the exact lookup of the address, as `optout.record`
does. A person Reply does not hold takes part in nothing, so the stop answers `already_stopped` without a command.
Otherwise the CLI reads the person's part in the sequence — a person who takes no part, or whose part is finished, is already stopped and nothing is
removed — and only then takes them out, never marking them finished. That read is also what makes a lost answer safe
to repeat. A person Reply does not hold is answered without a command, so then a campaign the work item names for the
first time is read, and a mistyped identifier is `campaign_not_found` rather than a completed stop. A person found or
pinned needs no such read: the CLI tells a missing sequence apart itself.
Reply's own opt-out is never read: a stop is for exactly the people it would refuse. A person approves every stop
before it runs, and its work waits while its Jason campaign is paused.

### `conversation.reply`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply thread number, or Reply holds no thread under it that this account may see (`inbox.thread_not_found`). Nothing is sent. | `conversation_not_found` | permanent | Reply by an identifier `conversation.list` returned. |
| The thread is on another channel than the one `args.channel` names, or on one Reply sends no reply on (`inbox.channel_mismatch` too). Nothing is sent. | `channel_mismatch` | validation | Name the channel the thread is on — `conversation.list` and `conversation.get` say which — and propose the reply again. |
| The thread is not this person's: its person is not the contact pinned for them, its address or LinkedIn profile is not the channel's value, or it names nobody Reply still holds. Nothing is sent, and nobody is pinned. | `thread_contact_mismatch` | validation | Reply in a thread of this person's, or propose the reply for the person the thread is with. |
| The text carries `{{`, which Reply reads as a placeholder of its own and fills in. Nothing is called. Or Reply refused the send because it read part of the text as a placeholder it could not fill in (`reply.refused`, with Reply's code `inboxThread.invalidInput` and a sentence that starts `RenderingFail`). Nothing is sent. | `message_invalid` | validation | Write the text without anything Reply reads as a placeholder and propose the reply again. |
| Reply's own opt-out register holds the thread's person (`contact.opted_out`). Nothing is sent. | `suppressed` | permanent | Nothing, on purpose. |
| Reply took the reply and could not deliver it (`inbox.send_failed`); Reply's reason for the failed message is in the trace as `provider_status_code`, null where it recorded none. | `delivery_refused` | permanent | A sending limit waits for its window; a disconnected account is connected again at Reply. Then propose the reply again. |
| The inbox is not available to this user or team (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes the inbox. |

The thread is read first, one message long, for its channel and its person, and nothing is sent unless both agree
with the work item: the thread is on the channel named, and its person is the one pinned — where there is a pin — and
in every case the one whose address, or LinkedIn profile, is the channel's value. Jason checked its own suppression
register for that value before the work was claimed, and the two are compared the way the register keys a value, so
that check is about exactly the person the reply reaches: an address whatever its case, and a profile link by the
profile's name after `/in/`, decoded once, composed and lowercased, whatever host of linkedin.com's, scheme, slashes,
query or fragment it carries.
Reply answers from the identity that owns the thread, so no sender is named, and no copy or blind copy can be. A person approves every reply before it is sent, and its work
waits while its Jason campaign is paused. A reply whose answer was lost — the CLI's `outcome.unknown`, or the call cut
off — may have gone, and it is never sent again: the item stops for a person, with the thread's person pinned so
whoever looks knows who it was for.

**What the answer leaves out, and what goes out beside the text.** `message.sent_at` is null: the provider's send
answer stamps a local time as UTC, so it is not passed on. The message's own `date`, which `conversation.get` returns
as that message's `sent_at`, is the moment, in true UTC. `message.external_id` is null on email, where Reply's answer
gives no identifier; the thread holds Reply's identifier for the message afterwards, and `conversation.get` returns
it. What Reply sends is the text followed by its own quote of the thread's earlier messages, and that is what the
thread stores. A person approving the reply reads the text: the approval shows the text only, never the quote.

### `conversation.classify`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply thread number, or Reply holds no thread under it that this account may see (`inbox.thread_not_found`) — which is also how Reply answers a category it listed and no longer holds when the filing reaches it. Nothing is filed. | `conversation_not_found` | permanent | File a thread by an identifier `conversation.list` returned; where the thread is there, file it again under a category Reply still lists. |
| Reply lists no category under the identifier or the name the call carried. Nothing is filed. | `category_not_found` | permanent | Name one of the account's categories: `conversation.list` and `conversation.get` report the ones threads are under. |
| More than one of the account's categories carries the name, whatever its case. Nothing is filed. | `category_not_unique` | permanent | Name the category by its identifier. |
| The category is Reply's reserved Do not contact, named by its identifier or by its name whatever its case; an entry that does not say whether it is reserved is read as reserved. Nothing is filed. | `category_opts_out` | validation | Filing there would opt the person out at Reply in every sequence, end their part in each and cancel the emails planned for them, and clearing the category undoes none of it. Record the stop with `optout.record`, and with `jason suppression add` for Jason's own register. |
| The inbox is not available to this user or team (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes the inbox. |

A category named by its identifier or its name is looked up in `inbox categories` first, so only Reply's number for
it reaches the command line, and a category Reply does not hold is told apart from a thread it does not hold: Reply
itself answers a filing under a missing category as if the thread were missing, so the lookup is the only place the two
can be told apart, and a category that disappears between the lookup and the filing is reported as
`conversation_not_found`. An entry of the list no thread can be filed under — one with no name, or an identifier that
is not a Reply number — is passed over. No category at all takes the thread out of the one it is in, with no lookup.
Either is set outright, so a repeat after a lost answer runs the command again.

**Reply gives its reserved names a meaning of its own.** What follows is read from the provider's own implementation,
not seen on an account. Filing a thread under any category fires Reply's own event for it, which a webhook carries for
every category, and a connected CRM is sent a positive event for Interested and a negative one for Not interested and
Not now. Not interested and Not now also feed Reply's own sales-agent re-engagement, which may reach the person by
Reply's hand rather than Jason's. Taking the thread out of the category again undoes none of it. So a campaign that must
own every send files under categories of its own, made at Reply, and not under the reserved names. Do not contact is
refused outright, as the table says.

### `conversation.close` and `conversation.reopen`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The identifier is not a Reply thread number, or Reply holds no thread under it that this account may see (`inbox.thread_not_found`). | `conversation_not_found` | permanent | Close or reopen a thread by an identifier `conversation.list` returned. |
| The inbox is not available to this user or team (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes the inbox. |

Reply holds no closed state; read is how its inbox shows a thread as worked. So closing marks the thread read and
reopening marks it unread, outright and with nothing read first, and the answer is `closed` or `reopened` whatever the
thread was. A repeat after a lost answer runs the command again. Neither records an outcome or stops outreach.

### `contact.update`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The pinned contact identifier is not a Reply contact number, or no longer resolves there (`contact.not_found`). | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again. |
| More than one contact is held under the address (`contact.not_unique`). Nothing is changed. | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the update again. |
| The person carries no value on the channel the call named, or the channel is one Reply looks nobody up by. Nothing is called. | `invalid_channel_value` | validation | Name a channel they have: an email or a LinkedIn profile. |
| Reply will not accept the address or the LinkedIn URL the person would be created from (`contact.invalid_email`, `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email). Nothing is changed. | `invalid_channel_value` | validation | Correct the contact's data; no later attempt would be accepted either. |
| Reply holds nobody for the person and the account will hold no further contact (`account.contact_limit_reached`). Nothing is changed. | `limit_reached` | permanent | Free capacity at Reply, then propose the update again. |
| The person has no pin, no first name and no last name, Reply holds nobody under the value, and it has no text before an `@`. Nothing is changed. | `provider_call_failed` | permanent | "A first name could not be derived." Give the person a first name in Jason and propose the update again: Reply creates nobody without one. |
| The person has no pin and no first name, Reply holds nobody under the value, and the route's binding says `derive_first_name: false`. Nothing is changed. | `provider_call_failed` | permanent | "The route forbids deriving a first name." Give the person a first name in Jason, or allow the derivation in the route's binding, and propose the update again. |
| A custom field the work item names is one the account has none of, whatever its case (`contact.field_not_found`). Nothing is changed. | `field_not_found` | validation | Name each custom field as the account names it. |
| Reply did not accept a value the work item gives a field (`reply.invalid_request`; Reply's names for the fields are in the trace), or the work item names one custom field twice, whatever its case. Nothing is changed. | `invalid_field_value` | validation | Correct the value, or name the field once, and propose the update again. |
| Reply answered that it holds no contact under an identifier it had just named — one the lookup found, or one the command's own import made or matched (`contact.not_found`). Nothing is changed, and nothing is pinned. | `provider_call_failed` | permanent | Look the person up at Reply, then propose the update again. |
| The work item arrived without the person's own fields at all — absent, not empty — which only a Jason older than 0.14.0 hands over. Nothing is called; the fields it lacks are named in the trace. | `invalid_channel_value` | validation | Update Jason to 0.14.0 or newer. |
| Reply says the account's plan or role may not change a contact (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes it. |

The person is found before anything is written: by the pin, or by the exact lookup of the address. Then one command
changes what the work item names — the values on stdin, a null clearing a field, a field left out staying as Reply holds
it — and nothing Jason holds of the person is copied to a contact Reply holds besides. A person Reply holds nobody for is
created by that same command, `reply contact update --contact - --fields -`: the person and the fields are one object
on stdin, the person imported as an add imports them, and the fields set on the contact made — or on the one the import
matched, where Reply's lookup did not return a contact it held, as an add acts on one. A custom field is named as
the account names it; the CLI reads the account's custom fields once and refuses an unknown name before anything is
written or imported. The update runs without anybody approving it: one work item changes one record. A repeat after a
lost answer works by the contact the failure pinned, or by the one the lookup then finds, and sets the same values
again.

### `contact.delete`

| When | Code | Class | What to do about it |
|---|---|---|---|
| No Reply contact identifier is pinned for the person. Nothing is called. | `contact_not_linked` | permanent | A delete is never aimed by an address. Look the person up at Reply and delete the contact there if that is what is meant. |
| The pinned contact identifier is not a Reply contact number, or, on a first attempt, Reply holds no contact under it (`contact.not_found`). Nothing is deleted. | `contact_not_found` | permanent | It may be deleted at Reply already, or the pin may belong to another account; look at Reply first. On any attempt after the first, the same answer is `already_deleted`: an earlier attempt may have deleted it. |
| Reply holds the person as opted out. Nothing is deleted. | `contact_opted_out` | permanent | Leave the contact at Reply: it carries the stop, and the address imported again later would be reachable. Jason's own register keeps its stop either way. |
| Reply says the account's plan or role may not read or delete a contact (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes it. |

The contact is read by the pin with `reply contact get` before anything is deleted, then deleted with `reply contact
delete`; Reply answering the delete as missing right after the read found the contact is the delete done too. A person
approves every delete before it runs, and its work waits while its Jason campaign is paused. The answer pins nothing,
and Jason's contact, its pins and its do-not-contact register stay as they were: a later operation about the person
fails `contact_not_found` rather than finding them by address again.

### `sender.list`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The one channel `sender_channel` names is one the account's plan or role does not include (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes the channel. Without `sender_channel` the same answer is the channel said as `not_included`, and the others are still listed. |
| Reply's list of mailboxes did not end within twenty thousand (`account.list_incomplete`). Nothing is listed. | `provider_call_failed` | permanent | Name the sender from Reply's own list of accounts instead. |
| Reply's answer was longer than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. | `provider_answer_too_large` | permanent | Name one channel with `sender_channel`, or raise `Plugins:Exec:OutputBytes`. |

`reply auth whoami` says who the credential signs in as, and `reply account list` is asked for one channel at a time:
asked for both at once, Reply refuses the whole list when the plan or role leaves one out. Under `owners: own` an
account is listed only when Reply names that user as its owner, so a teammate's account, and one Reply names no owner
for, are never offered; under `team` every account is, marked `own`, `teammate` or `unknown`. A whoami that names
nobody lists nothing, and the read is asked again.

### `schedule.list`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The credential lacks Reply's sequences read scope, the one thing Reply refuses this read for (`access.feature_unavailable`). Nothing is listed. | `unauthorized` | permanent | A key with the sequences read scope. |
| Reply's answer was longer than this installation lets a plugin read from a program (`Plugins:Exec:OutputBytes`), so it was cut off. | `provider_answer_too_large` | permanent | Raise `Plugins:Exec:OutputBytes`: Reply gives every schedule in one answer, so the same read is cut at the same place. |

`reply schedule list` gives every schedule in one answer, in no order Reply states, so the package orders them by
Reply's identifier, the lowest first, and cuts the page itself. A schedule Reply names with no time zone is left out,
and so are a weekday that is none of the seven or named a second time, a range whose times are not times of day and a
day's ranges past the twenty-fourth: what is listed is what Reply said. A list holding a schedule without a usable
identifier is refused whole by reply-cli (`reply.unavailable`), which is `provider_unavailable` and asked again. A range
may end at `24:00`, the end of the day, and never start there. The read pins nothing.

### `social_invitation.send`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The note carries `{{`, which Reply reads as the start of a placeholder of its own, closed or not; or Reply could not fill one it found (`linkedin.missing_variables`). Nothing is sent. | `message_invalid` | validation | Write the note without one and propose the invitation again. |
| Reply found the note too long (`linkedin.message_too_long`, or `reply.invalid_request` naming the `message` field). Nothing is sent. | `message_too_long` | validation | Reply refuses notes over 200 characters; the document holds a note to that, so this comes back only if Reply's own bound moves. Write a shorter note. |
| The sender's identifier is not a Reply number, Reply's list holds no LinkedIn account under it, or the invitation answered `linkedin.account_not_found`. | `sender_not_found` | permanent | Name an account `sender.list` returns. |
| Reply holds the sender's account under another address than the arguments state, under one where they state none, or under none where they state one. Nothing is sent. | `sender_mismatch` | validation | Name the account by the identifier and the address `sender.list` returns. |
| The sender's account is turned off or disconnected in Reply's list, or the invitation answered `linkedin.account_inactive` or `linkedin.invalid_cookie`; Reply's word is in the trace. The block is the sender's, for its `channel`; met by a command that ran from no sending account, such as a read on the way to the send, it is `sender_unavailable`, permanent, since a block names the account it holds. | `provider_blocked` | blocked | Put the account right at Reply. The invitation waits on the block, with the rest of that sender's work, and goes once a trial gets past it. |
| The LinkedIn account's daily or weekly allowance is spent (`linkedin.limit_reached`); which one is under `limit` in the trace. The block is the sender's, for its `limit`, and clears at `clears_at` where the CLI said how long to wait, up to a year; met by a command that ran from no sending account, it is `sender_limit_reached`, permanent. | `provider_blocked` | blocked | Nothing. The invitation waits on the block and goes once the allowance renews and a trial gets past it. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there (`contact.not_found`). | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again. |
| More than one contact is held under the profile (`contact.not_unique`). | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the invitation again. |
| The person carries no value on the channel the call named. Nothing is called. | `invalid_channel_value` | validation | Name the channel they have a profile on. |
| Reply will not accept the LinkedIn URL the person would be created from (`contact.invalid_linkedin_url`). Nothing is sent. | `invalid_channel_value` | validation | Correct the contact's data; no later attempt would be accepted either. |
| The work item arrived without the person's own fields at all — absent, not empty — which only a Jason older than 0.14.0 hands over. Nothing is called; the fields it lacks are named in the trace. | `invalid_channel_value` | validation | Update Jason to 0.14.0 or newer. |
| Reply holds nobody for the person and the account will hold no further contact (`account.contact_limit_reached`). Nothing is sent. | `limit_reached` | permanent | Free capacity at Reply, then propose the invitation again. |
| The person has no pin, no first name and no last name, Reply holds nobody under the value, and it has no text before an `@`. Nothing is sent. | `provider_call_failed` | permanent | "A first name could not be derived." Give the person a first name in Jason and propose the invitation again: Reply creates nobody without one. |
| The person has no pin and no first name, Reply holds nobody under the value, and the route's binding says `derive_first_name: false`. Nothing is sent. | `provider_call_failed` | permanent | "The route forbids deriving a first name." Give the person a first name in Jason, or allow the derivation in the route's binding, and propose the invitation again. |
| Reply holds the person as opted out. Nothing is sent. | `suppressed` | permanent | A refusal on purpose; the only way past it is the person asking to be reinstated. |
| Reply's contact for the person carries another LinkedIn profile than the work item's, or Reply says it carries none after the package gave it one (`contact.no_linkedin_profile`); or Reply holds nobody for the person and the work item's profile is not Jason's key. Nothing is changed and nothing is sent. | `contact_channel_conflict` | permanent | Look at the contact at Reply: the profile there, or the one Jason holds, is wrong. |
| Reply did not say whether the profile was written onto a contact that held none. Nothing is sent. | `provider_unavailable` | transient | Nothing. The runtime repeats the attempt, which reads the contact again first. |
| Reply answered that it holds no contact under an identifier it had just named — one the lookup found, one the command's own import made, or one it named as already held (`contact.not_found`). Nothing is sent, and nothing is pinned. | `provider_call_failed` | permanent | Look the person up at Reply, then propose the invitation again. |
| The command answered that Reply already held the person (`contact.already_held`) without saying which contact. Nothing is sent. | `provider_call_failed` | permanent | Look the person up at Reply, then propose the invitation again. |
| The account's plan or role does not include LinkedIn sends outside a sequence (`linkedin.feature_disabled`), or its LinkedIn accounts (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes them. |

Before the invitation, in this order and each refusing with nothing sent: the note is held to what Reply would send as
written; the sender is read from `reply account list --channel linkedin` and held to the address the person approving
read; the person is found by the pin or by `reply contact find`; and the contact Reply holds is read with `reply contact
get` — opted out is a stop, another profile a refusal, no profile filled with the work item's. Then `reply contact
linkedin-connect`, the note on stdin or no `--message` at all for an invitation without one. A person Reply holds nobody
for is named to it by `--contact -` instead, imported by the profile, and the note beside them in one object on stdin;
the command invites only a contact it made, and one Reply already held it names as `contact.already_held`, inviting
nobody, which the package then reads as above and invites by its identifier. A person
who is already a connection answers `already_connected`. An invitation whose answer was lost is never sent again:
the item stops for a person, with the contact pinned.

### `message.send`

| When | Code | Class | What to do about it |
|---|---|---|---|
| The subject or the text carries `{{`, which Reply reads as the start of a placeholder of its own, closed or not; or Reply could not fill one it found in a LinkedIn text (`linkedin.missing_variables`). Nothing is sent. | `message_invalid` | validation | Write it without one and propose the message again. |
| The message's format belongs to another channel than the one the call named: an email on LinkedIn, or a LinkedIn format on the email channel. The document refuses it first; the package refuses it too. Nothing is called. | `message_invalid` | validation | Name the format of the channel, or the channel of the format, and propose the message again. |
| Reply found the text too long (`linkedin.message_too_long`), or a voice message too long to speak (`linkedin.voice_too_long`). Nothing is sent. | `message_too_long` | validation | Write a shorter text. A script within the document's 1000 characters can still be too long to speak. |
| A LinkedIn message or voice message to a person the account is not connected with (`linkedin.not_connected`). Nothing is sent. | `not_connected` | permanent | Invite them with `social_invitation.send` and send once they accept, or send an InMail. |
| The account has no InMail credits left (`linkedin.no_inmail_credits`). Nothing is sent. | `insufficient_credits` | permanent | Propose the InMail again once the account has credits. |
| The sender's identifier is not a Reply number of the channel's size, Reply's list holds no account of the channel under it, or the send answered `email_account.not_found` or `linkedin.account_not_found`. | `sender_not_found` | permanent | Name an account `sender.list` returns. |
| Reply holds the sender's account under another address than the arguments state, under one where they state none, or under none where they state one; an address Jason would not key matches nothing. Nothing is sent. | `sender_mismatch` | validation | Name the account by the identifier and the address `sender.list` returns. |
| The sender's account is turned off or disconnected in Reply's list, or the send answered `linkedin.account_inactive` or `linkedin.invalid_cookie`; Reply's word is in the trace. The block is the sender's, for its `channel`; met by a command that ran from no sending account, such as a read on the way to the send, it is `sender_unavailable`, permanent, since a block names the account it holds. | `provider_blocked` | blocked | Put the account right at Reply — connect it again. The message waits on the block, with the rest of that sender's work, and goes once a trial gets past it. |
| The LinkedIn account's daily or weekly allowance is spent (`linkedin.limit_reached`); which one is under `limit` in the trace. The block is the sender's, for its `limit`, and clears at `clears_at` where the CLI said how long to wait, up to a year; met by a command that ran from no sending account, it is `sender_limit_reached`, permanent. | `provider_blocked` | blocked | Nothing. The message waits on the block and goes once the allowance renews and a trial gets past it. |
| The account lacks what the format needs (`linkedin.sales_navigator_required`, `linkedin.sales_navigator_disconnected`, `linkedin.voice_profile_missing`); Reply's word is in the trace. The same account still sends a plain message, so nothing is held. | `sender_unavailable` | permanent | Give the account Sales Navigator or a voice profile at Reply, or send another format, then propose the message again. |
| The pinned contact identifier is not a Reply contact number, or no longer resolves there (`contact.not_found`). | `contact_not_found` | permanent | The pin has to be cleared before this person can be worked again. |
| More than one contact is held under the address or the profile (`contact.not_unique`). | `contact_not_unique` | permanent | Resolve the duplicate at Reply, then propose the message again. |
| The person carries no value on the channel the call named. Nothing is called. | `invalid_channel_value` | validation | Name the channel they have. |
| Reply will not accept the address or the LinkedIn URL the person would be created from (`contact.invalid_email`, `contact.invalid_linkedin_url`, or `reply.invalid_request` naming the email). Nothing is sent. | `invalid_channel_value` | validation | Correct the contact's data; no later attempt would be accepted either. |
| The work item arrived without the person's own fields at all — absent, not empty — which only a Jason older than 0.14.0 hands over. Nothing is called; the fields it lacks are named in the trace. | `invalid_channel_value` | validation | Update Jason to 0.14.0 or newer. |
| Reply holds nobody for the person and the account will hold no further contact (`account.contact_limit_reached`). Nothing is sent. | `limit_reached` | permanent | Free capacity at Reply, then propose the message again. |
| The person has no pin, no first name and no last name, Reply holds nobody under the value, and it has no text before an `@`. Nothing is sent. | `provider_call_failed` | permanent | "A first name could not be derived." Give the person a first name in Jason and propose the message again: Reply creates nobody without one. |
| The person has no pin and no first name, Reply holds nobody under the value, and the route's binding says `derive_first_name: false`. Nothing is sent. | `provider_call_failed` | permanent | "The route forbids deriving a first name." Give the person a first name in Jason, or allow the derivation in the route's binding, and propose the message again. |
| Reply holds the person as opted out, or its blacklist refuses the email (`contact.blacklisted`). Nothing is sent. | `suppressed` | permanent | A refusal on purpose; the only way past it is the person asking to be reinstated, or the blacklist changed at Reply. |
| Reply's contact for the person is reached at another address or LinkedIn profile than the work item's, or at no address; or Reply says it carries no profile after the package gave it one (`contact.no_linkedin_profile`); or Reply holds nobody for the person and the work item's profile is not Jason's key. Nothing is changed and nothing is sent. | `contact_channel_conflict` | permanent | Look at the contact at Reply: the value there, or the one Jason holds, is wrong. |
| Reply did not say whether the profile was written onto a contact that held none. Nothing is sent. | `provider_unavailable` | transient | Nothing. The runtime repeats the attempt, which reads the contact again first. |
| Reply answered that it holds no contact under an identifier it had just named — one the lookup found, one the command's own import made, or one it named as already held (`contact.not_found`). Nothing is sent, and nothing is pinned. | `provider_call_failed` | permanent | Look the person up at Reply, then propose the message again. |
| The command answered that Reply already held the person (`contact.already_held`) without saying which contact. Nothing is sent. | `provider_call_failed` | permanent | Look the person up at Reply, then propose the message again. |
| The account's plan or role does not include LinkedIn sends outside a sequence (`linkedin.feature_disabled`), or the accounts of the channel (`access.feature_unavailable`). | `feature_unavailable` | permanent | A plan or a role that includes them. |
| Reply refused a field (`reply.invalid_request`) or the text (`reply.refused`, `contact.unknown` among them) in a way this version has no word for. Nothing is sent. | `provider_call_failed` | permanent | Reply's code, and the fields it names, are in the attempt's trace; correct what it names and propose the message again. |

The same path as the invitation's, in this order and each refusing with nothing sent: the subject and the text are held
to what Reply would send as written; the sender is read from `reply account list --channel email` or `--channel
linkedin` and held to the address the person approving read; the person is found by the pin or by `reply contact
find`; and the contact Reply holds is read with `reply contact get` — opted out is a stop, another address or profile a
refusal, no LinkedIn profile filled with the work item's. Then the one command made for the format, the text on stdin —
for a person Reply holds nobody for, given `--contact -`, which imports them, the person and the text one object
`{"contact": …, "message": …}`: `reply contact send-email` with `{subject, body}`, the body the text written as HTML; `reply contact
linkedin-message` with `{body}`; `reply contact linkedin-inmail` with `{subject, body}`; `reply contact linkedin-voice`
with the text as `{script}`. Given `--contact -`, the command sends only to a contact it made, and one Reply already
held it names as `contact.already_held`, sending nothing; the package then reads that contact as above and sends to it
by its identifier. `status: sent` says Reply took the message; on a failure, Reply's own word for an email's
state stays in the evidence. A message whose answer was lost is never sent again: the item stops for a person, with the contact
pinned.

## What it cannot do

- **A delete reads the stop and then deletes, in two calls.** `contact.delete` reads the contact's opt-out with `reply
  contact get` and deletes it with `reply contact delete` about a second later, so a person who opts out at Reply in
  between has that stop erased with the contact. Reply publishes no delete held to a condition, and reply-cli has none.
  Jason's own register keeps whatever stop Jason recorded.
- **An enrolment never moves or re-adds a person already in the sequence, and that guard is the CLI's.** Reply's own
  add, given a contact who already takes part, moves them forward to a later start step or refuses a re-add with a
  generic error. So the CLI reads the person's part in the sequence before it adds anybody, and answers that they
  take part with nothing written. A participation that appears between that read and the add is the one race left;
  Reply then does what is described above.
- **The `already_*` answers are never given.** The contracts let a repeat answer `already_member`, `already_recorded`
  or `already_paused` where the provider can tell. Reply cannot: it does not say whether a person was on the list
  already, a flag set again changes nothing, and it answers a pause of a paused sequence as a pause. So this package
  answers `added`, `recorded` or `paused`, and the effect at Reply is the same either way.
- **A person without a first name.** Reply requires a first name; see [Contacts at Reply](#contacts-at-reply) above.
- **Jason always asks Reply for a LinkedIn profile by its key: the profile's name, lowercased, in Unicode.** A name
  outside ASCII is asked for as its letters (`…/in/jörn-example`), not as its percent-escapes: Reply's own key decodes
  percent-escapes and stores the name in Unicode, so the Unicode form is Reply's native spelling. Whether Reply's lookup
  ignores the name's case is not verified.
  If it does not, a person Reply holds under a mixed-case profile link is not found: a list add or
  an enrolment creates a second contact, and `optout.record` lands on that one — made for it if need be — while the
  contact Reply held is untouched.
- **`conversation.list` with `source: all` reads Reply's inbox and its sent threads, and merges them.** Reply's thread
  filter has no value for "every thread": its inbox holds the threads with an inbound message, and `sent` the threads
  whose latest message is outbound. The package reads both from the first thread, a hundred at a time, merges them by
  last activity and lists a thread found in both once, so a page with `source: all` can reach at most the first 1000
  threads of each (`offset + limit` up to 1000); a page further in is refused rather than guessed at.
  `source: inbound` is one read and has no such bound.
- **An opt-out at Reply is a yes or a no.** What they said, where it arrived and when are Jason's record, carried
  by the work item; none of it reaches Reply, whose contact holds only its opt-out flag.
- **A person Reply holds under another address is not found by this one.** The lookup is exact, by the address the
  work item carries; somebody who wrote from a second address is a second contact at Reply. The opt-out is
  recorded on the contact for this address — made for it if need be — and the other is untouched: record the stop
  for that address as well. A stop finds nobody under this address and answers `already_stopped`, while the
  participation under the other one stays; `enrollment.list` shows who the sequence still holds.
- **A contact pin Reply no longer holds reads as a completed stop.** Reply answers a participation read for a contact
  it no longer holds with the same answer as for a person who takes no part, so `enrollment.stop` answers
  `already_stopped`. A sequence Reply no longer holds is told apart: the CLI reads it, and the stop fails
  `campaign_not_found`. The reason for a stop is Jason's record; Reply holds none.
- **A sequence's name is its only key, and one race is left.** Reply accepts two sequences of one name and keeps no
  key on either, so `campaign.create` refuses a name it finds taken rather than make a second. A sequence somebody
  makes under the same name between the list and the create is a race Reply offers no way to close. And any attempt
  after the first — whatever ended the one before, a list that failed included, when nothing was made — takes the one
  sequence of the name for the one its earlier attempt made, whoever made it, when it is an empty draft made at any
  moment from ten minutes before the work item was made up to that attempt, which can be hours later for work that
  waited.
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
  untranslated. `conversation.classify` files a thread under one of them, in Reply's own words. What it does filter by —
  the thread's channel, `thread_channel`, and one of the account's categories by Reply's identifier for it — Reply
  applies before it pages, so a page with either still says where the next one starts.
- **A reply Jason sends leaves the thread where it was in Reply's list.** Reply keeps a thread's last activity at the
  person's latest message after a reply Jason sends into it — read minutes after the send, and again many minutes after
  — so inbound sync says nothing for it, and writes no `inbound_reply_observed` line for Jason's own reply. A reply
  Jason sends does not wake the manager at all. A line for that thread is the person, or somebody else, moving it. The
  manager role reads the thread before it decides anything.
- **A reply costs three requests to Reply before its send.** The package reads the thread with `reply inbox get`,
  which asks for the thread and then for one page of its messages, one message long, to know its channel and its person
  before anything is sent; and `reply inbox reply` reads the thread once more itself, to send on the thread's own
  channel. Reply has no call that does both, and against a rate limit the three count.
- **A LinkedIn value Jason keys as no profile refuses the reply.** The thread's person is held to the profile the work
  item consumes, under the key Jason gives a profile link for its suppression register, so every spelling of one
  profile — its case, its slashes, a query, `uk.` or `m.` for `www.`, its name written in escapes — is the same profile.
  A value Jason keys as no profile — a link to another kind of page, another host, a name with anything but letters,
  digits, combining marks, hyphens and underscores in it, or one that starts with a hyphen or carries a mark after one —
  is nobody, and the reply fails `thread_contact_mismatch` with nothing sent:
  the register's check at the claim was about the value the work item consumes, and only that value is reached. Only
  Reply's spelling is keyed: the work item's profile is the one Jason stored and asked its register about, compared as
  it stands, so a value stored before the key, which Jason left as it stood, matches nothing Reply holds. The invitation
  holds Reply's contact to the profile the same way.
- **A text carrying `{{` is refused.** Reply writes its placeholders that way, and it does render one in a reply in a
  thread: a live account refused a reply carrying `{{firstName}}` because it could not fill it in. So the package
  refuses such a text before anything is called rather than let something nobody approved go out, and a refusal of that
  kind from Reply is `message_invalid` too. An invitation's note, and a message's subject and text on every format, are
  held the same way, `{{ name }}` and a `{{` that never closes alike; other template syntax is not refused. On an email
  Reply's own refusal of one comes under a code that says nothing of it, so one that got past this check would be
  reported as `provider_call_failed`, Reply's code in the trace.
- **Reply does not refuse a send outside a sequence to a person it holds as opted out, nor consult its blacklist on a
  LinkedIn send.** Jason's own register is checked at the claim for the address or the profile the send consumes, and
  the package reads Reply's opt-out mark on the contact before every invitation and every message. Reply consults its
  blacklist on an email alone, and its refusal there is `suppressed`. A blacklist rule at Reply that sets no
  opt-out mark on the contact is not seen: record the stop with `jason suppression add` as well.
- **Reply refuses notes over 200 characters.** The document holds a note to that, so a longer one is refused when the
  work item is written, before anybody is asked.
- **No moment is recorded for an invitation or a message.** Reply answers a LinkedIn send with a moment of its own, and
  nothing has shown it to be UTC, and a direct email with none, so `invitation.sent_at` and `message.sent_at` are null.
- **An invitation and a message read before they send.** The sender from `reply account list`, the contact with `reply
  contact get`, and the exact lookup where there is no pin: two or three reads before the send, and one write more when
  a contact carries no LinkedIn profile. Where the lookup found nobody and the send's own command names a contact Reply
  already held, that contact is read and the command run once more. Against a rate limit they count.
- **An email's text is sent as HTML written from it.** The text a person approves is plain text, and Reply takes an
  email's body as HTML, so every character HTML reads as markup is escaped and every line break is a `<br>`. Spaces and
  tabs stay as written: a run of them collapses to one space in the reader's mail program, though the HTML Reply sends
  keeps it. The HTML is longer than the text it is written from, so an email whose text is near the document's bound can
  come back cut from `conversation.get`, which reads at most 12000 characters of what Reply holds.
- **Whether a person is a connection is not read before a LinkedIn message.** Reply publishes no read of it, so a message
  or a voice message to somebody the account is not connected with is refused by Reply, with nothing sent, as
  `not_connected`.
- **A voice message's length is Reply's to judge.** The document holds a script to 1000 characters, Reply's published
  bound, and Reply may still find one within it too long to speak.
- **A profile may be written onto a contact before a refusal the package cannot foresee.** Where Reply's contact carries
  no LinkedIn profile, the invitation and a LinkedIn message give it the one the work item consumes first. Reply may
  still refuse the invitation or the message after that — LinkedIn sends outside a sequence turned off for the account,
  its allowance spent, a note Reply counts as longer than the document does, a person the account is not connected with
  (`not_connected`), an InMail with no credits left (`insufficient_credits`) or without Sales Navigator, or a voice
  message with no voice profile — and the profile stays on the contact.
- **A LinkedIn account whose profile link this package cannot read as a link cannot be named as a sender, nor a mailbox
  whose address Jason would not key.** The sender's address is compared as Jason keys a profile or an address, and a
  value Jason keys as nothing — not a link to a profile on linkedin.com, not a bare mailbox address with a dotted domain
  — matches nothing, so such an account is refused as `sender_mismatch` even when its address is copied from
  `sender.list`.
- **`schedule.list` does not say which campaign runs on which schedule.** `campaign.get` names no schedule, so which one
  a campaign sends on is read at Reply.
- **`conversation.classify` does not say what the thread was filed under before.** Saying it would mean reading the
  whole thread before every filing; the reads report a thread's category.
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
