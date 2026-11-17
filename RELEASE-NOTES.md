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

# Jason 0.48.1

## What is new since 0.48.0

This patch release is about the first minutes with Jason: the install page offers its two ways side by side, Jason
Desktop's chat says when it waits for you and what became of what you approved, and the chat is given your memory files.

- **Two ways to install, neither ranked.** Given only Jason's address, an agent asks before it installs, and now offers
  both ways, each said by whom it suits: the command line with your own agent, if you are at home in a terminal or an
  editor, or Jason Desktop, if you want a desktop application, signed on Windows and macOS, with the terminal open to
  you later. With no preference, it offers Jason Desktop. Given the three lines under Quick start, it installs the
  command line as before and names Jason Desktop once when it hands over. The README, the install page, the Desktop's
  own page and the `operating-the-installation` skill say the same.
- **The chat says when it waits for you.** While a card under the conversation asks for your permission, the line under
  the thread reads "Waiting for your answer on the card above." instead of "Thinking...".
- **A message you send stays in view.** A message you send, and a card that asks you something, scroll the conversation
  to its foot, above the box you type in. Opening a conversation or moving between them keeps its place.
- **What you approved stays in the chat.** An approval you give on a card under the chat is kept as "You approved: ...",
  followed to the end of its work: "Jason will send it." while it is queued, "Jason is sending it." while it runs, "It
  was sent." once it succeeded, and a plain line when it did not, or when it is held again.
- **A card's heading matches its body.** The card that lists what waits for your decision is headed "Your decisions"
  when nothing waits, and "Waiting for you" is drawn only over something that does.
- **The chat is given your memory files.** Jason Desktop reads your `AGENTS.md` files itself and gives them to the agent
  at the start of a conversation: the client's to every chat, the Feed and set-up chats included, and the project's and
  the campaign's as well in a campaign's chat. A file is given again only when it changes, and all of them again after
  Start conversation afresh. The chat does not write these files: when you state a standing rule, it says the exact
  line and the file it belongs in. Only folders the runtime records are read; a file reached through a link, over
  32 KiB, or that does not answer in time is named to the agent, so the chat can tell you. What the chat may read and
  write on your computer is unchanged, and the desktop's log and conversation record keep that a file was given, never
  its words.

## Upgrading from 0.48.0

Nothing stops, no setting is renamed and no migration runs.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason ui install` brings the
  console's changes.
- **The desktop is a separate install:** `jason update` does not install it. Download the installer, the disk image or
  the Linux package for your machine from this release, or let an installed desktop offer the update.
- **From 0.47.2 or earlier,** the notes of each release in between apply as well.
