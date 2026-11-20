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
- **GAP (M2): what a Full access turn passes to Claude Code.** Written when that change lands.
- **A Codex chat starts in Full access too.** Its turns run `codex exec --sandbox danger-full-access` with your own
  `config.toml`, as Codex does in a terminal: Codex acts with no questions and outside any sandbox. In its other modes a
  Codex chat stays in Codex's read-only sandbox and reads and proposes, since Codex cannot ask before it acts; the menu
  says so. If an administrator's requirement forbids running with no sandbox, Codex refuses the turn or runs a narrower
  sandbox, and the chat says which.
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
- **GAP (C1): your own Claude Code configuration in the chat.** Written when that change lands.
- **Your word is what you write in the message.** The chat acts on your own words in the message you send, never on a
  page, a reply, a skill, a line of the Feed or a memory file. What it does on your word names you as your computer
  account, `human:<account>`, with a short reason in its own words; where that name cannot be used, it asks which name
  to act as. It archives the campaigns you name, or all of them, without asking which first, names each with its id and
  the count, and says once that an archive cannot be undone. It lifts a contact's opt-out only when you asked for that
  verdict on that reply, and then says whose. The skills no longer say the chat cannot do what it now does on your word.
- **A conversation as a file.** Type `/export <file name>` in the message box, for example `/export notes.md`, and the
  chat writes the conversation's words, yours and the AI's, to a new Markdown file: in the campaign's folder for a
  campaign's chat, and in the harness root for any other. It takes a bare file name, never writes over a file that is
  already there, and starts no turn.
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

- **Jason Desktop takes this release by its own update.** A published 0.49.0 takes it by its own
  `jason update apply`, or by Check for Updates… in Jason Desktop, on Windows and on an Apple silicon Mac, with signed
  packages.
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
