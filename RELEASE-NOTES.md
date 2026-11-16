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

# Jason 0.47.0

## What is new since 0.46.0

This release is about what an agent's campaign did without Jason: a provider's own sequence sends messages on its own
schedule, and Jason now sees them, counts the people they reached and shows them on a person's timeline. Around it, the
chat takes a list you give it and adds it only when you press, archiving an agent is your own act, and the Windows
window loses its second title bar.

- **Messages the account sent are seen and counted.** A provider's events can now report a message the account sent,
  whoever sent it (Jason, a sequence's own step, or a person in the provider's screens): a fifth kind of the events
  contract, `message_sent`, with the contact, the provider's campaign, the thread, the message and the moment. A plugin
  is not obliged to report it, and is asked to report a send only where it can give the moment it was sent.
  - The runtime says each stored send once, as the journal line `provider_send_observed`, in the one campaign whose
    provider campaign the send names. A send that names no provider campaign, or one that no campaign or more than one
    holds, is kept and concerns nobody. The line wakes no manager unless you list it under `Manager:Triggers`, and no
    reader of replies or of provider events sees it.
  - `Contacted` counts a person once when an act about them succeeded (enrolling them, sending a message, replying or
    sending a social invitation) or a send was said for them in the period, dated by the moment the provider gives, not the moment the span was read. A send to somebody who is not a member of
    the campaign counts nobody, and no count of messages is offered. A person's latest touch, the reason the list gives,
    is the later of their latest act and their latest send.
  - A person's timeline shows each send under All as "Sent from the account" with a mark of its own, never the reply
    mark, and the people list's Last activity says the same; a campaign's timeline shows none.
    Nothing says who sent the message, since the feed cannot tell.
  - **Reply plugin 0.18.0** reports the account's own email sends through this kind.
  - **journal-forwarder 1.6.0** declares `provider_send_observed`, so a person can grant it.
- **A list you give the chat is shown back and added by your press.** In an agent's own chat the AI can end a turn with
  a card of the list as it read it: each column and the field it fills, the row count, the first ten rows, and a
  comparison of the addresses with what you wrote in the chat, which gates nothing. Under it the card reads the agent
  and says in fixed sentences what adding does and, for a running agent under automatic sending, that a message written
  to someone added can be sent without asking once Jason's own review of it passes. **Add to this agent** is yours: one
  request for every row, answered row by row in the runtime's words. A person you took out of the agent is not added
  back, and a repeated press adds nobody twice.
- **Two cards that only lead somewhere.** Ask the chat which AI runs your agents, or how to connect Reply, and it can
  end with a card whose one button, **Open Connect** or **Open Accounts**, opens that place.
  Neither card takes a key, a sign-in or any input.
- **Archiving an agent is your own act.** `jason campaign archive` is refused in the desktop's chat in every access
  mode. In the console, the agent's Settings tab ends with an **Archive** part whose confirmation names the agent, says an archive
  cannot be undone, and says what it cancels, read from the runtime as the dialog opens: the open work, the approvals
  still asked and those already given on it, and the pending questions. Where a provider's campaign is linked to the
  agent, it says that campaign is left as it is and keeps sending if it is live.
- **A question names the thread it is about.** `decision.raise`, `decision.answer`, `decision.get` and each row of
  `decision.list` carry `thread`, the plugin and the conversation the runtime woke the asker for, or null. Only the
  runtime writes it, once, when the question is raised, and only for a responder the runtime itself made for a reply
  whose record nobody else rewrote. Needs you's Inbox holds those questions beside the answers to replies, and
  `jason decision get --human` prints a `Thread:` line. The responder checks for an open question about its thread
  before it asks again.
- **An approval keeps covering its work when the only change is an added pin.** A provider identifier the runtime
  learned after you approved, one the approved work did not hold, no longer parks the work for a second approval: the
  provider addresses a record it already holds, and an added pin is either never shown to you or a value you already
  read. A pin the approval held, an argument, a field of the person, the address, the campaign, the plugin, the account or the operation's version still
  park it.
- **The dark theme is complete.** The browser shell and the console share one ground in the dark theme, and every
  screen and dialog was walked in both themes, wide and narrow: fields, cards, notes and accent buttons read on the
  dark ground, and the dashboard's agents table scrolls inside its card. The layout faults found on the way are under
  Fixed.
- **The Windows window has no title bar of its own.** The system's minimize, maximize and close buttons stand over the
  console's top strip, in the console's ink for the theme in force, and follow a change of theme while the window is
  open. Open in and the Demo | Live switch stand left of the buttons, and the strip, with a drag band the desktop adds,
  moves the window. macOS is unchanged and Linux keeps the system's frame.
- **The desktop names the default execution profile through the runtime.** It calls `jason profile set-default`, so
  the journal says the desktop named it and the runtime puts it in force without a restart, and it reads the default in
  force from `jason profile list`, which also covers a default set by a `JASON_` variable. It writes no settings key
  of its own for this.
- **Served files.** Every file of the UI is served by the exact name its directory holds, on every system; see
  Upgrading. A build of the UI may also keep pages it does not trust under a directory named `sealed/`, which the
  runtime serves unable to act: only `.html` files may stand there, and every answer under it carries a stricter
  header set (no address to fetch, connect or frame, and no same-origin rights). `docs/ui.md` prints the set whole and
  says what it does not stop. The release's own UI has no such directory, so nothing changes for it.
- **Words of a work item's attempts.** `jason workitem list --human` heads its count `FAILED TRIES`, and
  `jason workitem get --human` prints `Attempts: <made> made, <n> counted toward the limit`, since the runtime's
  `attempt_count` has always counted failed attempts only. The JSON does not change.
- **The packages:** reply 0.18.0 and journal-forwarder 1.6.0, which need Jason 0.47.0 or newer; ntfy 1.0.3, slack 1.0.0
  and telegram 1.0.0 as in 0.46.0.

## Fixed since 0.46.0

- On Windows, `jason session open` could hand the agent a different brief or folder than Jason wrote: the console
  host replaces every `%NAME%` of a variable it knows in the command line before the program reads it. Jason now
  reads the words the host would change and, if any would change, starts nothing, names the word, and gives a line to
  run by hand. `--dry-run` refuses the same way.
- An install folder whose name holds the character that separates the entries of `PATH` (`;` on Windows, `:`
  elsewhere) was put on `PATH` as two wrong entries, so no shell found `jason` by name and `jason status` offered a
  repair that wrote the same entry again. `jason status` and `jason doctor` now say so once and offer no repair, a
  role's agent is not given the folder, `jason session open` refuses it, and `install.ps1` and `install.sh` refuse it
  before they write anything. Each installer also refuses, only where it writes the `PATH` entry itself, a name its own
  form cannot carry; use another folder or the option that leaves `PATH` alone.
- In a browser tab that Jason Desktop opened, pressing the browser's Back after opening a page from the chat (a card's
  Open, a link) brought the chat back with controls that did nothing. Back and Forward now return to the chat as it was, with its
  draft, and its controls work, in the tab and in the window.
- On the Accounts page, the provider's card did not contain its box: the view inside it ran past the card's edge. It
  now stays inside.
- A stray line ran down the right edge of People, Performance and Work items when no item was open beside the list. It
  is gone.
- On the agent's page, the send-mode rows were squeezed into a narrow column in a narrow window; they now use the width.
- Contacted undercounted a campaign that runs as a provider's sequence: a person sent the sequence's later steps was
  not counted for a period that held those steps but not the enrolment, and their last touch read as their enrolment.

## Upgrading from 0.46.0

Nothing stops and no setting is renamed. Two database migrations run at the first start, after the usual backup: one
adds an index for the new journal line and writes no row, and one adds two columns to the questions' table and fills
them for the questions already there by the rule above.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason ui install` brings the
  console's changes.
- **A custom UI build:** every file under `<data>/ui/` is now served only by its exact name, segment by segment, in the
  case and the spelling the directory holds. A request that relied on the system folding a name, as a case-blind disk
  does, gets a 404, as it already did on a case-sensitive disk. The release's own UI is not affected. A build that wants a directory of
  untrusted pages places it as `sealed/`, with `.html` files alone.
- **The packages:** update Jason before the Reply plugin. Reply 0.18.0 needs Jason 0.47.0 or newer: an older runtime
  refuses a whole page of events that holds a kind it does not know, so replies and bounces stop being read through
  events until Jason is updated. journal-forwarder 1.6.0 needs 0.47.0 or newer too: an earlier runtime rejects the
  reload over a kind it does not write.
- A send outside every provider campaign, or in a span the events left unread, is not counted; a person can ask for a
  span to be read again with `jason events backfill`.
- **The desktop is a separate install:** `jason update` does not install it. Download the installer or the disk image
  for your machine from this release, or let an installed desktop offer the update.
- **From 0.45.x or earlier,** the notes of each release in between apply as well.

## Known issues

- On Windows, these parts of the new window were checked by the system's own hit tests and by keys sent to it, and not
  seen by eye: a real drag and snap, the buttons' clicks and hover tints, the snap flyout, the transparent ground with
  the GPU on, and a maximized window at the monitor's edge.
- That Reply's feed carries a sequence's own send with its message identifier and date has not been seen on a real
  account: the Reply plugin's reading is tested against a stand-in only. A send with no date is left out, and a send
  with no message identifier is reported without one. Only email sends are reported; another channel's, if the feed
  carries them, are not claimed.
