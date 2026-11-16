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

# Jason 0.46.0

## What is new since 0.45.0

This release is about the chat and the console around it: the AI can show you what it found as cards you read, Needs you
shows one item whole beside the list, and the agent's page and Settings are drawn in the console's look.

- **Cards in the chat.** An AI turn can end with a card, and the chat draws it from the runtime's own answers, not from
  the AI's account of them. A card only shows what the runtime says and never acts for you.
  - A **choice** card puts the AI's question to you with two to five options; a click sends that option's words as
    your next message, and any later entry of yours locks the choice.
  - **Data cards** show an agent's status (its counts now and over the last thirty days), a list of people of one
    agent or of everyone, and how many approvals and questions wait for a decision.
  - An **accounts** card shows Reply's accounts as the desktop reads them at that moment and the plugin the runtime's
    routes name for messages, with a Refresh button.
  - **Chips** under the last answer offer up to three next questions, chosen from the agent's counts; a chip sends its
    words as your next message and does nothing else.
  - A block the chat cannot read is one quiet line instead of a card.
- **Needs you in two panes.** What waits is listed at the left, one line per approval or question, and the chosen item
  is shown whole at the right: who it reaches, from which account and channel, its subject and its whole body, then
  Approve, Edit, Reject and Open the chat. Filters (All, Approvals, Questions, Inbox) carry counts and an agent picker
  narrows them. Edit changes the words of a message and approves nothing: the runtime asks again with the new text.
  Reject asks for confirmation first. One number, from the runtime's own lists, is shown on the sidebar, the
  dashboard and each agent, with a plus where a further page exists.
- **The agent's page and Settings in the console's look.** An open agent takes the whole area, with the state and the
  type as pills in the bar and Add people, the move and Close at the right. Settings lists its sections at the left
  and shows the chosen one beside them.
- **Starting an agent is your own act.** In the desktop's chat, `jason campaign start` (which starts a draft agent and
  resumes a paused one) is refused in every access mode. The chat says when an agent is ready and that you press Start
  in the window; the Start and Resume dialogs say what starting lets go. The chat also leaves the sender's name and tone
  to you: they are set in Settings, and the chat does not set or ask for them.
- TODO(orchestrator): the sender's line on an approval, only if it has landed in this release
- **The default execution profile is named through the runtime.** `jason profile set-default <name>` and
  `jason profile unset-default` write and take away `Roles:DefaultExecutionProfile`, and `jason profile list` shows the
  default in force. The install guide, the handbook and the status check name these verbs. The journal-forwarder 1.5.0
  can be granted the new `default_profile_updated` line.
- **Terminals opened from Jason run this installation's `jason`.** `jason session open` passes the data directory
  explicitly and puts the folder of the opening executable first on `PATH`, so a `jason` of another installation does
  not answer in that window.
- **Reply plugin 0.17.0:** `campaign.get` names the mailboxes a sequence sends from.
- **Role skills write for a person.** What a role writes for you to read, a question, its options or a note on an
  approval, names people, messages and campaigns in words; identifiers go in the fields made for them.
- **The Work items page** approves a waiting item only once its approval has been read whole, as the chat and Needs
  you do.
- **The old demo installation's code leaves the desktop** and its packaging, since the Demo has been a sealed page
  since 0.45.

## Fixed since 0.45.0

- A quick second click on Approve in the chat could approve the next waiting message. The chat's cards now hold every
  act from a press until the lists read after it have answered, and the second click of a double click is not taken as
  a press.
- Approve on the Work items page sent the approval without reading it whole, showing a doubted send's doubt or
  checking the message against its work item; it now does all three.
- A `jason` of another installation earlier on the `PATH` could answer in a terminal that `jason session open` opened.

## Upgrading from 0.45.0

Nothing stops, no setting is renamed and no database migration runs.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason ui install` brings the
  console's changes.
- **The packages:** reply 0.17.0 and journal-forwarder 1.5.0, which Jason 0.46.0 or newer needs; ntfy 1.0.3, slack
  1.0.0 and telegram 1.0.0 are those of 0.45.0, and the skills' words change as above.
- `~/.jason-demo` and `~/Jason-demo`, which a demo of 0.44 or earlier made, stay until you delete them by hand once
  nothing runs from them, or until the Windows uninstall or Remove What Jason Placed (macOS and Linux) takes them off.
  Either takes off only a folder that carries the mark the earlier desktop wrote into it, and only once that demo's
  runtime has stopped. No release removes them on its own.
- **The desktop is a separate install:** `jason update` does not install it. Download the installer or the disk image
  for your machine from this release, or let an installed desktop offer the update.
- **From 0.44.x or earlier,** the notes of each release in between apply as well.
