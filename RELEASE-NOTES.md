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

# Jason 0.49.2-rc.5

This is a candidate, published as a pre-release. It is built on 0.49.1 and carries 0.49.2's work on Jason Desktop's
chat, with the fixes below. It follows 0.49.2-rc.4 and replaces it. Beside the chat it carries a set of smaller
changes: the console's left column has an Agents section and a Chats section, an agent's menus open its agent host in
a terminal, Open in on Windows opens Windows Terminal, Update has a section of its own in Settings, the console says
when a plugin your routes name cannot run, `--human` says an error as a sentence, the runtime starts at logon by
default, and the Reply package needs reply-cli 0.22.0.

0.49.1 made Jason on Linux one installation, as on macOS and Windows: Jason Desktop and the `jason` it carries,
installed, updated and removed once. This candidate carries all of it.

The chat in Jason Desktop now works as a full session. Every new chat starts in Full access, with no box to confirm,
and does on this computer what a session in a terminal does. It acts on your word: when you say so, it approves and
answers what waits for you, starts, pauses and archives the campaigns you name, and sets your sender's name and tone of
voice.

## What is new since 0.49.1

- **Every new chat starts in Full access, and it holds.** A new chat, the Feed's included, is made in Full access, with
  no confirmation box and no standing choice to keep. The first message of a chat runs in Full access. A mode you
  choose from the menu beside the composer (Ask first, Plan only or Auto-edit) is kept with the conversation, after a
  restart and through **Start fresh**, until you choose another. Where Full access cannot apply to a turn, that turn
  runs in Ask first and the chat says why once and keeps its mode: a turn sent from a browser tab, and a turn on a
  Claude Code too old for it. The line under the composer always says the mode the next turn runs in.
  [Access modes](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md#access-modes) says what each
  mode lets the AI do.
- **Every turn is told the mode it runs in.** Each turn tells the AI which mode it runs in and what that mode lets it
  do, so the first turn after you change the mode acts in the new one. A turn that does not load your own Claude Code
  settings, MCP servers and plugins tells the AI so, and where a Full access turn could follow, that only Full access
  on your own sign-in loads them, so asked about your MCP servers there it can say that this mode does not load them.
- **Full access is Claude Code's own bypass.** A turn in Full access runs Claude Code in its bypass mode, as a terminal
  started that way would. When the bypass is not available, the chat says "Full access is not available
  here" once, with what Claude Code showed of the cause, runs that turn in Ask first and keeps the conversation in Full
  access. Where Claude Code started in another mode or refused its bypass, the next Full access turn after a minute asks
  for the bypass again. Where Jason Desktop runs as root or under sudo, every turn runs in Ask first. Where a policy
  filtered Jason's check out, every turn runs in Ask first until Jason Desktop starts again. Jason's check before each
  tool use refuses a tool use that names one of three kinds: Jason's settings and run files; its data directory outside
  the conversation's folder; and what Jason itself sets for the turn, meaning its variables and its permission tool. On
  Windows, a path to a share of this computer is read as the drive it shares. Everything else runs. From Claude Code
  2.1.322 on, Ask first and Auto-edit carry the same check; below it, a plain `jason` command that ran with no card
  still does, and one fed the settings or the run file is refused.
- **Your own Claude Code configuration in a Full access chat.** A Full access chat that runs on your own Claude Code
  sign-in loads your own skills, hooks and MCP servers, as in a terminal; one on a key set in Jason does not. A PATH set
  under `env` in your own Claude Code settings does not apply in the chat, nor does a variable Jason sets for the turn.
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
- **The chat acts on your word.** From Claude Code 2.1.322 on, in Ask first and Auto-edit, when you tell it to, the
  chat approves or rejects what waits for you, answers a decision, starts, pauses or archives a campaign, takes back a
  standing approval, and sets the sender's name or tone of voice, with no card; each is recorded under the person the
  command names, as it would be from a terminal. It acts only on your word for that item, by name or as "all of these"
  about a list it showed.
- **A message the chat writes follows its campaign's send mode.** Outside Plan only, a message the chat writes is
  treated as one written from a terminal: it follows its campaign's send mode, and under "every message" it still waits
  for your approval.
- **It asks in words before what cannot be undone.** A direct command is done at once, with no
  "shall I?" and no `--help` first. Before an irreversible or bulk act you did not spell out, "archive all my campaigns"
  say, it says in a sentence or two what will happen, how many and that it cannot be undone, and asks once; your answer
  in the chat is the word, and on "yes" it does all of it. Where you spelled the act and its reach, "archive all
  campaigns except this one" say, that is already the word: it does all of it, one after another, and reports once at
  the end. Work from one agent's chat reaches that campaign only, unless you name others or say all. What waits for you
  is never said from memory: it reads the approvals and the questions first. `jason status` and `jason logs` are named
  to it as commands.
- **Your word is what you write in the message.** The chat acts on your own words in the message you send, never on a
  page, a reply, a skill, a line of the Feed or a memory file. What it does on your word names you as your computer
  account, `human:<account>`, with a short reason in its own words; where that name cannot be used, it asks which name
  to act as. It names each campaign it archives with its id and the count, and says once that an archive cannot be
  undone. It lifts a contact's opt-out only when you asked for that verdict on that reply, and then says whose. The
  skills no longer say the chat cannot do what it now does on your word.
- **Notices go by themselves.** A blue notice goes when the next answer of that conversation
  begins, and has a close control; none stays until a restart. The line under the composer still says why a mode fell
  back.
- **Agents and Chats in the left column.** In the console's left column, Recent is now two sections, **Agents** above
  **Chats**, each with a small plus, under Search, Needs you, the Feed and Accounts.
  - **Agents** lists your agents by name, the one active most recently first, an agent made from the command line
    among them. Under All it shows the first ten, then **All agents**, which opens the Agents page; under any other
    filter (Needs you, Live, Paused, Draft) it shows every agent the filter keeps.
  - **Chats** lists the conversations about no agent, newest first, ten at a time with **Load more**. Such a chat
    says so in one line at its top, "Chat · no agent".
  - The two wide buttons, New GTM agent and New chat, are gone, and so is Recent's own search box: Search finds agents
    and chats.
- **A new agent at once.** The plus over Agents makes the agent at the click, named "New agent 1", "New agent 2" and so
  on, in your default project. Its row is on top and selected, and its Chat opens asking "Who should New agent 1
  reach?", with the box and the chips. There is no dialog: rename it when you like. If no AI is connected yet, your
  first message waits, with **Take back**, and goes into the agent's conversation once one is. The palette's New GTM
  agent makes the agent the same way. The plus over Chats and Ctrl+N (⌘N) start a chat about no agent, as before.
- **A row's menu, behind three dots or a right-click.** Every row of the left column shows a **⋯** button on hover and
  on focus, which opens the row's menu, as a right-click does. An agent's row offers the actions its own "..." menu
  offers, among them Open and Archive; a chat's menu has no Archive. For an agent pinned to a provider's campaign,
  where its route can pause that campaign, Archive... there offers Jason's pause first, as the same dialog in the
  agent's Settings does: ask in the agent's chat to pause that campaign, and archive once that work has finished.
- **Rename in the row.** A row's Rename makes the name a field in place. Enter keeps it, Escape or a click away keeps
  nothing, and a refusal is said under the field. On an agent's row it renames the agent itself, and the new name shows
  in the row, the agent's header and the Agents page at once; the agent's folder stays where it is. On a chat's row it
  renames the conversation. The chat's own "Rename conversation" is unchanged.
- **Open the agent's folder.** An agent's "..." menu and its row's menu carry **Open in File Explorer** (Windows),
  **Open in Finder** (macOS) or **Open in File manager** (Linux), which opens the agent's folder. A chat of no agent and
  a browser tab have none.
- **Open Claude Code, or Codex, in a terminal from an agent's menus.** After the folder item, the agent's "..." menu
  and its row's menu carry **Open Claude Code in terminal** or **Open Codex in terminal**: a new session of that host
  in a terminal at the agent's folder, the host the agent's chat runs on where this computer has it, else the first one
  found. It is not offered in a browser tab, for a chat of no agent, or where no host is found.
- **`/export <file name>` writes the conversation to a file.** A chat message of `/export` and a file name writes the
  conversation's words to a new Markdown file.
- **A file dropped on the message box puts its path in the text.** The path is quoted and set at
  the caret, as a terminal does; several files put several paths. On macOS and Linux it goes in single quotes. On
  Windows it goes in single quotes where the name holds none; in double quotes where it holds one, a typographic one
  among them, and no `$`, backtick or double quote, and is not a network path; and otherwise in single quotes with each
  quote doubled, as PowerShell reads them.
- **A folder of its own for each new conversation.** Each new conversation's turns run in a folder of its own,
  `<data>/desktop/chats/<id>/`. Removing a conversation moves its folder to `<data>/desktop/chat-full-ended/`, where it
  is kept seven days, and the removal dialog says so; nothing is deleted at once.
- **A Codex chat starts in Full access too.** Its turns run `codex exec --sandbox danger-full-access` with your own
  `config.toml`, as Codex does in a terminal: Codex acts with no questions and with no sandbox, and none of Jason's own
  refusals holds there. In its other modes a Codex chat stays in Codex's read-only sandbox and reads and proposes,
  since Codex cannot ask before it acts; the menu says so, and the line under the composer says that in such a mode the
  chat does not read your Codex `config.toml`, and that Full access does. If an administrator's requirement forbids
  running with no sandbox, the turn ends in Codex's own words, or runs in the sandbox they allow, and the chat says so.
- **The Feed and Accounts have rows of their own.** In Live the Feed stands under "Needs you" and Accounts below it,
  as in the Demo.
- **The console says when a plugin your routes name cannot run.** In Live, while a plugin that one of your routes
  names is unavailable, the bar at the top of the console says that its work does not run, with the runtime's own
  words for the problem and its repair, and a link to Accounts; past two such plugins it counts them in one line. It
  goes by itself once the plugin can run again, after a `jason plugin reload` or a restart of the runtime. A plugin no
  route names never draws it, and the Demo never shows it. Where the program a plugin runs is older than its manifest
  asks for, the problem now ends on the repair: install that version or a later one where the runtime can see it,
  and run `jason plugin reload`. `jason update` prints a line `<id> unavailable: <problem>` after its reload for each
  plugin the reload left unavailable.
- **Open in on Windows opens Windows Terminal.** `jason session open`, and every Open in of Jason Desktop that goes
  through it, opens the agent host in a new Windows Terminal window at the folder where your account has Windows
  Terminal 1.18.2681.0 or later. Otherwise the console window opens as before; where Windows Terminal does not start,
  the console window opens in its place and the command says so. A folder whose name holds a `%` opens in the console
  window. macOS and Linux are unchanged.
- **Update has a section of its own in Settings.** In Jason Desktop, Settings lists **Update** last, with the desktop's
  version, its update item and what the last check you asked for found; the line under the list is gone. From the
  moment the desktop knows of a version it has not installed until that version is installed, a quiet dot stands on
  the sidebar's Settings row and on the Update section. The notification that says what a check you asked for found
  opens Settings at Update. Where Jason Desktop is the deb, the section says that the system's packages update it.
- **A download you asked for asks to install now.** When a download you asked for ends, Jason Desktop asks once:
  **Restart and Install**, or **Not Now**, the default. Not Now leaves the download waiting under "Restart Jason and
  Its Runtime to Install". A look Jason Desktop makes by itself downloads nothing and asks nothing, and nothing installs
  by itself.
- **`--human` says an error as a sentence.** With `--human`, a refusal is printed as a sentence with its code in
  brackets, for example `Refused (campaign_not_found): The campaign cmp_X was not found.`; where no runtime could be
  asked it begins `Not reached`, one worth repeating ends "It can be tried again.", and each detail follows on a line
  of its own. Without `--human` the output is the same as before: the exact JSON body.
- **Notifications say what Windows answered, and when.** In Settings, Notifications, a toast Windows did not show is
  said as what Windows answered at that notification and how long ago, and that Windows is asked again at the next
  one. Jason Desktop reads it again each time its window takes the focus back. Where the last toast was not shown
  because a switch was off and Windows' switches now read as on, `jason status` counts the toast as reaching you
  again. Where it cannot read the switches, it words a toast Windows did not show as what Windows answered when the
  toast was last sent.
- **The runtime starts at logon by default.** Installing Jason, or updating it, registers the runtime's start at logon
  once. Nothing is started at that moment: the next logon starts it. Your turn-off is kept:
  `jason runtime autostart disable` is now recorded, and an install or an update never turns it back on. "Start the
  Runtime at Logon" is no longer in the Jason menu or the tray; "Open Jason at Login" is still there.
  - **The Demo stays first.** A new installation's first start of Jason Desktop still opens on the Demo, and nothing is
    registered then. The start at logon is registered once Live has brought the installation up.
  - **`jason runtime autostart enable` starts the runtime now** where none answers, on all three systems.
  - **What a turn-off and a stop mean is said.** `disable` says that after a restart nothing of Jason runs until
    `jason runtime start`, or until you open Jason Desktop. Your `jason runtime stop` says that it holds only until the
    next logon, while a registration stands. `jason status` says when you turned the start at logon off.
  - **The set-up's report** has a `logon` member saying what it did: `registered`, `already`, `off`, `left`,
    `no_installation`, `refused` or `unsupported`. A system that refuses never fails an install.
- **A new campaign's folder is named by when it was made**, for example `2026-11-21-143005-k3x9qa`: the moment in the
  local time of the computer the runtime runs on, then the end of its id. A rename never moves it, and a move to
  another project keeps it. Existing campaigns keep their folders. `jason campaign get --human` shows a `Folder:` line.
- **Jason and Homebrew.** Jason Desktop is not yet offered in Homebrew's tap: install this candidate as "Upgrading"
  says. On a Mac where Homebrew has placed Jason Desktop, `jason uninstall` takes off what Jason set up and leaves the
  application, its link and its state under `~/Library` to Homebrew, and its report names
  `brew uninstall --zap --cask jason`, which takes them off. On macOS and Linux, where the older Homebrew formula is
  still linked beside the `jason` Jason Desktop carries, set-up says so and stops a runtime started from it, and set-up
  and `jason status` name `brew uninstall --formula jason`, which takes it off; every command Jason prints for the
  formula now says `--formula`.

## Fixed since 0.49.1

- **A new chat's first message no longer falls back to Ask first.** In rc.1 a turn in Full access could be stopped and
  run again in Ask first, with a box naming a setting you had not made, and the menu dropped to Ask first. A turn now
  starts in Full access as the menu says.
- **A question is no longer answered as if you asked it twice.** A conversation whose message had to be started again
  in Ask first now holds that message once; before, the session it went on in could hold it twice.
- **A refused Full access turn keeps the whole conversation.** When Claude Code refuses Full access on a turn of a
  conversation it already holds, and no Full access turn has worked since Jason Desktop started or since Full access
  was last refused, Jason Desktop now runs that turn in Ask first in that conversation itself, with all of it in
  Claude Code's memory, instead of in a fresh session given a recap of it. The first refusal after a Full access turn
  worked still takes the fresh session with the recap.
- **A tool the mode takes out is no longer taken for a tool that ran unchecked.** In Ask first and Auto-edit, where
  Claude Code refuses a tool the mode takes out, PowerShell say, in a conversation that used it in Full access, the
  chat no longer says that Jason's check before each tool did not run, and Full access is still offered afterwards,
  with no restart. Such a tool that did run keeps the full warning.
- **The first turn after a change of mode acts in the new mode.** In rc.4, after a turn in Plan only, the first turn
  in Ask first could still answer that the chat was in Plan only and change nothing; it now acts in Ask first.
- **Needs you speaks only of what left in sight.** "What you had open is no longer waiting" is said only when the item
  you had open was decided or withdrawn while Needs you was on screen, not on a later visit with other items waiting.
- **A dialog from a row's menu no longer darkens the whole window.** Archive..., Pause and Start opened from a row of
  the left column drew a solid box, near black in the dark theme, over the sidebar and the chat behind the dialog;
  the page now shows through it.
- **A start at logon that Homebrew's formula registered is moved to the `jason` Jason Desktop carries** on macOS and
  Linux, where the formula is still linked beside it. Before, such a start kept bringing the formula's older runtime
  back at every logon, beside the application's own. Set-up rewrites it and starts nothing. A start naming another
  program or another data directory is left, and is said.
- **A Reply mailbox that cannot send is that mailbox's block.** Where Reply says a mailbox cannot send direct emails
  at all, its daily limit being 0 or its team's plan sending nothing, the Reply package (0.19.0) now holds that as the
  mailbox's block: the messages wait on it and go once the mailbox can send, instead of each failing. Its note says
  what to change in Reply.
- **A daily-limit block clears no later than the next midnight UTC.** Where an email was held for a later day that is
  already full, the mailbox's block used to name the end of that day, weeks out, as the moment it clears, though the
  work was still tried each day. It now names the next midnight UTC; a shorter wait is kept, and the active-contacts
  limit is not capped.
- **Jason Desktop no longer offers Reply's shared mailbox page.** Where Reply makes no link for one mailbox, the Reply
  card says the Reply CLI answered `email_link.not_available` and offers nothing else.
- **The left column leaves out a conversation whose agent is archived.** Neither Agents nor Chats lists it.
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
  its version, over 0.49.1, rc.1, rc.2, rc.3 or rc.4, once Jason Desktop is quit (Quit in its tray menu); while Jason
  Desktop runs it installs nothing and says to quit it. On Windows (PowerShell):
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
- **From 0.49.0, rc.1, rc.2, rc.3 or rc.4.** This version adds no migration to the database, so each opens the same
  data as it is. 0.49.1's Linux work comes with it.
- **Your agents and chats move into the two sections by themselves.** Nothing to do: a chat that set up an agent is
  that agent's conversation and appears under Agents, and agents made before keep their names and folders.
- **The runtime now starts at logon by default:** installing or updating registers it once, unless you turned it off
  with `jason runtime autostart disable`; nothing is started at that moment. On Windows and Linux a disable made before
  this version left no record, so run it again to keep it off. Start the Runtime at Logon is no longer in the Jason menu
  or the tray.
- **`jason uninstall` removes the start at logon's records** (`autostart/off.json`, `autostart/default.json`) even when
  it keeps the data, and `jason export` no longer carries them. An import is a new machine.
- **The Reply package needs reply-cli 0.22.0 or later.** The Reply package, now 0.19.0, and Jason Desktop's Reply card
  ask for reply-cli 0.22.0 or later. With an older one the Reply package is unavailable, `jason plugin list` says
  `executable_incompatible`, in Live the console's top bar says its work does not run where a route names it, and the
  Reply card says to update the Reply CLI and offers its install where npm is on this computer. Bring it up to date as
  it was installed, with `npm install -g reply-cli`; where npm's global folder is not your account's own,
  `npm install -g --prefix ~/.local reply-cli`.
- **Your chats run in Full access.** A chat made before this release opens in Full access, as a new one does; a chat
  in Plan only keeps Plan only. 0.49.0 did not keep Ask first or Auto-edit with a chat, so choose such a chat's mode
  again from the menu; from now on it is kept. The standing choice of Full access that 0.49.0 offered is gone, and its
  file is removed at the first start.
- **Going back.** If you go back to an earlier Jason Desktop and then return to this one, the chats that earlier
  version used open in Ask first, a chat in Plan only keeps it, and each says why once. The earlier version does not
  keep the mode or the folder this release writes with a chat. The Reply package stays at 0.19.0, which an earlier
  release, 0.48.7 among them, reads as it is, since it adds no manifest key, and which still needs reply-cli 0.22.0 or
  later.
  [Going back](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#going-back) says how
  to go back to an earlier release.

## Known issues

- Your own Claude Code skills, hooks and MCP servers are not loaded in a chat that runs on a key set in Jason, nor in a
  turn that runs in Ask first, Auto-edit or Plan only.
- No screen lists the archived agents yet.
- In a Codex chat in Full access none of Jason's own refusals apply, as in a terminal: the turn runs as Codex runs it.
- An agent has one conversation: there is no second chat for the same agent yet.
- On Windows, a `%NAME%` in the agent host's words may still be expanded by a Windows Terminal that is already
  running, over its own environment.
- On Windows, a dropped file's path whose name holds a single quote together with a `$` or a backtick, or a network
  path whose name holds a single quote, is quoted as PowerShell reads it, and Git Bash reads it back wrong.
- A Jason Desktop older than this one, showing this version's page, may still say "Its shared mailbox page can be
  opened instead." with no button after it.
