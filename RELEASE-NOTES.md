<!--
What one release says of itself. The release workflow puts everything below its title into the release's body,
after the template's opening paragraph, with {{version}} filled in as the template's is; this comment is dropped,
so it is the only comment the file holds.

The shape: the title is `# Jason <version>`, then only these sections, each at most once:
`## What is new since <version>`, `## Fixed since <version>`, `## Upgrading from <version>` and `## Known issues`.
What is below this comment is the body of a public release, read on the release page and in the mail that announces
it: a link in it is an address into the public repository or the product's own, never a relative one.

After a release this file still names it: the version pull request leaves it as it is, and the next release rewrites
it. That is safe because a tag is checked before anything is built: a tag whose file is missing, untitled or names
another version stops the release there, so notes written for one release can never ship with another. The
button's run carries the template alone in that case, and says so. A candidate's tag needs the file to name the
candidate: v1.0.0-rc.1 reads `# Jason 1.0.0-rc.1`.

While a release's notes are not yet written, the file names its version with `-unwritten`, as
`# Jason <version>-unwritten`, which no tag carries, so a placeholder can never ship; whoever writes the notes
replaces the title and the body together.
-->


# Jason 0.23.0

## What is new since 0.22.0

- **The Windows executable is signed.** A release signs `jason.exe` with Reply's certificate, issued to ReplyApp Inc.
  through Azure Artifact Signing, and timestamps it under RFC 3161, so the signature stays valid after the certificate
  expires. Before anything is published, the release verifies the copy unpacked from the archive, with
  `signtool verify /pa` and with Windows' own reading of the signature, and stops unless that signature is valid,
  timestamped and made by a certificate whose name is exactly `ReplyApp Inc.`. You can read the same yourself:
  `Get-AuthenticodeSignature` on the unpacked executable says `Valid`, the subject of the certificate it was signed with
  begins `CN=ReplyApp Inc.`, and `signtool verify /pa /v`, where the Windows SDK is installed, accepts it and prints the
  timestamp. SmartScreen's reputation for a new publisher builds over downloads, so in the first weeks of signed
  releases a download may still show its dialog. The section on what is signed, below, gives each line to type.
- **The macOS executable's one native library is signed as well, and the executable carries two entitlements fewer.**
  `libe_sqlite3.dylib`, which the executable unpacks under `~/.net/jason/` the first time it runs, is signed with the
  same Developer ID as the executable and timestamped before it is bundled. So the executable keeps the hardened
  runtime's library validation, which loads only a library signed by Apple or by the executable's own team, and it
  ignores `DYLD_` environment variables: the only entitlements left, `com.apple.security.cs.allow-jit` and
  `com.apple.security.cs.allow-unsigned-executable-memory`, let the .NET runtime compile code as it runs. As in 0.22.0,
  the executable is notarized, and before anything is published the release's macOS job has `spctl` assess the
  executable's signature and stops unless that signature is accepted from the source `Notarized Developer ID`; the same
  job now also starts a copy carrying the quarantine flag a browser puts on a download, and its log says whether that
  copy started. The Linux executable is not signed, and the checksum stays its provenance.
- **Each operation says whether a provider block holds its work.** Every operation document now states
  `dispatch.under_provider_block`, `waits` or `runs`. When a provider refuses an account, or one sending account under
  it, Jason keeps the block, and the work of an operation that says `waits` waits on it and goes back to the queue when
  it clears; the work of one that says `runs` goes through the open block, and a `blocked` answer ends it. Seven of
  the twenty-four operations wait: the five whose reach is `act`, which may reach a person (`message.send`,
  `social_invitation.send`, `conversation.reply`, `campaign.enroll` and `campaign.activate`), and beside them
  `campaign.pause` and `optout.record`, which must each happen once. Every read and every other control runs, and among
  them `message.cancel`: taking a send back no longer waits behind the block that holds the send, since the moment of
  the send it takes back would pass while it waited, and a take-back the provider answers `blocked` ends failed, so a
  notification says it as a protective action that failed. A block's trial, the one held item let through to learn
  whether the provider still refuses, now goes first to held work that reaches a person, then by priority, then by
  age, and a control's answer never clears a block: only an act the provider takes shows that it accepts the account
  again. [The contracts](https://github.com/reply-team/jason-ai/blob/v0.23.0/docs/contracts/README.md) state the
  property once.
- **The API matches a request's members by their exact spelling.** A member is compared ordinally, at every depth of a
  request: `Name` is not `name`. At the top of a body, a name that differs from a member only in case is skipped like
  any member the operation does not take, so a `campaign.create` that carries `Name` alone answers `validation_failed`,
  saying `name` is required. Inside a free-form object, such as a context or a patch's `set`, it is a key of its own
  beside the one it resembles. Among a work item's arguments it is a name the operation's schema does not declare,
  refused as before, and a context that carries `Input` in place of `input` carries no arguments at all. Until now
  members were matched without regard to case. Nothing stored, returned or handed to a plugin reads differently, and a
  client that spells members as the API publishes them is refused nothing new.
  [The Runtime API page](https://github.com/reply-team/jason-ai/blob/v0.23.0/docs/runtime-api.md) says so.
- **`jason status --provider-cli <name>` says whose PATH it read.** It looks a bare name up on the PATH of the process
  running `jason status`, which is not necessarily the PATH the runtime started with, and whenever it read a PATH its
  answer now ends, in the JSON body's `fact` and in the `--human` line alike: *It read the CLI's PATH; the runtime's
  PATH may differ, see the install page.* A name given as a path reads no PATH and says nothing of one, except an npm
  shim whose `node.exe` is not beside it. The JSON's shape is unchanged.
  [The install page](https://github.com/reply-team/jason-ai/blob/v0.23.0/docs/INSTALL.md) says how the runtime's PATH
  differs.
- **This release publishes the Reply plugin 0.14.3, which needs Jason 0.22.0 or newer and reply-cli 0.18.0 or newer**.
  It reads Reply's deferred LinkedIn sends as Reply will answer them: a cancel of a send Reply is sending now, which
  Reply refuses at 409 once Reply times deferred LinkedIn sends itself and reply-cli names
  `direct_send.already_sending`, ends `send_not_pending` as a send that already went, where 0.14.1 ends it
  `provider_call_failed`; until then Reply answers such a cancel as one of a send that went, which both end
  `send_not_pending`. A LinkedIn message, InMail or voice message may name a Reply sending schedule, as an email may,
  where 0.14.1 refused one with `provider_input_invalid`: once Reply times deferred LinkedIn sends itself, it holds the
  message for the schedule's next opening, and until then the package answers `capability_not_offered` and sends
  nothing. A schedule Reply cannot time a send by, one it does not hold, one with no hours to send in, or one that
  keeps the contact's own time zone where Reply holds none, ends `provider_call_failed`, permanent, on one row for each
  of reply-cli's three codes, an email and a LinkedIn message alike, where 0.14.1 had no row for an email's. A cancel
  Reply could not confirm, of a send still queued or waiting for its schedule, which reply-cli names
  `direct_send.cancel_not_confirmed`, is `provider_unavailable`, transient, and repeated, as Reply asks; 0.14.1 ends it
  `provider_call_failed` under reply-cli 0.18.0. Its README says what Reply answers for a LinkedIn send it holds for a
  moment ahead or a schedule and how one can end, and with its error table says three things more exactly:
  - A LinkedIn invitation or message Reply refuses over the account's allowance, a 429 under
    `directLinkedIn.dailyLimitReached` or `directLinkedIn.weeklyLimitReached`, is the sending account's block,
    `provider_blocked` with the allowance under `limit`, whether or not Reply names the send in the state `failed`
    beside it; a 429 under no such code stays `rate_limited`.
  - `route_not_offered` says reply-cli passes Reply's bare 404 through on every command but the ones that read every
    404 as a thing not found, and names those, where 0.14.1 listed the commands that pass it through and left out most
    of them, `inbox list`, `inbox changes`, the InMail and the voice message among them.
  - Reply answers an email timed by a sending schedule in one of two ways: queued, with its moment, where the schedule
    is open now or opens within ten minutes, or waiting for the schedule, with only an estimate of the moment, where it
    opens later. Both are `accepted`, can still be cancelled, and carry no `accepted_for`: the estimate, which
    reply-cli 0.18.0 passes on as `estimated_send_at`, is never read as the moment Reply holds the send for.

  [The Reply plugin's page](https://github.com/reply-team/jason-ai/blob/v0.23.0/plugins/reply/README.md) says each.
- **For plugin authors.** `dispatch.under_provider_block` is required on every operation document, with no default,
  and a document whose `reach` is `act` that says `runs` is refused when the catalog loads; every fixture that records a
  `blocked` answer now says whether the operation's work is `held`. An attempt's provenance, as `workitem.get` answers
  it, carries `input_digest`, a field added beside the others. [The plugin
  guide](https://github.com/reply-team/jason-ai/blob/v0.23.0/docs/plugins.md) lists `package_unreadable` beside the
  manifest's other problem codes: the load raises it when `plugin.yaml` or the package cannot be read.

## Fixed since 0.22.0

- **A person's text no longer acts on the terminal of whoever reads a `--human` view.** A name, a description, a
  reason, a provider's message or a field value holding an escape sequence, a backspace, a line break or another
  character that changes how a line reads is shown as U+FFFD in every human view: in every table, on every labelled
  line, and in the lines a view writes around such text. Until now only the approval and thread views marked them. The
  left-to-right, right-to-left and Arabic letter marks (U+200E, U+200F and U+061C) are printed as they came, as the
  joiners and the soft hyphen already were, so text that mixes Arabic or Hebrew with Latin reads in order; the
  overrides, embeddings and isolates stay marked. A question's later lines sit indented under its first, so a line of
  the question cannot pass for the view's own `Status:` line. The JSON output is unchanged: the serializer already
  escapes such characters.
- **On a Windows console with an OEM code page, a glyph the code page writes as a control byte no longer reaches the
  terminal as one.** Page 437 writes the right arrow as 0x1A, the left arrow as ESC and the eighth note as CR. The CLI
  now writes a character outside ASCII that the console's encoding would write as a byte below 0x20, or as 0x7F, as
  U+FFFD, and leaves every other character as it came. It does not change the console's code page, and under UTF-8
  nothing changes.
- **A repeat after a lost answer is made only with the request that may already have reached the person.** Where a
  send's answer was lost and its plugin settles it by the work item's key, the claim repeated it under the same
  package, account and arguments even where what Jason would send had changed since. It now also compares the whole
  input composed for the plugin, by the `input_digest` each attempt's provenance records at its claim: the arguments,
  the send, the person with the fields the operation projects, the address or profile the send consumes, the pins, the
  campaign with its name and pins, and the work item's key, leaving out only the campaign's status, so a pause and a
  resume change nothing. A person whose address, field or pin changed since, or a campaign renamed or pinned since,
  refuses the repeat with `provider_answer_lost`, final, as a changed argument already did. A pin the compared
  attempt, or a later attempt of the same item, learned from the provider is left out before the comparison, since it
  is the provider's own answer to that request. The digest names the input and carries none of it, no address
  included.
- **`--human` lists a context that holds two keys differing only in case.** A context holding `region` and `Region` is
  listed as two keys; before, the CLI exited 1 after the runtime had answered 200.

## Upgrading from 0.22.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update`, `jason ui install`
  and `jason plugin install reply --replace` follow.
- **The Reply plugin:** with the package 0.22.0 published installed as it came, 0.14.1, `jason update apply` replaces
  it with 0.14.3 and keeps the package it replaced under the data directory's `update/previous/plugins/reply`. A
  package edited on this machine is left as it is, and so is the package under the install line and Homebrew:
  `jason plugin install reply --replace` swaps it for 0.14.3. 0.14.1 keeps working under 0.23.0 until it is replaced;
  until then, once Reply times deferred LinkedIn sends itself, a cancel of a send Reply is sending ends
  `provider_call_failed`.
- **reply-cli 0.18.0 or newer:** the Reply plugin 0.14.3 asks for it, since 0.18.0 is the release that names a cancel
  of a send Reply is sending, a cancel Reply could not confirm and a sending schedule that cannot time a send each by a
  code of its own, and reads and cancels a LinkedIn send Reply holds for later by the reference Reply calls it by,
  `linkedin-scheduled-<n>`. Under reply-cli 0.17.2 or older the plugin is `unavailable` with
  `executable_incompatible` once 0.14.3 is installed, so work routed to it fails
  at the claim with `plugin_unavailable` and runs nothing. Update it with `npm install -g reply-cli`, then
  `jason plugin reload`.
- **The data directory:** this release adds no database migration. Your projects, campaigns, work items and chronicle
  stay as they were.
- **A take-back held by a block:** a take-back that 0.22.0 parked behind a block stays parked after the upgrade until
  that block clears.
- **A send whose answer 0.22.0 lost:** an attempt recorded by 0.22.0 carries no `input_digest`, so its repeat is
  compared by its arguments only, as before; every attempt claimed by 0.23.0 is compared whole.
- **What reads or is refused differently:**
  - `campaign.create` and `campaign.update` refuse a campaign name holding a control character, and `contact.create`,
    `contact.update` and each row of `campaign.add_contacts` such a `first_name` or `last_name`: `validation_failed`
    with the detail `invalid` on the field, the code and the shape the API already gives a value it refuses. A name
    stored before is kept, and the human views show it marked.
  - Request members are matched by their exact spelling, a tightening: a member spelled in another case is no longer
    read as the member it resembles.
  - A control's success under an open provider block, a held `campaign.pause` let through as the block's trial
    included, leaves the block open; in 0.22.0 a held control's success, a pause's among them, cleared it. Only an
    act the provider takes clears a block now, or a person with `block.clear`.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. The campaign manager and the troubleshooting skill say which held
  work a block's trial lets through: acts first, and where none can run, one held control.

From 0.19.x to 0.21.x the update is one apply as well, and what the notes of 0.22.0 say of upgrading from 0.21.0
applies too, among it reply-cli 0.17.2 and the one migration 0.22.0 added. From 0.18.x or earlier, Jason's own
installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
older than 0.18.0, the first of the two can stop at `started`, as the first known issue below says.

## Known issues

On a build older than 0.18.0, the update through 0.19.0 can stop at `started`, saying that a library failed to load.
That update is still performed by the older build, and it happens when the two builds carry different .NET patches.
Type `jason update apply` again: it runs 0.19.0, which finishes the update, and one more `jason update apply` then
takes the newest release. From 0.18.x this does not happen: 0.19.0 is built on the same .NET patch as 0.18.0.

Reply's routes behind 0.22.0's send features - a send's moment and its sending schedule, reading and cancelling a send
by its reference, and the inbox change feed - reach Reply's accounts on Reply's own schedule. Until an account's Reply
has a route, the Reply package answers `capability_not_offered` for that operation or argument, and the account's
replies are read thread by thread in place of the feed, as its README says; nothing in Jason needs updating when the
route arrives.

Through Reply, Jason sees only a contact's first reply. Reply marks a contact who replied as finished and stops
detecting that contact's later replies, so a later reply from the same contact never reaches Jason.

A send or an invitation through Reply carries no sending time in its own result: `sent_at` is null, because Reply's
answer to a LinkedIn send gives a local time marked as UTC and its answer to an email gives none. `message.status`
reports when a send went, and a message's own moment is read from its thread, where it is true UTC.

Through Reply, a sending schedule times a LinkedIn message only once the account's Reply times deferred LinkedIn sends
itself. Until then the Reply package answers a LinkedIn message that names one `capability_not_offered`, and nothing is
sent, whether the account's Reply has no direct sends yet or has them without a schedule on LinkedIn.

The approval preview shows a send's `send_after` and not the provider object beside the arguments, so a send that also
names a sending schedule goes at the schedule's first opening at or after that moment, which may be later than the
moment the approver read.

A template held for a later moment, or queued by Reply behind the account's other sends, is filled in when it goes: a
name Reply cannot fill then fails the send after a person approved it, which `message.status` reads as `failed` with
Reply's reason.

Reply converts a LinkedIn text from HTML to text after filling it in, so markup in a LinkedIn message or InMail, a
final text or a template, does not reach the person as written; what Reply makes of a `<` that starts a tag there is
not known yet, and the Reply package does not refuse one on LinkedIn as it does on email.

Reply does not consult its blacklist when it takes a LinkedIn direct send, and does when it makes it: a send to a
person its blacklist holds is accepted, and fails when Reply makes it, which `message.status` reads as `failed` with
Reply's reason. Jason's own do-not-contact register is checked at the claim as before; record such a stop there with
`jason suppression add` as well, so the send is refused before it is made.

Through Reply, `schedule.list` does not say which campaign runs on which schedule: `campaign.get` names no schedule,
so which one a campaign sends on is read at Reply.

Through Reply, a reply on a thread, `conversation.reply`, is not held to the rule on a `<` that starts a tag yet, and
its text goes as written; whether Reply cuts a reply as it cuts a direct email has not been seen, so write an address
in a reply without its angle brackets.
