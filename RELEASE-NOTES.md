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


# Jason 0.22.0

## What is new since 0.21.0

- **A send can wait for its moment.** `message.send` and `social_invitation.send` take an optional `send_after`: the
  moment the message goes out after, with its offset, such as `2026-11-03T09:00:00-05:00`. A moment without an offset
  is refused when the work item is written. The approval shows it, after `send after:`. A send's work item now
  succeeds once the provider has taken the send: its result's `status` is `sent` where the provider sent it within the
  call, and `accepted` where it took it, for now or for the moment named. Whether the message went is read afterwards,
  never from that item. A plugin that does not list `send_after` on the send's channel is never handed it: the claim
  refuses the work with `argument_unsupported` before anything runs, so a moment is never dropped and sent at once.
  Through Reply, a moment goes on an email or any LinkedIn message, an InMail and a voice message among them, and on a
  LinkedIn invitation.
- **A send the provider holds can be read and taken back.** Two new operations, `message.status` and `message.cancel`,
  name a send by the work item that made it, `{"send": "wi_…"}`; the runtime supplies the reference the provider gave
  that send, so neither reaches a send another campaign or another route made. `message.status` answers `queued`,
  `sent`, `cancelled`, `failed` or `unknown`, the last for a send the provider attempted and lost track of, which may
  have gone; with it, when the send went, the message it became, and the provider's reason and code for a failure.
  `message.cancel` takes back a send still held; a second cancel succeeds as well, and a send that went or failed is
  refused with `send_not_pending`. A cancel runs in a paused campaign, as stopping is the protective half of the work.
  When a read finds a send gone, the chronicle says so once, as `message_sent_observed`; the campaign manager is woken
  by it only where `Manager:Triggers` lists it. Twenty-four operations are published now.
- **A send whose answer was lost is repeated only where that is safe.** Until now such a send was final. A plugin that
  sends under the work item's key, and says so in its manifest's `settles_by_key`, may now be asked again: a provider
  that keeps the key answers a send it already took with that send, and sends nothing twice. The claim makes the repeat
  only under the package, the account and the arguments of the attempt that may have sent, and otherwise refuses it
  with `provider_answer_lost`, final, saying that an earlier attempt may have reached the person. Through Reply both
  sends are repeated this way within 23 hours of the first attempt that may have sent; past that, or where Reply
  cannot be asked, the repeat is `send_not_settled` and nothing is sent.
- **An email through Reply can be timed by one of the account's sending schedules.** `message.send` takes, in the
  provider's own object, `"provider": {"schedule_id": "<id>"}`: the `id` of a schedule `schedule.list` names, written
  as a string of its digits. Reply holds the email for the schedule's next opening, or its first opening at or after
  `send_after` where both are given, and since Reply only estimates that moment the result's `accepted_for` is null.
  Any other shape of the object is refused before anything is called, with `provider_input_invalid`. A LinkedIn
  message cannot be timed this way, as the known issues below say. The
  [Reply plugin's page](https://github.com/reply-team/jason-ai/blob/v0.22.0/plugins/reply/README.md) says how.
- **Replies through Reply are read from Reply's change feed.** One read per account lists the threads that moved, in
  place of walking every thread of every campaign page by page, once per cadence. The walk stays as the fallback, and
  takes an account back whenever its feed is not offered or stops answering; `jason events sources` shows each
  account's source and whether it is healthy. What the feed gives Jason is identifiers, moments and Reply's flags,
  never a subject, a preview or a name.
- **A LinkedIn message and an InMail can be templates the provider fills in, as an email already could.**
  `compose: template` now covers a LinkedIn message's text and an InMail's subject and text. A plugin is handed a
  template only on a channel its manifest lists under `templates`; on any other channel the claim refuses it, with
  `template_not_offered`, before anything runs. The Reply plugin writes all three in Reply's syntax: the sender's
  first name and full name become Reply's LinkedIn variables, and `sender.address` is written in as the sending
  profile. A template naming `sender.last_name`, which Reply's LinkedIn account does not carry, is refused with
  `template_unsupported`, and a voice message stays a final text. You approve the template and its example, as for an
  email; the text as Reply filled it in is read afterwards from `message.status`, where Reply gives it. The copywriter
  and both planners now write a template for any of the three.
- **The macOS executable is signed with Reply's Developer ID and notarized by Apple.** A browser-downloaded archive
  runs on its first start without Gatekeeper refusing it; that first start needs the network, as the section on what
  is signed below says. The Linux and Windows executables are not signed, and the checksum stays their only
  provenance.
- **This release publishes the Reply plugin 0.14.1, which needs reply-cli 0.17.2.** Every call it makes tells Reply it
  came through Jason: reply-cli puts `jason-harness/<version>` at the front of its User-Agent, the runtime's version
  without its build metadata, and nothing that names the machine or the person. Where an account's Reply does not offer
  what a work item asks for, the plugin answers `capability_not_offered`: final, nothing done, and the manager skill
  takes it as a reason to do the work another way or tell you.
- **For plugin authors.** Two codes are reserved beside every operation, and no document declares them:
  `provider_input_invalid` (`validation`), for a provider object the package cannot read, and `capability_not_offered`
  (`permanent`), for what the provider does not offer yet. Neither clears a provider block. The manifest takes three
  new keys at `manifest_version` 1: `arguments`, the channels each argument is taken on; `settles_by_key`; and
  `templates`. A runtime older than 0.22.0 refuses all three with `unknown_field`. Every operation document now names
  its `channel_arguments` and `channel_templates`, and the claim has twenty checks in one published order.
  [The plugin guide](https://github.com/reply-team/jason-ai/blob/v0.22.0/docs/plugins.md) and
  [the contracts](https://github.com/reply-team/jason-ai/blob/v0.22.0/docs/contracts/README.md) set them out.
- **The skills know the new answers.** The campaign manager asks after one send and takes one back, reads the
  person's thread before any new send when a send is `unknown`, and treats `capability_not_offered` and
  `template_not_offered` as final; the troubleshooting skill tells apart a capability the provider does not offer, a
  provider object it refused, and a refusal of a send that may already have reached the person, which nobody repeats
  until the provider has said the earlier attempt did not act.

## Upgrading from 0.21.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update`, `jason ui install`
  and `jason plugin install reply --replace` follow.
- **The Reply plugin:** with the package a release published installed as it came, 0.10.0 from 0.21.0,
  `jason update apply` replaces it with 0.14.1 and keeps the package it replaced under the data directory's
  `update/previous/plugins/reply`. A package edited on this
  machine is left as it is, and so is the package under the install line and Homebrew:
  `jason plugin install reply --replace` swaps it for 0.14.1. Until it is replaced, the old package under 0.22.0 has
  its email templates refused with `template_not_offered` at the claim, since it lists no `templates`, and offers none
  of `message.status`, `message.cancel` and `send_after`.
- **reply-cli 0.17.2 or newer:** the Reply plugin 0.14.1 asks for it, and an older CLI makes the plugin `unavailable`
  with `executable_incompatible`, so work routed to it fails at the claim with `plugin_unavailable` and runs nothing.
  Update it with `npm install -g reply-cli`, then `jason plugin reload`.
- **The data directory:** this release adds one database migration, an index that lets the chronicle say once that a
  send went. The first start on 0.22.0 runs it after taking a backup, as every migration does. Your projects,
  campaigns, work items and chronicle stay as they were.
- **What reads or is refused differently:**
  - Through Reply, a send's work item ends with `status` `accepted` where Reply holds the send rather than sending it
    within the call. Both `sent` and `accepted` are done; neither is a reason to send again.
  - Through Reply, a `message.send` whose `provider` object is anything but `{"schedule_id": "<id>"}` is refused with
    `provider_input_invalid` before anything is called; the package 0.21.0 published, 0.10.0, ignored the object.
    The other operations still ignore it.
  - A template on a channel the routed plugin's manifest does not list under `templates` is refused with
    `template_not_offered` before anything runs.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`.

From 0.19.x and 0.20.x the same applies, and the update is one apply. From 0.18.x or earlier, Jason's own installation
updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build older than
0.18.0, the first of the two can stop at `started`, as the first known issue below says.

## Known issues

On a build older than 0.18.0, the update through 0.19.0 can stop at `started`, saying that a library failed to load.
That update is still performed by the older build, and it happens when the two builds carry different .NET patches.
Type `jason update apply` again: it runs 0.19.0, which finishes the update, and one more `jason update apply` then
takes the newest release. From 0.18.x this does not happen: 0.19.0 is built on the same .NET patch as 0.18.0.

Reply's routes behind this release's send features - a send's moment and its sending schedule, reading and cancelling
a send by its reference, and the inbox change feed - reach Reply's accounts on Reply's own schedule. Until an account's
Reply has a route, the Reply package answers `capability_not_offered` for that operation or argument, and the account's
replies are read thread by thread in place of the feed, as its README says; nothing in Jason needs updating when the
route arrives.

Through Reply, Jason sees only a contact's first reply. Reply marks a contact who replied as finished and stops
detecting that contact's later replies, so a later reply from the same contact never reaches Jason.

A send or an invitation through Reply carries no sending time in its own result: `sent_at` is null, because Reply's
answer to a LinkedIn send gives a local time marked as UTC and its answer to an email gives none. `message.status`
reports when a send went, and a message's own moment is read from its thread, where it is true UTC.

Through Reply, a sending schedule times an email only: Reply refuses a schedule on every LinkedIn direct send in this
version, so a LinkedIn message that names one is refused before anything is called, with `provider_input_invalid`.

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
