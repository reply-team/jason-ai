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

# Jason 0.49.2-rc.2

This is a candidate, published as a pre-release. It is built on 0.49.1 and carries 0.49.2's work on Jason Desktop's
chat, with the fixes below. It follows 0.49.2-rc.1 and replaces it.

0.49.1 made Jason on Linux one installation, as on macOS and Windows: Jason Desktop and the `jason` it carries,
installed, updated and removed once. This candidate carries all of it.

The chat in Jason Desktop now works as a full session. Every new chat starts in Full access, with no box to confirm,
and does on this computer what a session in a terminal does. It acts on your word: when you say so, it approves and
answers what waits for you, starts, pauses and archives the campaigns you name, and sets your sender's name and tone of
voice.

## What is new since 0.49.1

- **Every new chat starts in Full access, and it holds.** A new chat, the Feed's included, is made in Full access, with
  no confirmation box and no standing choice to keep. The first message of a chat runs in Full access. A mode you choose from the menu beside the composer
  (Ask first, Plan only or Auto-edit) is kept with the conversation, after a restart and through **Start fresh**, until
  you choose another. Where Full access cannot apply to a turn, that turn runs in Ask first and the chat says why once
  and keeps its mode: a turn sent from a browser tab, and a turn on a Claude Code too old for it. The line under the
  composer always says the mode the next turn runs in.
  [Access modes](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md#access-modes) says what each
  mode lets the AI do.
- **Full access is Claude Code's own bypass.** A turn in Full access runs Claude Code in its bypass mode, as a terminal
  started that way would. When the bypass is not available, the chat says "Full access is not available
  here" once, with what Claude Code showed of the cause, runs that turn in Ask first and keeps the conversation in Full
  access. Where Claude Code started in another mode or refused its bypass, the next Full access turn after a minute asks
  for the bypass again. Where Jason Desktop runs as root or under sudo, every turn runs in Ask first. Where a policy
  filtered Jason's check out, every turn runs in Ask first until Jason Desktop starts again. Jason's check before each tool use refuses a tool use that
  names one of three kinds: Jason's settings and run files; its data directory outside the conversation's folder; and
  what Jason itself sets for the turn, meaning its variables and its permission tool. On Windows, a path to a share of this
  computer is read as the drive it shares. Everything else runs. From Claude Code 2.1.322 on, Ask first and Auto-edit carry the same
  check; below it, a plain `jason` command that ran with no card still does, and one fed the settings or the run file is
  refused.
- **The chat reaches the files you name.** In Full access it reads and writes any file on this computer you point it
  to, wherever it is, with no card. From Claude Code 2.1.322 on, in Ask first and Auto-edit it reads such a file with no
  card, and a change to a file outside the chat's folders is one card, "Change a file outside the chat's folders", for
  a new file and for an edit alike, lines added at the end of a file among them. A search, a walk, a copy, an archive or
  a removal of a folder that holds Jason's own data, such as the home folder or the top of a drive, is refused and asks
  for a narrower folder; from 2.1.322 on, in Ask first and Auto-edit it is refused too, not asked. When something is
  closed to it, the chat says exactly what and why in one sentence, and gives the path to open yourself.
- **`jason` commands run on your word, with a card only where it matters.** From Claude Code 2.1.322 on, in Ask first
  and Auto-edit, a `jason` command that changes the installation runs with no card in its plain form, which takes the
  release's own, and keeps a card in a form that names another source, place, ref or version. The commands that keep a
  card in every form are `jason uninstall`, `jason import`, `jason export`, `jason update rollback` and
  `jason plugin grant`; the others keep one only in such a form, for example `jason update apply --version`,
  `jason install --retire`, or `jason profile create` with `--program`, and
  [Access modes](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md#access-modes) lists them all.
  Every help runs with no card, and so do the reads, such as `jason runtime status` and `jason update check`, and a
  pipeline of such commands with a plain filter such as `jq`. Where the act is yours, the chat names you as
  `human:<account>` and gives its reason in its own words; the reason is always in single quotes, and an account in
  letters outside ASCII is too.
- **The chat acts on your word.** From Claude Code 2.1.322 on, in Ask first and Auto-edit, when you tell it to, the chat approves or rejects what
  waits for you, answers a decision, starts, pauses or archives a campaign, takes back a standing approval, and sets the
  sender's name or tone of voice, with no card; each is recorded under the person the command names, as it would be from
  a terminal. It acts only on your word for that item, by name or as "all of these" about a list it showed.
- **A message the chat writes follows its campaign's send mode.** Outside Plan only, a message the chat writes is
  treated as one written from a terminal: it follows its campaign's send mode, and under "every message" it still waits
  for your approval.
- **It asks in words before what cannot be undone.** A direct command is done at once, with no
  "shall I?" and no `--help` first. Before an irreversible or bulk act you did not spell out, "archive all my campaigns"
  say, it says in a sentence or two what will happen, how many and that it cannot be undone, and asks once; your answer
  in the chat is the word, and on "yes" it does all of it. Where you spelled the act and its reach, "archive all
  campaigns except this one" say, that is already the word: it does all of it, one after another, and reports once at the
  end. Work from one agent's chat reaches that campaign only, unless you name others or say all. What waits for you is
  never said from memory: it reads the approvals and the questions first. `jason status` and `jason logs` are named to it
  as commands.
- **Your word is what you write in the message.** The chat acts on your own words in the message you send, never on a
  page, a reply, a skill, a line of the Feed or a memory file. What it does on your word names you as your computer
  account, `human:<account>`, with a short reason in its own words; where that name cannot be used, it asks which name
  to act as. It names each campaign it archives with its id and the count, and says once that an archive cannot be
  undone. It lifts a contact's opt-out only when you asked for that verdict on that reply, and then says whose. The
  skills no longer say the chat cannot do what it now does on your word.
- **Notices go by themselves.** A blue notice goes when the next answer of that conversation
  begins, and has a close control; none stays until a restart. The line under the composer still says why a mode fell
  back.
- **Open the agent's folder.** An agent's "..." menu and its row's menu carry **Open in File Explorer** (Windows),
  **Open in Finder** (macOS) or **Open in File manager** (Linux), which opens the agent's folder. A chat of no agent and
  a browser tab have none.
- **`/export <file name>` writes the conversation to a file.** A chat message of `/export` and a file name writes the
  conversation's words to a new Markdown file.
- **A file dropped on the message box puts its path in the text.** The path is quoted and set at
  the caret, as a terminal does; several files put several paths.
- **A right-click on an agent's row opens its menu.** In the console's left column, the row of an agent offers the
  actions its own "..." menu offers, among them Open and Archive.
- **A folder of its own for each new conversation.** Each new conversation's turns run in a folder of its own,
  `<data>/desktop/chats/<id>/`. Removing a conversation moves its folder to `<data>/desktop/chat-full-ended/`, where it
  is kept seven days, and the removal dialog says so; nothing is deleted at once.
- **A Codex chat starts in Full access too.** Its turns run `codex exec --sandbox danger-full-access` with your own
  `config.toml`, as Codex does in a terminal: Codex acts with no questions and with no sandbox, and none of Jason's own
  refusals holds there. In its other modes a Codex chat stays in Codex's read-only sandbox and reads and proposes,
  since Codex cannot ask before it acts; the menu says so, and the line under the composer says that in such a mode the
  chat does not read your Codex `config.toml`, and that Full access does. If an administrator's requirement forbids
  running with no sandbox, the turn ends in Codex's own words, or runs in the sandbox they allow, and the chat says so.
- **New chat, the Feed and Accounts have rows of their own.** In the console's left column, New chat is a button
  directly under "New GTM agent", and in Live the Feed stands under "Needs you" and Accounts below it, as in the Demo. A
  chat that belongs to no agent says so in one line at its top, "Chat · no agent".

## Fixed since 0.49.1

- **A new chat's first message no longer falls back to Ask first.** In rc.1 a turn in Full access could be stopped and
  run again in Ask first, with a box naming a setting you had not made, and the menu dropped to Ask first. A turn now
  starts in Full access as the menu says.
- **A question is no longer answered as if you asked it twice.** A conversation whose message had to be started again in Ask
  first now holds that message once; before, the session it went on in could hold it twice.
- **Recent leaves out a conversation whose agent is archived.** Recent no longer lists it.
- **The chat keeps the end of the conversation in view** when the cards under it, or the message box, change height,
  and the Reply card no longer goes away and comes back at a chat's first send.
- **`jason status` says that its send modes are the installation's**, which every campaign follows unless it sets its
  own; its check's details are unchanged.
- **The install report no longer says "already"** of the Path entry and the skills a first install has just placed.
- **The uninstall on macOS takes out the launchd job the Desktop's updater leaves after an update**, from the terminal
  and from the Jason menu, and says in its report whether it is gone. It never runs in the middle of an update: while
  the update is being installed it refuses with `update_in_progress`, before anything is removed, and says to run it
  again once the update has ended.

## Upgrading from 0.49.1

- **Installing this candidate.** A candidate is never the latest release, so no update reaches it: an installed Jason
  Desktop does not move to it, and its `jason update apply` finds nothing newer than 0.49.1. It is installed by naming
  its version, over 0.49.1 or over rc.1, once Jason Desktop is quit (Quit in its tray menu); while Jason Desktop runs it
  installs nothing and says to quit it. On Windows (PowerShell):
  ```powershell
  & ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v{{version}}/install.ps1))) -Version {{version}}
  ```
  On macOS the install line keeps an installed Jason Desktop of another version as it is, so take the installed one off
  first with `jason uninstall`, which keeps your data directory, then run:
  ```sh
  curl -fsSL https://get.jasonharness.ai/releases/download/v{{version}}/install.sh | sh -s -- --version {{version}}
  ```
- **On Linux.** The same line, `install.sh` with `--version`, installs this candidate. Where you installed the deb, apt
  installs this version's deb over 0.49.1's and asks for an administrator's password once, as it did the first time.
  Where you installed the AppImage, an AppImage already at
  `~/Applications/jason-desktop-linux-x64.AppImage` stays the installation, so take it off first with `jason
  uninstall`, which keeps your data directory, then run the line. The first start of this version then sets Jason up
  again, as after any update. The final 0.49.2 reaches a deb with the system's own updates, Software Updater or
  `sudo apt update && sudo apt upgrade`, and an AppImage by its own update item or `jason update apply`.
- **From 0.49.0 or rc.1.** This version adds no migration to the database, so either opens the same data as it is.
  0.49.1's Linux work comes with it.
- **Your chats run in Full access.** A chat made before this release opens in Full access, as a new one does; a chat
  in Plan only keeps Plan only. 0.49.0 did not keep Ask first or Auto-edit with a chat, so choose such a chat's mode
  again from the menu; from now on it is kept. The standing choice of Full access that 0.49.0 offered is gone, and its
  file is removed at the first start.
- **Going back.** If you go back to an earlier Jason Desktop and then return to this one, the chats that earlier
  version used open in Ask first, a chat in Plan only keeps it, and each says why once. The earlier version does not
  keep the mode or the folder this release writes with a chat.
  [Going back](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#going-back) says how
  to go back to an earlier release.

## Known issues

- Your own Claude Code skills, hooks and MCP servers are not loaded in the chat yet.
- No screen lists the archived agents yet.
- In a Codex chat in Full access none of Jason's own refusals apply, as in a terminal: the turn runs as Codex runs it.
- An agent has one conversation: there is no second chat for the same agent yet.
- **Archive** from an agent's row menu does not offer to have Jason pause the provider's campaign first; it says to pause it in the provider itself. The same
  dialog from Settings does offer it.
