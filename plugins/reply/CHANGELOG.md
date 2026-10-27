# Changelog

This package versions independently of the Jason runtime, under SemVer. A change at Reply's end is
absorbed by a release of this package alone.

## 0.10.0

- `message.send` sends an email composed as a `template` in Reply's own syntax, by the same `reply contact send-email`,
  and Reply fills it in for the person: each construct and name of the template standard as Reply writes it, the body
  HTML around the tags. Before anything is called it refuses as `template_unsupported` (validation) a template the
  standard refuses, a custom field under a key Reply keeps for a value of its own, a template whose text and fallbacks
  spell a prompt to a model, a template that compares a value with a text holding a character Reply's comparison does
  not read, and a template on LinkedIn. A template Reply could not fill in for the person is `template_unrendered`
  (validation), with Reply's own sentence in the message and nothing sent. A message composed as final text, or with no
  `compose`, goes as before, but for the `<` that starts a tag the next entry refuses: a double opening brace in it, or
  in an invitation's note, is still `message_invalid`, and so, from this release, is `@prompt[[`, which Reply reads as a
  prompt to a model.
- An email whose subject or body holds a `<` that starts a tag, one followed by an ASCII letter, `/`, `!` or `?`, is
  refused before anything is called, as `message_invalid` (validation) when it is final text and as
  `template_unsupported` (validation) when it is a template, and the message names the character without quoting the
  text. Reply decodes the body before it reads it and drops a tag it does not know with everything after it, in the body
  and in the subject alike, so an address written as `<name@example.com>` cut the email there; the subject goes as
  written. In a template its text, a fallback's text and the text a comparison compares are held alike, the last because
  whether Reply sanitizes before or after it fills a template in has not been seen, and so is a `<` right before a
  value. `a < b`, `<3` and a `<` at the end still go.

## 0.9.1

- No behaviour changes. The README installs the package with `jason plugin install reply`, which checks it against the
  release's `checksums.txt` and its content digest before placing it; copying the tree `jason skills install` fetched
  stays the fallback.

## 0.9.0

- The six writes that may create the person — `list_membership.add`, `campaign.enroll`, `optout.record`,
  `contact.update`, `social_invitation.send` and `message.send` — now report whether the contact was found or created:
  reply-cli's `contact_created` true is `created`, false is `found`, under `external_id_origins.contact` beside the
  identifier they return. Where the CLI's answer carries no `contact_created`, or one that is not a boolean, nothing is
  said, and nothing is guessed. A runtime from 0.19.0 writes it to the chronicle after the pin as an
  `external_id_origin` line, and keeps it on the attempt; an earlier runtime ignores it.

## 0.8.1

- No behaviour changes. The list of the person's own fields a write that may create somebody carries, which every
  document of one projects as `contact_projection`, now names that field of the documents as its source. The plugin
  host does not hand a plugin the documents, so the list stays in the package, and a test holds it to what the
  documents project.

## 0.8.0

- `schedule.list` lists the account's sending schedules with `reply schedule list`, which gives every schedule the team
  can use in one answer. The package orders them by Reply's identifier, the lowest first, and cuts the page itself, so a
  page asked for again starts where it did. Each schedule carries its time zone as Reply holds it, a Windows time zone
  identifier, under the scheme `windows` and never converted; whether its windows are read in each person's own time
  zone; whether it skips holidays; whether it is the default of the user the credential signs in as; its windows by
  weekday as authored; and the windows of a campaign's later steps where Reply's follow-up schedule is in use. The
  windows are listed as written, a window that runs to the end of the day ending at `24:00`, and no calendar is worked
  out from them. A credential without Reply's sequences read scope is
  `unauthorized`. The package needs reply-cli 0.16.0 or newer, the first release with `reply schedule list`.

## 0.7.0

- `message.send` and `social_invitation.send` say a sending account Reply refused for its own state is that sender's
  block: `provider_blocked`, class `blocked`, with the block's scope `sender` and the account's identifier. An account
  Reply lists as `inactive` or `disconnected`, and the answers `linkedin.account_inactive` and
  `linkedin.invalid_cookie`, are its `channel`; `linkedin.limit_reached` is its `limit`, and where reply-cli says how
  long to wait (`retry_after`), the block's `clears_at` is that long after the answer; a wait longer than a year names
  no moment, and the runtime tries the block on its own cadence. The runtime holds that sender's
  work on the block and lets it through once a trial gets past it, rather than ending each item final.
- The codes they replace, `sender_unavailable` and `sender_limit_reached`, stay declared. `sender_unavailable` is still
  what `message.send` says for a format the account lacks what it needs for — Sales Navigator for an InMail, a voice
  profile for a voice message — since the same account still sends a plain message; `no_inmail_credits` keeps its own
  word, `insufficient_credits`. The two codes are also what a command that ran from no sending account, such as a read
  on the way to the send, ends in when it meets one of these refusals, since a block names the account it holds.
- The block travels on `host.fail`, which a runtime from 0.18.0 accepts. An older one ends these refusals
  `plugin_exception`, permanent: as final as the codes they replace, and never a block mistaken for something else.

## 0.6.1

- the error table names `provider_blocked`, the runtime's class `blocked` from 0.18.0; no Reply answer reaches it until reply-cli reports a block; no behaviour change.

## 0.6.0

- `message.send`, `social_invitation.send` and `contact.update` are `find_or_create` now, as their documents say: a
  person Reply holds nobody for is created rather than refused as `contact_not_found`. The package needs reply-cli
  0.15.0 or newer, whose direct sends and `reply contact update` take the person by their fields, given `--contact -`,
  and import one Reply holds nobody for, as the add commands do; the person and the command's own input travel as one
  object on stdin. It also needs Jason 0.14.0 or newer, the first release whose three documents hand it the person's
  own fields; an input without them at all is refused before anything is called, naming the fields.
- A person found by the pin or by `reply contact find` is read first, as before: opted out at Reply is `suppressed`,
  another address or profile `contact_channel_conflict`, a missing LinkedIn profile filled. Only a person the lookup
  found nobody for goes through the command's import, named by the value consumed alone, with the first name derived as
  an add derives it; the route's `derive_first_name` set to false refuses one without a first name, as for an add, and a
  profile that is not Jason's key is refused rather than imported. A send's command goes only to a contact it made:
  where Reply already held the person — its own lookup found them, or its import matched a contact Reply's lookup did
  not return — it sends nothing and answers `contact.already_held` with that contact, which the package then reads
  first like any other and sends to by its identifier. `contact.update` changes a contact the import matched.
- New refusals, in the words the add operations use: `limit_reached` for an account that holds no further contact, and
  `invalid_channel_value` for a value Reply will not import. A contact Reply named and then answered it holds no longer
  — one this attempt was not pinned to — fails as `provider_call_failed` and pins nothing; `contact_not_found` means
  only a pin that no longer resolves.

## 0.5.0

- `sender.list` lists the mailboxes and the LinkedIn accounts a send can go from, with `reply account list`, one channel
  at a time, and says whose each one is: `reply auth whoami` gives the Reply user the credential signs in as, and only
  that user's id is read. Under `owners: own` a teammate's account, and one Reply names no owner for, is never offered;
  under `team` every account is, each marked. A channel the account's plan or role leaves out is said beside the others,
  so a plan without LinkedIn still lists its mailboxes. The package needs reply-cli 0.14.0 or newer.
- `social_invitation.send` invites one person to connect on LinkedIn with `reply contact linkedin-connect`, once a person
  approves it, with the note on stdin or with none. Before it the person is found by the pin or by `reply contact find`,
  and nobody is made; the account is held to the address the person approving read, from `reply account list`; and
  Reply's contact is read with `reply contact get`: one Reply holds as opted out is refused as `suppressed`, because Reply
  does not refuse an invitation to such a person by itself. A contact that carries no LinkedIn profile is given the one
  the invitation consumes first, and one that carries another is never changed. Reply refuses notes over 200 characters,
  and the document holds a note to that. A person who is already a connection answers `already_connected`, and an
  invitation whose answer was lost is never sent again: its document says `never`.
- `message.send` sends one person one message outside any sequence, once a person approves it: an email with `reply
  contact send-email`, or on LinkedIn a message to a connection, an InMail or a voice message Reply speaks from the text,
  with `reply contact linkedin-message`, `linkedin-inmail` or `linkedin-voice`. It takes the invitation's path first —
  the text held to what Reply would send as written, the account to the address the person approving read, the person
  found and their contact read, opted out at Reply a stop — and then the one command, the text on stdin. Reply's contact
  has to be reached at the address the work item consumes: an address is never written onto a contact. An email's text is
  plain text written as HTML that shows exactly it, its characters escaped and its line breaks kept, and Reply's
  blacklist refusing an email is `suppressed`. A LinkedIn message or voice message to a person the account is not
  connected with is `not_connected`, an InMail without credits `insufficient_credits`, and a message whose answer was
  lost is never sent again.
- A LinkedIn profile and an email address are keyed by `host.channelKey`, the runtime's own rule, rather than by a copy
  of it here: a profile by its name after `/in/`, decoded once, composed and lowercased, whatever host of linkedin.com's,
  scheme, slashes, query or fragment it carries, and an address as the runtime keys it rather than by JavaScript's
  lowercasing. `conversation.reply`, the invitation and the message compare Reply's spelling by that key, so a thread
  whose person's profile Reply spells another way is answered rather than refused, a value the runtime keys as no
  profile or no address is nobody's, and two addresses the register keys apart are two people: `conversation.reply`
  now refuses a thread whose person's address only JavaScript's lowercasing made the work item's, and a mailbox whose
  address the runtime would not key cannot be named as a sender. What Reply holds is keyed whole, never cut to 500
  characters first, so a longer value is nobody's. The value Jason stored is compared as it stands, and a profile that
  is not a key is never written onto a contact. A name outside ASCII reaches the CLI as its letters, not as its
  percent-escapes. The package needs Jason 0.13.0 or newer, which has `host.channelKey`.

## 0.4.0

- Four inbox operations, each through the Reply CLI command made for it. The package needs reply-cli 0.13.0 or newer.
- `contact.update` changes a person's fields and custom fields on Reply's contact for them with `reply contact update`,
  the values on stdin: a null clears a field, one left out stays, and a custom field is named as the account names it,
  an unknown name refused with nothing changed. The person is found by the pin or by `reply contact find`, and nobody is
  made: a person Reply holds nobody for is `contact_not_found`, and is enrolled or put on a list first.
- `contact.delete` deletes Reply's contact for a person, by the pin alone and once a person approves it: `reply contact
  get` first, then `reply contact delete`. A contact Reply holds as opted out is refused — Reply keeps that stop on the
  contact — and one it no longer holds is `contact_not_found` on a first attempt and `already_deleted` on any later attempt, since an earlier one may have deleted it.
  Nothing is pinned, and Jason's own record of the person and its register are not touched.
- Every operation about a person finds Reply's contact for them before it writes anything, the way its document's
  `preflight.provider_contact` says: by the pin, or else with `reply contact find`, Reply's exact lookup by email or by
  LinkedIn URL, read to its end. A person Reply holds is named by that identifier — also one who has a first name, whom
  `list_membership.add` and `campaign.enroll` imported before — so nothing Jason holds is written to their record. Only
  the list add, the enrolment and the opt-out import a person Reply holds nobody for; the stop creates nobody. Two
  contacts under one address are `contact_not_unique` for the list add and the enrolment too, which declare the code
  now, and a lookup the CLI could not read to its end is asked again. The package's own contact search is gone.
- `campaign.create` runs `reply sequence list` and `reply sequence create`, with the name on stdin, so the package
  makes no raw call at all. More than twenty thousand sequences whose names contain the name is still
  `provider_call_failed`, and nothing is made.
- A person reached by LinkedIn URL is looked up, imported and compared by its lowercased slug: on linkedin.com the
  path's case does not count. Reply is given the URL in that form, as Jason now keys it.
- The README states the limit that comes with it: whether Reply's lookup ignores the slug's case is not verified. If it
  does not, a person Reply holds under a mixed-case profile link is not found: a list add or an enrolment creates a
  second contact, and `optout.record` lands on that one.
- `conversation.reply` answers the person on a thread, in that thread, with `reply inbox reply`: the text goes on stdin,
  Reply sends on the thread's own channel from the identity that owns it, and nobody else can be put on it. The thread
  is read first with `reply inbox get`, one message long, and nothing is sent unless the thread is on the channel the
  work item names and its person is the work item's — the contact pinned for them where there is a pin, and in every
  case the one whose address, or LinkedIn profile, is the channel's value, compared as Jason keys the value for its
  suppression register: an address whatever its case, a profile link by its scheme and host whatever their case, with
  a default port, a user name, a fragment and one trailing slash dropped, its query exactly, and its path whatever its
  case on linkedin.com and its subdomains and exactly on any other host. Nobody is created: the thread's person is
  pinned once found. A reply costs three requests to Reply before its send: two for `reply inbox get`, and one more read of the
  thread inside `reply inbox reply`. A text carrying `{{` is refused,
  `message_invalid`, because Reply renders it as a placeholder of its own; Reply's own refusal of a text it could not
  render — `reply.refused` with its `inboxThread.invalidInput` and a sentence starting `RenderingFail` — is
  `message_invalid` too, and every other refusal of the send stays `provider_call_failed`. A reply Reply took and could
  not deliver is `delivery_refused`, with Reply's reason in the trace. A reply whose answer was lost is never repeated:
  the operation's contract says `never`, and the work item stops for a person.
- What a reply answers: `sent_at` is null, because Reply's send answer stamps the account's local time as UTC; the
  message's own date, which `conversation.get` returns, is the moment. Reply gives no identifier on email, so
  `message.external_id` is null there. What goes out is the text followed by Reply's own quote of the thread's earlier
  messages, and the approval shows the text only.
- `conversation.classify` files a thread under one of the account's inbox categories, by Reply's identifier for it or
  by its name, matched in this package against `reply inbox categories` and never put on a command line, or takes it
  out of its category; `reply inbox categorize` sets it outright. Reply's reserved Do not contact is refused,
  `category_opts_out`, by identifier or by name, before anything is filed: filing there opts the person out in every
  sequence, and clearing it undoes nothing. Stopping a person is `optout.record`'s path. The README says what Reply does
  on its other reserved names — events, a connected CRM, its own re-engagement — and that a campaign that must own every
  send files under categories of its own.
- `conversation.close` and `conversation.reopen` mark the thread read and unread with `reply inbox mark-read` and
  `reply inbox mark-unread`: Reply has no closed state, and read is how its inbox shows a thread as worked.
- `conversation.list` takes `thread_channel` and `category`, passed to every `reply inbox list` it makes as `--channel`
  and `--category-id`, so Reply filters before it pages. A category identifier that is not a Reply number asks nothing.
- A reply Jason sends leaves the thread where it was in Reply's list, which keeps its last activity at the person's
  latest message, so inbound sync says nothing for it: a reply Jason sends does not wake the manager at all.

## 0.3.2

- `campaign.create` makes the sequence a Jason campaign works with, so nobody has to make it at Reply by hand first. It
  takes a name and nothing else, and the sequence it makes is a draft that holds nobody and has never been started.
  Reply refuses a sequence with no step, so it is given one: a task for its owner, which reaches nobody even once the
  sequence runs, and says it is to be replaced before the sequence is ever started. `steps_added` is 1. No schedule
  and no sender are sent; Reply attaches the account's default schedule and default mailbox by itself, and the draft
  still sends nothing. The sequence is pinned to the Jason campaign, so every later operation works by it.
- Reply keeps no key on a sequence, so the name is what a repeat finds an earlier attempt's sequence by. Every attempt
  lists the account's sequences to the end first and compares whole names, ignoring case. A first attempt that finds
  the name taken makes nothing: `campaign_name_taken`, an archived holder included. Any attempt after the first takes
  the one sequence of the name only when it is a draft that holds nobody, made no earlier than ten minutes before the
  work item; anything else is `campaign_name_ambiguous`, for a person to decide. A Jason campaign already linked is refused before any call:
  `campaign_already_linked`. The list is read to twenty thousand sequences at most; an account holding more makes
  nothing, `provider_call_failed`, because whether the name is taken could not be decided.
- The name never reaches a command line: the list is asked for every sequence, and the name travels in the create's
  body, on stdin. Two raw calls join the contact search, `reply api /v3/sequences` to list and, with `POST`, to create:
  reply-cli has no command that creates a sequence.

## 0.3.1

- A person with no pin and no first name, whom Reply does not hold, is imported under a first name derived from what
  Jason holds: their last name, which is then not sent as a last name, or else their address's text before the `@`,
  as spelled. `list_membership.add`, `campaign.enroll` and `optout.record` all do this. The first two refused such a
  person before, and an opt-out failed with Reply's "Invalid data". A person Reply holds is still worked by the
  identifier the lookup finds, and nothing is written to their record. `optout.record` now makes that lookup itself.
- `derive_first_name` in the route's binding, true when absent. With `false`, such a person is refused instead by a
  list add or an enrolment: `provider_call_failed`, "the route forbids deriving a first name". An opt-out is
  protective and derives either way. With no last name and no text before the `@`, the reason is "a first name could
  not be derived".
- A first name of nothing but white space counts as none, judged by JavaScript's own trim as the Reply CLI judges it.
  It used to be sent as it was, and the CLI, which trims it, imported nobody, even when Reply held the person.
- Each derivation writes one diagnostic line naming the field it used, `last_name` or `email_local_part`, and never
  the name.

## 0.3.0

- Every operation runs the Reply CLI command made for it — `reply sequence add-contact`, `reply contact opt-out`,
  `reply inbox list` and the rest — instead of calling Reply's API through `reply api`. Which endpoints to call, in
  what order, and how to tell Reply's answers apart is now the CLI's to know; the package turns the operation's input
  into the command's arguments, and the CLI's result and error code into the operation's answer or failure. It needs
  **reply-cli 0.11.0** or newer.
- The one raw call left is Reply's contact search, which `list_membership.add` and `campaign.enroll` make for a person
  with no pin and no first name, whom Reply will not import. Reply holding nobody under the address is now a refusal,
  `provider_call_failed`, saying a first name is needed; such a person is no longer created through a second path.
- `list_membership.add` answers `added`, `optout.record` answers `recorded` and `campaign.pause` answers `paused`,
  every time. `already_member`, `already_recorded` and `already_paused` are no longer answered: Reply does not tell
  them apart, and the effect at Reply is the same.
- The reads the package made before a write or a repeat — the opt-out register before an enrolment, a person's part
  in the sequence, the list's members, a sequence's state and count — are gone from it. The CLI reads what each
  command needs before it writes, and a repeat after a lost answer runs the command again.
- A person who opted out between two `list_membership.add` attempts is now refused as `suppressed` on the repeat: the
  CLI reads Reply's register before it adds.
- `campaign.activate` reads `was` from the state the CLI saw before the start. A sequence already live whose people
  Reply would not count is `provider_unavailable`, transient; a start from a state with no word for it, such as
  Reply's `skipped`, is `provider_call_failed` with the state in the trace.
- `enrollment.stop` fails `campaign_not_found` for a pinned campaign Reply no longer holds, where it answered
  `already_stopped`: the CLI tells a missing sequence apart from a person who takes no part.
- `conversation.get` reads a thread with a long history a page at a time. The thread no longer carries its whole
  history, so the history no longer has to fit within `Plugins:Exec:OutputBytes`.
- `engagement.summarize` refuses a window that does not end after it starts as `window_invalid`, before anything is
  sent. Reply answers such a window with a server error, which was asked again.
- A failure's trace carries the CLI's own error code (`cli_code`), Reply's own sentence where the CLI passed one on
  (`provider_detail`) and the wait Reply asked for (`retry_after`). Reply still activating a sequence
  (`sequence.busy`) is `provider_unavailable`, transient.

## 0.2.6

- `campaign.enroll` reads a person's participation before enrolling anybody who existed before the call, not only on
  a repeated attempt: Reply's bulk add moves a person already in the sequence forward to a later start step, and
  refuses a re-add at the same step with a generic error after assigning the sequence's sending account. Such a
  person now answers `already_enrolled` or fails `collision_refused`, as the arguments asked, and nothing is written.
- `enrollment.list` fails `campaign_not_found` for a sequence this account does not hold. Reply answers that page
  with an empty list; its count is what refuses, and the package read the refusal as an unknown total.
- `conversation.list` with `source: all` lists the threads Reply files as sent as well as its inbox; it read the inbox
  alone, because Reply's filter with no source is the inbox. A page past the first 1000 threads of each is refused.
- Nothing else changes: the same codes, classes and messages for every other ending.

## 0.2.5

- The README no longer sends a reader to pages this repository does not carry: it says in place that an
  approval belongs to the operation's contract, and names `jason route resolve` for which route a claim would
  take.
- Nothing else changes: the same calls, codes, classes and messages.

## 0.2.4

- The README states the provider's bound on inbound replies: Reply marks a contact finished at its first reply and
  stops detecting that contact's later replies, so only a contact's first reply is read.
- Nothing else changes: the same calls, codes, classes and messages.

## 0.2.3

- `conversation.list` now lists a thread whose last activity is stamped exactly at `since`. The contract keeps the
  threads that moved at or after `since`, and Reply's `from` keeps only those that moved after it, to the
  millisecond, so the package passed the contract's inclusive bound as Reply's exclusive one and a thread stamped
  at that very moment was left out. It now asks Reply from one millisecond before `since`.
- Nothing else changes: the same calls, codes, classes and messages.

## 0.2.2

- A call to Reply is given what is left of the invocation's budget less five seconds (never more than half of
  what is left), where it used to be given all of it. A call given everything runs out at the moment the
  host's own engine ends the invocation, and on a busy machine the host's work after a cut-off call — ending
  the CLI's process tree, waiting for its pipes — could outlast what the engine had left. The host then
  answered `plugin_timeout` in place of this package's `provider_unavailable` or `provider_answer_lost`, and
  the identifiers the attempt had learned did not travel with it. A call that runs out of time is now cut off
  with time left for the package to answer it.
- Nothing else changes: the same calls, codes, classes and messages.

## 0.2.1

- The messages of `campaign_not_pausable`, `campaign_not_activatable` and `population_changed`, and of
  four `provider_call_failed` rows — a refusal in a word this package does not know, an enrollment Reply
  rejects as invalid, an enrollment that could not be built, and a read filtering by an identifier that
  names nothing — no longer send their reader to the failure's details, which never reach a work item's
  last error. Each says what happened and what could have caused it, and that the attempt's trace says
  which, with Reply's own code or both numbers. The README's tables say the same, and two tests hold
  every message in the error table and every remedy in those tables to it.

## 0.2.0

Adds the four reads — `enrollment.list`, `engagement.summarize`, `conversation.list` and
`conversation.get` — and four writes: `campaign.pause`, `campaign.activate`, `optout.record` and
`enrollment.stop`. Built against Reply API v3 through the same CLI, under the same rules as the
first three operations: eleven in all, every operation this build publishes.

- `campaign.pause` pauses a sequence, and answers `already_paused` for one that is; it runs while its
  Jason campaign is paused. `campaign.activate` starts or resumes one only when Reply's count is the
  population a person approved, and fails `population_changed` with both numbers when it is not.
- `optout.record` sets Reply's opt-out flag, and `enrollment.stop` takes a person out of a sequence
  without marking them finished. Both find a person without a pin by Reply's free-text search, read to
  its end and compared with the address exactly; neither creates a second contact for an address Reply
  holds, and a stop creates nobody. What they said, and why they were taken out, never reach Reply.
- A contact Reply answers under an identifier this package cannot work by — from the search, the import
  or the create path — is reported in the failure's details by that identifier alone, never with the
  rest of the person's record.
- A read whose answer is cut off at `Plugins:Exec:OutputBytes` now fails `provider_answer_too_large`,
  permanently, instead of being retried as an answer that went missing. A write's cut answer is still
  a lost write.
- The manifest asks for 256 MiB of memory: a read of a thread reads two answers that can each be as
  large as the exec capture.
- Upgrading: `Plugins:Limits:MaxMemoryMb` must be at least 256 (it is 512 by default). A lower ceiling
  refuses the whole reload that loads this version, and every package stays on the snapshot before it.

## 0.1.0

First release. Implements `campaign.get`, `list_membership.add` and `campaign.enroll` against Reply
API v3 through the Reply CLI, which owns the account's credential. What it does not do yet, and why,
is in [README.md](README.md).
