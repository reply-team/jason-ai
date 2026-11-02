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


# Jason 0.25.0

## What is new since 0.24.0

- **A reply wakes the campaign's responder at once.** Until now a reply only released the manager's next check-in, so an
  answer waited for a review of the whole campaign. Now a reply on a thread the campaign's own operations pinned wakes the
  campaign's `responder` role itself, with a read of its thread already queued and ahead of the campaign's queued sends.
  A reply seen through a person another campaign shares, and a line the provider's events carry, wake the manager alone.
  The manager still decides at its review
  what the campaign as a whole calls for, and leaves a reply a responder has taken to the responder. [The responder's
  skill](https://github.com/reply-team/jason-ai/blob/v0.25.0/skills/runtime/roles/responder/SKILL.md) is the loop it
  follows: it reads the reply, classifies it, records its verdict, and reacts as the category says.
  - **What it does by itself.** It records its verdict, a `reply_classified` line of the campaign's chronicle that
    carries the reply's identifiers, a category, a confidence and a reaction and never a word of the reply
    (`jason reply record`). An absence, a "not now" and a bounce become a note with the moment, and nothing is sent. A
    person who asks not to be contacted again goes on Jason's do-not-contact register, and the provider's opt-out is
    recorded without anyone approving it. And where it is unsure, or the category is one that escalates, it asks you.
  - **What waits for you.** Every answer it drafts parks for your approval, like any send, unless a standing approval
    (next item) covers its category. What to do about a `referral` is always yours to decide. Below the confidence that
    the categories file names as `question_below`, the responder asks which category a reply is, offering the likeliest
    ids and always `other`, and drafts nothing.
  - **One answer per reply.** A reply gets one verdict and one draft: a verdict already standing on the message is the
    verdict, and a draft already open or sent for it stops a second one, for a run launched again as well. A further
    move on a thread whose responder has not finished is recorded on that responder, and wakes no other.
  - **Settings.** A `Responder` section of `settings.json`, read live: `OnReply`, on by default, turns the wake-up off,
    and `MaxAgeSeconds`, three days by default, leaves a reply older than that to the manager alone. Where nothing can
    launch the role, a responder is not created, and the log says so once for the campaign. Replies said before the
    upgrade owe no answer: the first reply that wakes a responder is one said after it. [The inbound sync
    page](https://github.com/reply-team/jason-ai/blob/v0.25.0/docs/inbound-sync.md) says what the step reads and when it
    passes a reply over.
- **A person may let drafted answers to one kind of reply go without being asked each time.** A standing approval is
  set for one campaign and one reply category, and only by a person who gives their name: a role never sets one, and an
  agent only on a person's explicit word.
  `jason approval set-standing <campaign> --category <id> --actor human:<id>` covers a drafted `conversation.reply`.
  `--operation` names the operations covered, `conversation.reply` when none is named, and a message to the same person
  outside the thread is covered only where `message.send` is named. `--expires-at` and `--max-releases` (from 1 to 10000) are the limits, and
  none is set unless you give it. `jason approval list-standing <campaign>` says what stands, how many drafts each one
  has let go and whether it is in force, and `jason approval unset-standing` takes one back. The skills advise one only
  where the reaction is a reply to the same person, and never for `referral`, `opt_out` or `bounce`.
  - **What it releases.** A draft nobody has been asked about, and only where every condition the claim checks holds: the
    verdict it answers is the newest on its reply and says `draft`, and was recorded by the run the runtime woke for
    that reply, by a person who gave their name, or with a person's answer to a question about that reply; the draft is
    unchanged since it was created; and a standing approval for exactly that category is in force, names the operation,
    has not expired and is under its ceiling. Otherwise the draft parks as before, and its preview says which condition
    failed.
  - **What it never releases.** A draft parked before the standing approval was set still waits for you, however long
    ago, and so does one whose question expired and which was reopened.
  - **Where to see what went out.** Each draft let go is an approved approval whose reason is `standing_approval`, under
    the name of the person who set it, with an `approval_standing_applied` line in the campaign's chronicle. [The work
    execution page](https://github.com/reply-team/jason-ai/blob/v0.25.0/docs/work-execution.md) has the contract, and
    [the approvals skill](https://github.com/reply-team/jason-ai/blob/v0.25.0/skills/runtime/approvals-and-questions/SKILL.md)
    shows an agent how to set, list and unset one.
- **A campaign states what it is for.** `goals` is a reserved key of a campaign's context: the outcomes the campaign's
  roles steer each conversation towards, each with an `id`, a `kind` (`meeting`, `demo`, `trial`, `interested_reply`,
  `purchase` or `custom`), a title, what "reached when" means in your words, and an order. It is checked when the
  campaign is created and whenever `jason campaign update-context` names it. The planning skills settle goals with you
  when a campaign is made, and a campaign with none is valid: its roles take a demo as the goal. The responder drafts
  toward the first goal the reply can move, and its verdict names that goal; a campaign with none names none. [The goals
  contract](https://github.com/reply-team/jason-ai/blob/v0.25.0/docs/contracts/context/goals.md) is the document the
  runtime enforces.
- **The reply categories are data, and yours to extend.** The thirteen base categories, `interested`,
  `meeting_requested`, `meeting_booked`, `question`, `objection`, `not_now`, `wrong_person`, `referral`, `out_of_office`,
  `auto_reply`, `bounce`, `opt_out` and `other`, are listed with what each says and its default reaction in
  [`categories.json`](https://github.com/reply-team/jason-ai/blob/v0.25.0/skills/runtime/roles/responder/categories.json),
  beside the responder's skill, and every other skill this release changed names a category by those ids and copies none. To add or refine
  one, write a section headed "Reply categories" in the `AGENTS.md` memory file of your client, of a project or of a
  campaign. Each entry there refines one base category under an id of its own, with a rule for what to do, and where
  two levels disagree the nearer wins: the campaign's over the project's, the project's over the client's. A reply that
  fits a refinement is recorded under its id and takes its base category's reaction unless the rule says otherwise; the
  responder's skill shows what such a section looks like. A category is what a standing approval is set on, so two
  replies whose safe reaction differs should never share one.
- **The other skills follow.** The manager reads the responder's verdicts, leaves a thread with an open responder alone,
  and records a person's answer to a doubtful category as a second verdict, which wakes the reaction it names. The
  analyst counts replies by category over a window and reports bounces as a deliverability figure, and inbox triage
  starts from what the responder left and never drafts a second answer to a reply that has one.
- **A person's answer to a question about a reply can back the verdict recorded from it.** `jason reply record
  --decision dec_...` (`decision_id` in the API) names the question that was raised about the reply, and the
  `reply_classified` line then carries the decision and the person who answered it. The runtime checks the answer when
  the verdict is recorded: the decision belongs to the campaign, it is answered, a person who gave their name answered
  it, the option they chose is the category being recorded, and the question was raised by a run that had recorded a
  verdict on this same reply. A refusal says why in one of five codes, `decision_not_found`, `decision_not_answered`,
  `decision_not_human`, `decision_option_differs` or `decision_not_about_reply`, and writes nothing. A standing approval
  counts such a verdict as that person's word, so a draft under it can go without a second approval of the choice they
  just made. An answer in words, with no chosen option, backs no verdict: the manager records the category it reads
  there without `--decision`, and the draft waits for you. The responder records its guess from its own run before it
  asks, which is what lets the answer count. [The API
  reference](https://github.com/reply-team/jason-ai/blob/v0.25.0/docs/runtime-api.md) has the contract.
- **The Reply package states what Reply does with a contact's later replies.** Its README no longer says that only a
  contact's first reply is read. For a sequence email, Reply keeps at most one reply to each email it sent and drops a
  finished contact's later replies on that thread before it stores them; after this side has answered on the thread
  through `conversation.reply`, the contact's next replies on it are stored and read. A LinkedIn thread keeps later
  messages. All of this is read from how Reply is built and has not yet been observed on an account. It is why
  answering a reply at once matters: a later reply on the thread can be lost until an answer goes out
  through `conversation.reply`, so a draft parked for approval leaves that window open, and a standing approval shortens
  it.

## Fixed since 0.24.0

- **An update says who asks.** Every request an update makes, for the feed, a release's archive and the files beside it,
  carries the same `jason/<version>` `User-Agent`, so a download can be told from a browser's in the feed's logs.
- **`jason doctor` reads less of a route.** From a `route.list` answer it keeps only the plugin each route names, and
  does not walk a binding. A plugin whose id is `toast` is aliased in the file like any other plugin Jason does not
  ship: the runtime's own notification channel keeps that word only on the one log line about a channel.
- **A pull request's hygiene check reads what the tree's own check reads:** a UTF-16 file whole, every file's name whole
  and folder by folder, a name git quotes as the bytes it was, and the changed names separated by NUL. This matters to
  whoever contributes; [the development guide](https://github.com/reply-team/jason-ai/blob/v0.25.0/DEVELOPMENT.md) says
  so.

## Upgrading from 0.24.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update`, `jason ui install`
  and `jason plugin install reply --replace` follow.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. This release changes the text of several skills, among them the
  responder, the manager, the analyst, the planning skills, inbox triage and the approval skills, and adds
  `categories.json` beside the responder's.
- **The responder is on after the upgrade.** Wherever an execution profile or an entry command resolves for the
  `responder` role, a reply on a campaign's own thread starts waking it at once. To turn that off, set `OnReply` to
  `false` in the `Responder` section of `settings.json`; the manager's check-ins are unchanged either way.
- **The data directory:** this release adds one database migration, `ResponderWatermark`, a column that records how far
  each campaign's replies have been read for its responder. The first start on 0.25.0 runs it after taking a backup, as
  every migration does, and sets each campaign level with its chronicle as it stands, so no answer is owed to a reply
  said before the upgrade. Your projects, campaigns, work items and chronicle stay as they were.
- **The Reply plugin** is published as 0.14.4 and needs reply-cli 0.18.0 or newer. What this version carries is the
  README's statement about a contact's later replies, as this release's list of what is new says; no call, code or
  message changed.
- **From 0.23.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
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

For a sequence email, Reply keeps at most one reply to each email it sent and drops a finished contact's later replies
on that thread before it stores them, so they reach none of `conversation.list`, `conversation.get` and the change
feed, and Jason's inbound sync says nothing of them. After this side has answered on the thread through
`conversation.reply`, the contact's next replies on it are stored and read as usual; a second reply that arrives
before any answer may never be seen. A LinkedIn thread keeps later messages. All of this is read from how Reply is built
and has not yet been observed on an account.

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

The responder's skill ships marked `draft`: no agent host has read its text in the state it ships in. Its commands parse
against this build, as every skill's do, but how an agent follows its loop on a real reply is not yet seen.
