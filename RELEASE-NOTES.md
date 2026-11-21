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

# Jason 0.49.2-rc.1

This is a candidate, published as a pre-release. It is built on 0.49.0 and carries 0.49.2's work on Jason Desktop's
chat, with the fixes below.

It follows 0.49.1 and carries all of it: on Linux, Jason is one installation, Jason Desktop and the `jason` it carries,
installed, updated and removed once.

The chat in Jason Desktop now works as a full session. Every new chat starts in Full access, with no box to confirm.
It acts on your word: when you say so, it approves and answers what waits for you, starts and archives the campaigns you
name, and sets your sender's name and tone of voice. In the modes that ask, it asks first for anything that could let a
message go without your approval.

## What is new since 0.49.0

- **Every new chat starts in Full access.** A new chat, the Feed's included, is made in Full access, with no
  confirmation box and no standing choice to keep. A mode you choose from the menu beside the composer (Ask first, Plan
  only or Auto-edit) is kept with the conversation, after a restart and through **Start fresh**, until you choose
  another. Where Full access cannot apply to a turn, that turn runs in Ask first, the chat says why once and keeps its
  mode: a turn sent from a browser tab, and a turn on a Claude Code too old for it. The line under the composer always
  says the mode the next turn runs in.
  [Access modes](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md#access-modes) says what each
  mode lets the AI do.
- **Full access is Claude Code's own bypass.** A turn in Full access runs Claude Code in its bypass mode, as a terminal
  started that way would, with PowerShell among its tools. Jason's check before each tool use refuses a tool use that
  names one of three kinds: Jason's settings and run files; its data directory outside the conversation's folder; and
  what Jason itself sets for the turn, meaning its variables and its permission tool. On Windows, a command or a path
  that names a share of this computer by `localhost`, a `127.x.x.x` address, `::1` or the computer's own host name is
  read as the drive it shares. Everything else runs, and what Claude Code still asks about in its bypass mode is a
  card, "Full access asks about this one". Where the bypass is not available (a setting of yours or an administrator's
  turns it off, a restricted mode or a policy refuses it, Jason Desktop runs as root or under sudo, or a policy filtered
  Jason's check out), the chat says "Full access is not available here" once, with the
  cause, and runs in Ask first until Jason Desktop starts again; the conversation keeps Full access. From Claude Code
  2.1.322 on, Ask first and Auto-edit carry the same check; below it, a plain `jason` command that ran with no card still
  does, and one fed the settings or the run file is refused.
- **A Codex chat starts in Full access too.** Its turns run `codex exec --sandbox danger-full-access` with your own
  `config.toml`, as Codex does in a terminal: Codex acts with no questions and with no sandbox, and none of Jason's own
  refusals holds there. In its other modes a Codex chat stays in Codex's read-only sandbox and reads and proposes,
  since Codex cannot ask before it acts; the menu says so. If an administrator's requirement forbids running with no
  sandbox, the turn ends in Codex's own words, or runs in the sandbox they allow, and the chat says so.
- **The chat acts on your word.** In Ask first and Auto-edit, when you tell it to, the chat approves or rejects what
  waits for you, answers a decision, starts a campaign, takes back a standing approval, and sets the sender's name or
  tone of voice, with no card; each is recorded under the person the command names, as it would be from a terminal. It
  acts only on your word for that item, by name or as "all of these" about a list it showed.
- **It asks first for what could send without your approval.** In Ask first and Auto-edit, setting a standing
  approval, setting another preference, changing a campaign's send mode and recording a reply's verdict are a card
  first, and so are the commands that change the installation itself (uninstall, update, export, import, stopping the
  runtime, granting a plugin and the like). Full access runs them all; Plan only is unchanged.
- **A message the chat writes follows its campaign's send mode.** Outside Plan only, a message the chat writes is
  treated as one written from a terminal: it follows its campaign's send mode, and under "every message" it still waits
  for your approval.
- **A folder of its own for each new conversation.** Each new conversation's turns run in a folder of its own,
  `<data>/desktop/chats/<id>/`. Removing a conversation moves its folder to `<data>/desktop/chat-full-ended/`, where it
  is kept seven days, and the removal dialog says so; nothing is deleted at once.
- **Your word is what you write in the message.** The chat acts on your own words in the message you send, never on a
  page, a reply, a skill, a line of the Feed or a memory file. What it does on your word names you as your computer
  account, `human:<account>`, with a short reason in its own words; where that name cannot be used, it asks which name
  to act as. It archives the campaigns you name, or all of them, without asking which first, names each with its id and
  the count, and says once that an archive cannot be undone. It lifts a contact's opt-out only when you asked for that
  verdict on that reply, and then says whose. The skills no longer say the chat cannot do what it now does on your word.
- **New chat, the Feed and Accounts have rows of their own.** In the console's left column, New chat is a button
  directly under "New GTM agent", and in Live the Feed stands under "Needs you" and Accounts below it, as in the Demo. A
  chat that belongs to no agent says so in one line at its top, "Chat · no agent".

## Fixed since 0.49.0

- **The chat keeps the end of the conversation in view** when the cards under it, or the message box, change height,
  and the Reply card no longer goes away and comes back at a chat's first send.
- **`jason status` says that its send modes are the installation's**, which every campaign follows unless it sets its
  own; its check's details are unchanged.
- **The install report no longer says "already"** of the Path entry and the skills a first install has just placed.
- **The uninstall on macOS takes out the launchd job the Desktop's updater leaves after an update**, from the terminal
  and from the Jason menu, and says in its report whether it is gone. It never runs in the middle of an update: while
  the update is being installed it refuses with `update_in_progress`, before anything is removed, and says to run it
  again once the update has ended.

## Upgrading from 0.49.0

- **Installing this candidate.** A candidate is never the latest release, so no update reaches it: an installed Jason
  Desktop does not move to it, and its `jason update apply` finds nothing newer than 0.49.0. It is installed by naming
  its version. On Windows (PowerShell):
  ```powershell
  & ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v{{version}}/install.ps1))) -Version {{version}}
  ```
  Over an installed 0.49.0 it installs this candidate once Jason Desktop is quit, Quit in its tray menu; while Jason
  Desktop runs it installs nothing and says to quit it. On an Apple silicon Mac the install line keeps an installed
  Jason Desktop of another version as it is, so take 0.49.0 off first with `jason uninstall`, which keeps your data
  directory, then run:
  ```sh
  curl -fsSL https://get.jasonharness.ai/releases/download/v{{version}}/install.sh | sh -s -- --version {{version}}
  ```
- **The road the final release comes by.** In this candidate's release run, a published 0.49.0 took this build by its
  own `jason update apply`, from a feed served on the runner, on Windows and on an Apple silicon Mac, with signed
  packages. The final 0.49.2 comes to 0.49.0 the same way.
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

- In this candidate the chat does not yet read a file outside its own folders without asking; that comes in a later
  release.
- In a Codex chat in Full access none of Jason's own refusals apply, as in a terminal: the turn runs as Codex runs it.
- Your own Claude Code skills, hooks and MCP servers are not loaded in the chat yet.
