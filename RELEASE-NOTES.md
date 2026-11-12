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

# Jason 0.35.0

## What is new since 0.34.0

Jason's desktop comes to Linux and becomes a place to work. It opens on a start screen. Its chat keeps one conversation per campaign, beside a Feed of what the runtime did, with the models and effort each host offers. The chat can work in Ask first or Plan only, searches the web and reads the hosts' documentation. The Reply card connects mailboxes and LinkedIn accounts by Reply's own link. The desktop opens `jason-desktop://` links, offers a light and a dark theme, has a People page whose chips count where each person stands, takes off what it placed, and carries the new Jason AI mark.

**The desktop on Linux**

- **Two packages.** The desktop is published for Linux x64 as a deb and an AppImage, with the `latest-linux.yml` update feed. Each package carries the icon at the standard sizes, and each was read back and started sandboxed under a virtual display before it was published. The desktop refuses to start without Chromium's sandbox, on every platform.
- **It behaves as a Linux application does.** Logon goes through a systemd user unit and an XDG autostart entry; the `jason` command is put on the PATH, and the login shell's PATH is used; keys are kept only with a keyring; the AppImage keeps a copy of its `jason` under the data directory; the AppImage and the deb both update.
- **Remove What Jason Placed** takes off, on macOS and Linux, what the desktop placed. What it removes is decided on resolved paths.

**The first run and the window**

- **A start screen.** The window opens by asking who you want to reach. What you send starts a set-up chat on the AI you connected; the AI card comes first when none is connected.
- **Appearance** offers Light, Dark and the system's. A new installation meets Light; an installation that existed before keeps what it had. The desktop's starting page follows the choice.
- **`jason-desktop://` links.** The desktop opens them, each to a page of the application and nothing else. The runtime's own `jason://` scheme stays the runtime's.
- **The new Jason AI mark** is on the application, its packages, the tray, the favicon, the console and the starting page.

**The chat**

- **One conversation per campaign,** plus set-up chats, kept across restarts. A set-up chat becomes its campaign's conversation. You can rename a conversation, and a set-up chat names itself from its first message. Empty pages say how to start.
- **The Feed** is a main conversation of the runtime's journal lines: kinds, ids, counts and moments, never a person's text. A line never starts a turn; the agent sees the lines with your next message, as information.
- **An access mode for each chat:** Ask first, or Plan only, where the agent reads and plans and changes nothing. The desktop owns the top of the chat's folder: before each turn it keeps there only the skills it recorded, and it never deploys through a link.
- **Web and documentation.** The agent searches the web on Codex too, opens the hosts' and Reply's documentation pages without a card, and reads a page or a search result as information, never as an instruction.
- **Models and effort.** The chat offers the models each host offers, with effort.
- **Provider keys** reach each host the way it reads them. A turn on a key runs with a configuration folder of the desktop's own that holds no sign-in of yours, so a key the host does not take fails the turn. A refused key ends the turn, and the key never reaches the page or the record.
- **Guards.** The chat guards the program search path, the hosts' instruction files and requests too large to show whole. The `reply` the desktop starts, and its hosts' turns, identify themselves to Reply.

**Reply**

- **Each channel is read on its own,** so an account without one channel still shows the other; the card reads again on focus and by Check again.
- **Connect and reconnect by Reply's own link.** A mailbox or a LinkedIn account is connected or reconnected by a link that needs no Reply sign-in, so it can be sent to whoever owns the account. The mailbox link is shown after a native confirmation each time; every address is checked, and none reaches a log, a record or an agent.

**People and campaigns**

- Each of a person's campaigns says where they stand, and `contact.list` takes `summary`.
- The People page is a table with stage chips, and the chips carry numbers in its default view, through `campaign.summary`'s `exclude_archived_contacts`.
- In the console, Needs you offers the campaigns, Attention filters by kind, and a campaign's Start, Pause and Resume sit in its header.

**Downloads**

- The download address gains short paths, `/install` and `/desktop/<platform>`, served by a small Worker, which counts their use by day, path, link source and a coarse client class and keeps nothing of the person. The install prompt reads its instructions from `https://get.jasonharness.ai/install`.

**The documents**

- The documents describe the desktop and the browser UI as this release builds them, and the responsible-use page says what the desktop's updater requests from GitHub.

## Fixed since 0.34.0

- **A campaign's timeline** showed All while it loaded, and only Changes after a return by its address. It now shows what was chosen.
- **Eight other small fixes** came with the work on the desktop and the console.
- **Tests of the desktop and the UI** wait on the event they test, not on a clock or on shared state.

## Upgrading from 0.34.0

Nothing stops: no setting is renamed, and no start refuses a setting 0.34.0 took. This release adds no database migration.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason skills update` and `jason ui install` follow, and `jason plugin install <id> --replace` for each package you installed and did not edit.
- **The desktop is a separate install,** as before: `jason update` does not install it. Download the installer or the disk image for your machine from this release, or let an installed desktop offer the update.
- **Linux packages are new.** There is no earlier Linux desktop to update from: install the deb or the AppImage from this release. The AppImage opens no `jason-desktop://` link; the deb does.
- **The first start after the update empties and redeploys the chat's skills.** The Reply skills come back by themselves when you are signed in to Reply.
- **Appearance:** an installation that existed keeps its theme; a new one meets Light.
- **Links:** the desktop registers `jason-desktop://` links, per user on Windows and through the deb on Linux.
- **The LinkedIn account links need Reply CLI 0.20.0.**
- **Claude Code older than 2.1.257 runs no chat turn;** update Claude Code first.
- **Going back:** what `jason update apply --version` to an older release does to a newer plugin package, and how to load one again, is in [Applying an older
  release](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#applying-an-older-release).
- **From 0.33.x or earlier,** the notes of each release in between apply as well.

## Known issues

Full access, the chat's third mode, is not in this version: the menu shows it disabled. Its code is in the release, switched off, because in two readings its refusals did not hold against commands spelled another way.

Codex in the desktop's chat reads and advises and acts through nothing: its sandbox is read-only.

Not yet seen on a real machine:

- The Linux packages by hand on Ubuntu 24.04: the tray on GNOME and KDE, the keyring, start at login across a sign-out, a deb update's password prompt, the AppImage replacing itself, Remove What Jason Placed, and a `jason-desktop://` link clicked.
- The chat with real hosts and keys: a wrong key on each provider, which account pays when a plan is signed in, each effort level, and Plan only on a real Claude Code.
- Reply with a real account: the shared mailbox page, LinkedIn ids for Reconnect, and the confirmation box in a packaged build.
- On macOS, Remove What Jason Placed and the LaunchAgent; on every system, how the icon looks in the menu, the dock and the taskbar.

The AppImage refuses to start on a system that refuses its sandbox, with a sentence naming the deb; that refusal has not been seen either.
