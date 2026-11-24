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

# Jason 0.51.0-rc.1

Jason 0.51 sends a follow-up email the way a person would: as a reply in the thread of the first letter, under the first
letter's subject, so the person finds it in one conversation instead of two. The approval card and the terminal say
under which letter and subject it will land before you approve it. A letter a provider held for later and then failed or
cancelled no longer counts as a touch, and Jason now reads every letter held for later until it is known whether it went,
and tells the campaign's manager about one that did not.

**Read this first.** Against a Reply that does not yet offer follow-ups, a follow-up is refused and nothing is sent: it
never goes as a new letter instead. The refusal is `capability_not_offered`, naming `follows`. To reach the person now,
propose the message again as a letter of its own, with `"follows": "new_thread"`.

## What is new since 0.50.0

**Follow-ups in the first letter's thread**

- **A follow-up goes as a reply under the first letter's subject.** Where you do not say otherwise, an email follow-up
  follows the line's first letter: the latest earlier email to that person in the campaign that went as a letter of its
  own, held the provider's reference, counted as a touch and was not taken back. Its own subject is not sent; the
  provider may add a quotation of the earlier letter beneath your approved text.
- **Say otherwise when you want to.** `"follows": "new_thread"` sends a letter of its own, and a work item's id follows
  that letter. A follow-up goes as a letter of its own, and the approval says so, where the routed plugin does not offer
  replies in a thread, where there is no earlier letter to follow, or where the earlier letter went through another
  account or another mailbox. A LinkedIn message follows nothing.
- **The approval says where it lands.** The approval card has a "Follows" row, and `jason approval get --human` and
  `jason workitem get --human` print a `Follows:` line: under which letter and subject the email goes, or that it goes
  as a new letter and why. The Subject row says "(not sent)" for a reply.
- **A follow-up that cannot go says why.** A followed letter that has not gone yet is tried again, and one held longer
  ends the item `follows_not_sent_yet`, to be proposed again with a later `send_after`. One that will never go ends it
  `follows_never_sent`; after a read of that letter records it as failed or cancelled it leaves the line, and the message
  can be proposed again. Nothing is sent in any of these.
- **The Reply plugin sends it.** The Reply plugin is now 0.20.0 and needs reply-cli 0.23.0 or later; an older reply-cli
  makes the plugin unavailable. reply-cli is a Node program installed with npm, and the
  plugin's README says how: [the Reply plugin](https://github.com/reply-team/jason-ai/blob/v{{version}}/plugins/reply/README.md).
- **The skills say the same.** The manager, the copywriter, the critic and the planners write a follow-up as a reply,
  read the letter it lands under, and know what to do with the two codes above.

**Letters held for later**

- **A letter that did not go is told.** After a letter's moment, Jason reads it 15 minutes later, then after an hour,
  then daily, until it is known whether it went. A letter the provider failed or cancelled, or still holds seven days
  past its moment, is said once in the journal as `message_unsent_observed`, and the manager is woken. The manager is
  told what each state means: a failed letter did not go; an overdue one may still go, so a second letter comes only
  after cancelling it succeeds; a cancelled one is read against the thread and the person's state first.
- **A failed or cancelled letter is no touch.** A letter the provider's own read says failed or was cancelled stops
  counting toward the ceiling of touches, the people list and the line's first letter, unless another read says it went.

**Speed**

- **A large campaign's lists and checks are fast.** An index on the sends a campaign names takes the people list of a
  campaign of 2,000 people from seconds to milliseconds.

## Fixed since 0.50.0

- **`jason status` no longer tells you to repair what Jason Desktop's first start repairs itself.** Between the install
  script's end and that first start, it named `jason plugin install reply --replace` and `jason ui install`; it now
  names Jason Desktop's start first and the command second.

## Upgrading from 0.50.0

- **Updating from 0.50.0.** On macOS and Windows, Jason Desktop's update item takes this version, and so does
  `jason update apply` in a terminal. On Linux, a deb takes it with the system's own updates, and an AppImage by its own
  update item or `jason update apply`. The first start of this version then sets Jason up again, as after any update.
- **The Reply plugin needs reply-cli 0.23.0 or later.** Update reply-cli with npm before the first follow-up. Against a
  Reply that does not offer follow-ups yet, a follow-up is refused with `capability_not_offered` and nothing is sent;
  propose it again with `"follows": "new_thread"` to reach the person now.
- **A letter the history already holds as failed or cancelled, by a provider's read, stops counting as a touch from this
  release.** A person below the ceiling only by such letters may be written to again by a touch not yet claimed. Nothing
  already refused comes back by itself.
- **Right after the update, every send held for later within the last thirty days is read once.** An old one still
  queued is said overdue at once, so the manager may be woken for many of them in one wave.
- **Approvals already waiting are asked again only where the letter now goes into a thread.** An approval given
  before this release is asked again where the follow-up would now go as a reply, and not where it goes as a letter of
  its own.
- **This version migrates the database.** Its first start writes a backup under `~/.jason/state/backups/` first, then
  adds the index. `context.follows` on a `message.send` is now the runtime's own record and is refused when a caller
  names it.
- **The journal forwarder is now 1.9.0**, which can forward the new kind and needs Jason 0.51.0 or newer.
  [Applying an older release](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#applying-an-older-release)
  says how to put back the package an older release published.
- **Going back.** A runtime refuses a database holding a migration it does not know (`data_newer`), so 0.50.x refuses
  data that 0.51 has opened.
  [Going back](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#going-back) says when
  `jason update rollback` restores the backup the migration took.
