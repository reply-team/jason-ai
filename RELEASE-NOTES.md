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

# Jason 0.47.2

## What is new since 0.47.1

This patch release is about one thing in the console: an agent is now one place. Its chat and its page's tabs stand
under one header, in Jason Desktop's window and in a browser tab alike.

- **An agent is one place.** Its **Chat**, **People** (with how many people the agent holds), **Inbox** and **Settings**
  stand under one header that stays while you move between the tabs. The header holds the agent's name, its state and
  kind as pills, its one move (Start, Pause or Resume, with the same dialog as before) and a **…** menu. It shows only
  what it has read from the runtime, and it follows the journal's lines without a reload. The page under it no longer
  draws a header, a Close or a row of numbers of its own.
  - **Every tab has its own address:** `#/campaigns/<id>/chat`, `/people`, `/inbox` and `/settings`, so a reload, a deep
    link, Back and Forward land on the tab the address names. An old link still lands: an agent's bare address and an
    old tab's address open its first tab. The first tab is Chat wherever a chat can be made; a browser tab with no
    desktop draws no Chat and opens on People.
  - **What moved into People:** the line the page's header used to carry, how many people the agent holds and the
    provider campaign it is linked to, and **Add people**, now sit at the top of the People tab.
  - **What moved into the …:** the conversation's acts, **Rename conversation**, **Start conversation afresh** and
    **Remove conversation**. They act on the conversation and the agent stays as it is, which Remove's question says.
  - **Places with no address,** Start, a set-up chat, the Feed and the sidebar's panels, stand at `#/chat`. Leaving an
    agent for one of them adds one entry to the browser's history, so Back returns to the agent's tab with the words you
    had typed there.
  - **An agent's conversation is made at your first message or choice in it,** a send, a chip, or opening one of the
    composer's menus, which show what the new conversation will take. Opening the Chat, by a link, a reload, Back or the
    Agents list, makes nothing, and typing alone makes nothing. If making the conversation is refused, the messages not
    sent go back to the box in the order you wrote them and the reason shows in the chat.
  - **An archived agent** shows its archived pill, no move, and its tabs to read. One with no conversation says on its
    Chat tab that it is archived, and nothing is made there; one with a conversation shows it.
  - **An address naming an agent the runtime does not know** is closed with a notice, as the page closes it. When the
    desktop cannot open an agent's conversation, its Chat tab says why under that agent's header, with **Try again**.

## Upgrading from 0.47.1

Nothing stops, no setting is renamed and no migration runs.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason ui install` brings the
  console's changes.
- **Links:** an old `#/campaigns/<id>` link now opens the agent's first tab.
- **The desktop is a separate install:** `jason update` does not install it. Download the installer or the disk image
  for your machine from this release, or let an installed desktop offer the update.
- **From 0.47.0 or earlier,** the notes of each release in between apply as well.
