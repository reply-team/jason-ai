# Jason Desktop

An Electron window onto the installation the `jason` command line uses. It is one more client of that installation, as
the browser UI is: it finds the data directory as the CLI does (`JASON_DATA_DIR`, otherwise `~/.jason`), shows the UI the
runtime serves at its loopback address, and changes the installation only through CLI verbs and the settings file a
person would edit.

This page is the builder's view. What the desktop is for a person, how it is installed, updated and taken off on each
system, and what its chat may run are in [docs/desktop.md](../docs/desktop.md), which carries the sentences of this page
that a person needs.

## What it does, and through what

| Step | How |
| --- | --- |
| Know whether a runtime is up and whether it is this build | `jason status` |
| Start one when none is up | `jason runtime start` (never `jason runtime run` as a child of its own) |
| Install the UI | `jason ui install`, only when the runtime is the version the desktop carries and the `ui_module` check of `jason status` is not ok; an install that fails stops the start only when no UI is served at all |
| Deploy the skills | `jason skills install --roles-only`, `jason skills install --root <data>/desktop/chat/.claude/skills --host claude_code` and `jason skills install --root <data>/desktop/chat/.agents/skills --host codex`, all from the skills package the desktop carries |
| Load the window | the link `jason ui open --no-open` prints |
| Place a notification plugin | `jason plugin install <id>`, its settings written, then `jason plugin reload` and `jason plugin list` |
| Wire Reply after a sign-in (`reply auth login`, or `--with-token` with the key on standard input) | `reply auth status --json` for the profile, `jason plugin install reply`, its settings written, then `jason plugin reload` |
| Say whether Reply is wired | `jason plugin list` shows `reply` valid, and `jason route list` names it the global default |
| Act on a Reply account, on the card's ask | one verb of `reply account` each (`src/reply-cli-account.ts`): `connect` (a LinkedIn account by the typed name, a mailbox by its provider), `reconnect`, `pending` (also read with the accounts), `limits` (a mailbox's daily limit), `pause` and `resume` (LinkedIn), `unblock` (a mailbox) and `delete`, each with `--json` |

When the runtime is another version, the UI it serves is loaded as it is and `setup.status()` says the versions differ
and names `jason update`. A `feed` in the desktop's settings is passed to every verb that takes `--feed`.

Claude Code's version is read with `claude --version` at each listing of the hosts and before each chat turn on it, never
remembered: a Claude Code older than 2.1.257 (`src/host-version.ts`), or one whose version cannot be read, is listed on
the Connect card and in the chat's host list with a sentence naming the version found, that floor and how to update it,
cannot be chosen there, and has every chat turn sent to it refused before the host starts, a key provider's included.
From 2.1.285 on (`noChromeFrom`) every turn also passes `--no-chrome`: Anthropic's changelog does not say which version
added the flag, and 2.1.285 is the first whose entry names it, so below it the turn's settings alone keep the browser out.

Codex's version is read with `codex --version` before each chat turn on it, never remembered: a Codex older than 0.122.0
(`codexFloor`), the first whose `exec` takes `--ignore-user-config`, or one whose version cannot be read, has the turn
refused before Codex starts, with a sentence that says to update Codex.

On Windows every Codex turn names the elevated sandbox for the run (`codexWindowsSandbox`): the person's own choice of
sandbox is in the config.toml the turn leaves unread, and a command in the unelevated one can read the data directory.
Naming it proves nothing (OpenAI's page says a fall to the unelevated sandbox can happen where the elevated one is not
set up), so the first Codex turn of each run of the desktop checks it (`codex-sandbox.ts`). One cmd line prints a random
marker, then a marker of each file's own only if `type` opens it, the file itself going to `nul`. The first file is a
control, the system's hosts file under `SystemRoot`, which every Windows account may read and the desktop does not
own: it opening in the sandbox shows that `type` and `nul` work there at all. Then the runtime's run file, which has an
owner-only access list of its own, and the desktop's settings file, which keeps the access list the data directory
inherits from the profile, as the database and the logs do; the desktop writes that file at each start when it is
missing (`settingsAtStart`). Each path is quoted, and a path holding
`, ; = % ^ & | < > !` or a quote is not checked, with a sentence that says so. cmd reads the line from
`JASON_SANDBOX_CHECK`, so every run sees the same bytes, and runs it three times, each within thirty seconds: by its
full path outside any sandbox, where it must print every marker; inside `codex sandbox`, with the turn's own `-c`
settings and its read-only mode, where it must print the first marker and the control's alone; and outside once more, every marker
again. Anything else proves nothing, and a read marker standing as a line of its own inside refuses. `codex sandbox`
is tried without a platform word, as the help of 0.167.0-alpha.3.2 shows it (`test/fixtures`), and, when that printed
no marker, as `codex sandbox windows`, the shape the reference names for older versions. The page says "Checking
Codex's sandbox…" while it runs. The answer is kept by Codex's file, the script it is handed, its version and
`CODEX_HOME` until the desktop quits; a check that proved nothing, or found no run file or no settings file, runs again
at the next send. A turn on the OpenAI key runs under a Codex home of the desktop's own, where the setup may not reach,
so it is refused with a sentence of its own: sign in to Codex, or chat on Claude Code. The check runs with the person's
config.toml plus `-c` while the turn leaves that file unread, since `codex sandbox` has no `--ignore-user-config`, and
the desktop does not read the file to say what in it would differ. A set-up chat's naming run on Windows names no
sandbox kind and gets no check: its shell tool is off, and it is held by the read-only sandbox and those settings alone.

**Nothing of the person's own tools reaches a turn.** A Claude Code turn runs `--strict-mcp-config` with the turn's MCP
file alone, `--no-chrome` as above, and settings that carry `disableClaudeAiConnectors: true` and deny the browser's
server by name (`deniedMcpServers: [{ "serverName": "claude-in-chrome" }]`) in every mode. Every Codex turn, Plan only
included, runs with `--ignore-user-config`: the chat does not read your Codex config.toml, so a provider or profile set
there is not used; connect a key on the Connect card. Nor are the MCP servers it names loaded, since a Codex turn asks no
one before such a tool runs. Nor are the person's ChatGPT apps (connectors), which config.toml does not hold: every
Codex turn sets `features.apps=false` (`codexConnectorsOff`), as the naming run does. The page says the sentence under the composer while Codex is the chat's host. Its sign-in
is still read from `CODEX_HOME`.

The Reply card's acts are the Reply CLI's `account` verbs and nothing else; the card offers them only from the Reply
CLI that has them (`replyMinimumVersion`), and below it says to update the Reply CLI and offers nothing. What the CLI
prints and the codes it ends with are read in one module, `src/reply-cli-account.ts`, held to a fixture taken from the
CLI's own tests (`test/reply-cli-account-fixture.ts`); what the desktop does with each code is `src/reply-accounts.ts`.
A link the CLI makes for connecting or reconnecting an account is the person's secret: the main process checks it
(https, a host name, no user, port or anything but printable ASCII) and hands it to the system browser through the
navigation lock's one way out, or, where the page asked for a copy and the link is one that expires and connects one
account, writes it to the clipboard itself; the shared mailbox page is never copied, whatever the page asks. Nothing
else of the desktop holds it. The page learns that a link was opened or copied, when it stops working and its pending
entry; the CLI's output for these verbs is never logged, recorded or put into an error.
A limit's change, a pause, a resume, an unblock and a delete ask the person first in a native box of the main process
that names the account as the list does (a connect and a reconnect make a link and ask nothing), with Cancel focused;
no window to ask on is a no, a box whose window is hidden or closed is closed and read as a no, and `delete` passes `--yes` only after the yes. Two
offers open only after a code that calls for them, each for the ask it answered and behind a box of its own: the
account's shared mailbox page (`--allow-shared-page`) after `email_link.not_available`, with the CLI's warning that the
address does not expire and lets its holder manage all of the user's mailboxes, and a forced delete (`--force`) after
`linkedin.account_in_use`. A refusal is one sentence of the desktop's, chosen by the CLI's code; a code the desktop does
not act on, known or not, is said by one sentence that carries it, and nothing of the CLI's own words is shown or logged.

## Its own files

- `<data>/desktop/settings.json`: the desktop's settings (`feed`, and the version whose skills were last deployed).
- `<data>/desktop/chat/`: the chat's working folder; its skills, Jason's and the Reply ones, are in `.claude/skills/` for
  Claude Code and in `.agents/skills/` for Codex. Its `CLAUDE.md` and `AGENTS.md` are the application's own instruction
  text, which the desktop writes again when it starts and before each turn, so an edit made to either does not last.
- `<data>/desktop/conversations/`: the chat's conversations, one record each (see below).
- `<data>/desktop/models.json`: the models Codex offered when last asked, on the person's sign-in and on the OpenAI
  key (see "Models and effort").
- `<data>/desktop/provider-keys`: AI providers' keys, encrypted by the operating system (`safeStorage`). On Linux a key
  is kept and read only where the session offers a keyring (GNOME Keyring or KWallet): where Electron falls back to its
  `basic_text` store, a fixed key, the card says so and the key is not kept.
- `<data>/desktop/claude-keyed/<provider id>/` and `<data>/desktop/codex-keyed/`: the folders a chat turn on a
  provider's key runs its host with, as `CLAUDE_CONFIG_DIR` and `CODEX_HOME`, made `0700` where the system has modes.
  No claude.ai login and no saved Codex login are in them, and a keyed turn is started without the variables that name
  another credential, a profile or a federation; the design is that a key the host does not take fails the turn instead
  of running it on the person's own plan, as built and not yet seen with a real host. A Claude Console profile kept under
  the person's own home is not moved by the folder; the key in the environment ranks above it. The key itself is never
  written there: it reaches the host in its environment alone (`CODEX_API_KEY` for Codex). Each folder keeps the
  transcripts of its provider's turns, one Claude Code folder per provider since a conversation keeps one session per
  provider.
- How a turn on a key ends. On Anthropic's own key, a turn whose host says the key came from elsewhere is stopped. On a
  key run through Claude Code (Anthropic, DeepSeek, Kimi, GLM, MiniMax), a retry or an end the host reports for a
  refused key ends the turn at once with one sentence naming the provider's card in Connect: a 401 or a failed
  authentication says the key was refused, a 403 alone that the key or the request was. On the OpenAI key, run through
  Codex, Codex's own words end the turn, since OpenAI does not say how its events name a refused key. On every keyed
  turn of either host, the host's error text passes one function that replaces any part of the key, and any token
  shaped as a provider's key, by `[key]` before the page shows it or the conversation keeps it.
- `<data>/desktop/bundle/<version>/`: on Linux, an AppImage's copy of the `jason` it carries (see "On Linux").
- `<data>/desktop/electron/`: Electron's own files (its profile, the page's storage, and in `logs/` what Electron itself
  logs), set before anything else, so a second data directory is a second desktop and nothing of it lands outside
  `<data>`.
- `<data>/logs/desktop-YYYYMMDD[_NNN].jsonl`: its log, in the runtime log's shape and under the same limits
  (`Logging:RetainedFileCountLimit`, `Logging:FileSizeLimitBytes`). `jason logs --source desktop` reads it.

A Connect card also writes, into `<data>/config/settings.json`, the keys of the plugin it configures and no other:
`Plugins:Config:<id>`, `Plugins:Grants:<id>:Http` and `Notifications:Plugins:<id>:Binding`, each section made only when
a value is written into it. The `Http` grant is written only where there is none, or where the server or receiver it
derives from changed, so a grant you wrote yourself (a server of your own for ntfy, say) outlives a new token or topic.
The Reply card writes `Plugins:Grants:reply:Exec` (`["reply"]`, only where the grant does not already hold
`reply`, so one you widened is kept) and, only where the file names no `Routes:Default` at
all, `Routes:Default` with the profile signed in (`{"Plugin": "reply", "Binding": {"profile": "default"}}` unless
`reply auth status` names another), as the golden path writes them by hand; a default you set is kept. No other
`Routes` key is ever written, and every other key is kept as it was. Disconnect, on the `slack`, `telegram` and `ntfy`
cards once configured, removes `Plugins:Config:<id>` and the plugin's entry under `Notifications:Plugins:<id>` whole, so
the plugin is no longer a channel that is on; an entry whose `Enabled` is `false` keeps it and loses only its `Binding`.
The plugin's grant under `Plugins:Grants:<id>` stays, except the Http grant derived from a server of the plugin's own,
which goes with the server: an `Http` that is exactly the one host Configure derived from it, so a later Configure
derives it again; any other, one you widened included, stays. A section the removal leaves empty is dropped; one empty
before is kept. The package stays; `jason plugin reload` runs after, and `jason plugin list` only after a reload that
exits 0.

A file the desktop cannot parse is refused and left as it is: the runtime reads comments and trailing commas, and the
desktop, which writes plain JSON, will not rewrite them away. A target is checked against the pattern the plugin's
binding declares before anything is written, and an empty one is refused. A card with no values runs only
`jason plugin reload` and `jason plugin list`, and a reload that cannot read the settings file is what
`setup.status().settingsRefused` says.

| Plugin | Token | Target | Grant |
| --- | --- | --- | --- |
| `slack` | `Plugins:Config:slack:token` | `Notifications:Plugins:slack:Binding:channel` | `slack.com` |
| `telegram` | `Plugins:Config:telegram:token` | `Notifications:Plugins:telegram:Binding:chat` | `api.telegram.org` |
| `ntfy` | none: `NTFY_TOKEN` is read from the runtime's environment only | `Notifications:Plugins:ntfy:Binding:topic`, and `server` beside it for a server of your own | `ntfy.sh`, or that server's host |
| `journal-forwarder` | `Plugins:Config:journal-forwarder:token` | `Plugins:Config:journal-forwarder:url` | the receiver's host |

The desktop never writes an `Env` grant (ntfy's `NTFY_TOKEN`) or a `Subscriptions` grant (the journal kinds the
forwarder is admitted to); each card's note says where those are set.

## The chat's conversations

A campaign has one conversation, found or made the first time it is opened, and the chat may hold any number of set-up
chats, which are about no campaign yet. Each is a record, `<data>/desktop/conversations/conv_<id>.json`: the campaign
it is about, its title, the session each agent host named for it, and what the page drew, kept as text and never as
markup (the person's words, the agent's, the steps, each card with its answer, the errors). A record is written beside
its name and renamed over it, readable by this account alone, never through a link, when a host first names its session
and at the end of each turn. It is kept within 2 MB: each text of an entry is cut to 128 KB, keeping its end after a
mark in the desktop's words, then the oldest entries are dropped and counted, and the newest entry always stays. An
entry of a kind this desktop does not know, written by another version, is skipped and counted the same way. A file in
the folder that is not a record is counted in the log and left as it was. On a Claude Code turn the desktop's file
rules deny the agent a read or an edit of the folder; a Codex turn is held only by its own read-only sandbox, which the
desktop does not narrow, so it may read the records.

- **The campaign comes from the record.** Every turn begins with a paragraph the desktop writes: which campaign the
  conversation is about, its name quoted on one line, with every control, format and separator character dropped and
  cut at 120 characters, and its folder; or that it is about no campaign yet. The person's words follow it as they are.
  A turn names its conversation and its host, never a campaign, and a turn sent to a conversation the desktop did not
  make or keep is refused.
- **A session per host.** A turn resumes the session its host named for the conversation; a session id begins with a
  letter or a digit, and a record's that does not is read as none. When that host has none, or its session is not
  found, another answers, or the resume fails before saying anything, the turn starts a fresh session given a recap, the
  last of the person's and the agent's words, at most 16 KB, set apart as earlier messages, and the page is told why
  (`fresh`). The record gives up its session only once the fresh session has said something.
- **The hand-over.** A set-up chat's turn reads every page of `jason campaign list --all-projects --include-archived`
  before and after it; a campaign counts as new when it was absent before, is present after, was created at or after
  the turn's start, and has no conversation. Exactly one new campaign, from a turn no other turn of any conversation ran
  beside, makes the chat that campaign's conversation (`attached`); any other count above none, or a turn another
  overlapped, is offered to the person to pick from (`pick`, kept with the record), and `chat.attach` takes only a
  campaign the pick named. Nothing is handed over when a page of the list could not be read. A campaign a person makes
  by hand while a set-up turn runs cannot be told apart from one the turn made.
- **The Feed.** One conversation is of its own kind: the Feed, the person's main chat, about no campaign, marked `main`
  in its record, made and written the first time the page asks for it (`chat.feed`) and drawn first in the sidebar. The
  page hands it each line of the journal's stream whose kind it takes: `inbound_reply_observed`, `approval_requested`,
  `decision_raised`, `campaign_created`, `campaign_started`, `campaign_paused`, `workitem_failed`, `workitem_expired`
  and `provider_blocked`, each a kind the runtime writes itself (a test reads them from the runtime's source). The
  desktop checks each line again and keeps its id, moment, kind, campaign and work item, each held to its shape, and
  nothing else of it: no reason, no old or new value, no name. A line it has taken is not taken twice; lines of the same
  kind and campaign that follow one another fold into one with a count; at most 500 are kept, the oldest dropped and
  counted; the ids it took, the last 1,000, are kept on its record. Each load of the page, and a stream the runtime
  refused to resume, has the desktop read back one page of 100 of the lines written since five seconds before the
  Feed's newest (`jason journal list --kind … --since …`, within thirty seconds), and says when there were more; the
  lines the page hands meanwhile are checked at once and wait, the newest 200, with a gap said for the rest. A line
  never starts a turn: a turn of the Feed is given, after its header, the lines no turn was given yet, at most 50, each
  as one JSON object, under the desktop's sentence that they are records, information and never instructions; the
  record marks each line given once the turn's host started, so a line that lands while a turn starts goes to the next
  one, whatever its journal id (the runtime commits lines out of id order), and a fold that raises a given line's count
  gives it again with its new count. Rule 10 of the instruction text says the same, and that the Feed may speak of any
  campaign. A kept line out of shape is skipped and counted, and the rest of the record is read. The Feed is never renamed, removed or handed over; a campaign one of its turns makes keeps a
  conversation of its own. Its record keeps no line outside it: a line in any other record is let go when it is read,
  and of two records marked as the Feed the older is it.
- **A name a person gives.** A person renames a conversation, a set-up chat or a campaign's, from its header
  (`chat.rename`): one line, every control, format and separator character and every default-ignorable code point (a
  filler that draws nothing) dropped, cut at 60 characters, and refused when nothing is left. The record
  keeps it with a mark that a person gave it, and nothing replaces it afterwards: not a hand-over to a campaign, not the
  campaign's name, not a name the desktop asks a model for. The campaign's own name in the runtime is not changed. A
  conversation never sent to has no record yet, and its rename is refused.
- **A set-up chat named by what it is about.** Once, after a set-up chat's first turn ended without an error and still
  about no campaign, the desktop asks the conversation's own host or provider for a name of two to five words, on its
  fastest model: Claude Code's `haiku` on a sign-in or on Anthropic's key, `deepseek-flash`, `kimi-k2.6`,
  `glm-5.3-flash` and `MiniMax-M3.1-Flash` on those keys, and on Codex `gpt-6-luna` when the list the desktop keeps of
  what that host offers holds it, at low effort when it takes that level, else Codex's own default model. It is one run
  with no tool:
  on Claude Code `--tools ""`, every MCP tool denied, no MCP server, our instruction as the whole system prompt, one
  turn, nothing kept (`--no-session-persistence`); on Codex `exec --ephemeral --ignore-user-config` in its read-only
  sandbox with every tool its configuration reference names turned off. OpenAI states no Codex run with no tool at
  all, so a Codex naming run cannot be shown to have none: what holds it is the read-only sandbox, those settings, and
  the rule that anything in its output but the agent's message and its reasoning refuses the name, whatever its type,
  as anything but text and thinking does on Claude Code. The run obeys what a turn obeys: Claude Code's version is
  read first and a host below the floor is not asked, a keyed run has its provider's environment and folder, the first
  1,000 characters of the message go on standard input as one JSON string, never on the command line, and an inherited
  `CLAUDE_CODE_EFFORT_LEVEL` is withheld. It runs in an empty folder under `<data>/desktop/turns/`, removed after, one
  run at a time across the desktop, ended at 30 seconds, and ended as a stop ends a turn's host when the chat's turns
  end (the window closes or the desktop quits), its folder removed with it; one that waits then never starts. What
  comes back is untrusted: it is kept only as two to five plain words of letters and digits, each beginning with a
  letter or a digit, and anything else, a step, an error or the bound keeps the first words of the
  message, with nothing said on the page. A name the model gives is the desktop's: a person's rename still replaces
  it, and so does a later hand-over to a campaign. **Naming costs one small request of the person's plan or key per new
  set-up chat**: on a sign-in it counts against the plan, on a key it is billed to that key. A Codex naming run costs
  more than Claude Code's: it keeps Codex's built-in instructions, and when the kept list does not offer Codex's fast
  model it runs on Codex's default model at its default reasoning effort. The log says how each run ended in one word.
- **Not a boundary.** The instruction text asks the agent to keep to the conversation's campaign and to create at most
  one campaign in a set-up chat; a command that names another campaign is not refused for being in this conversation.
  What a turn may do is the permission model's to decide, as before.
- **Web pages and searches.** On Claude Code a search needs no card, and neither does a documentation page of four
  hosts: Claude Code's under `https://code.claude.com/docs/`, OpenAI's under `https://developers.openai.com/codex/`,
  `/api/docs/` and `/api/reference/` and under `https://learn.chatgpt.com/docs/`, and Reply's on `https://docs.reply.io/`
  (`documentationPages` in `src/chat-permission-model.ts`), over https, with no query string, no path segment that
  begins with an underscore, and at most 300 characters. Every other page asks, a page of Claude Code's own preapproved
  documentation set included. On Codex every turn searches OpenAI's cached index with no card and opens no page live;
  the person's own search setting is set aside for the run, also when they had turned search off.
- A conversation whose turn runs is not removed, started afresh or attached; each is refused with a sentence.
  "Start afresh" drops the hosts' sessions and keeps the record. `chat.list` and `chat.open` say whether a
  conversation's turn runs. When the page is loaded again while a turn runs, the turn shows running with Stop, and a
  card that waited is drawn as ended, since the record keeps no request to answer it by. A conversation is kept from its first turn: one made and never sent to is not listed and
  leaves no record, and starting it afresh writes nothing.
- **A refusal is a value.** Each chat call the desktop refuses answers `{ refused }` with its sentence alone, and a
  call with nothing to answer answers null; only what is no refusal rejects. Electron would put "Error invoking remote
  method" and the channel before a sentence thrown across the seam.
- **What never carries one.** `jason doctor` carries none of it, its count and size included. The desktop's log says
  how many were read, how many files were not records, a record's error code when one could not be written, how many
  new campaigns a set-up turn found and why a turn started afresh, and nothing else. `jason export` carries no part of
  `<data>/desktop/`, so conversations do not travel to another machine, nor do the transcripts of turns on a provider's
  key. `jason uninstall --purge-data` removes both with the data directory; the transcripts the agent hosts keep of the
  chat's other turns are theirs, in their own folders, and are never touched.

## The chat's access modes

Each conversation has a mode, chosen from the menu beside the composer (`chat.setMode`, `src/chat-modes.ts`). The mode
applies to the turns a person starts by sending, from the next one on.

- **Ask first** is the mode of every new chat, and no setting changes that. Claude Code starts in its own default
  mode; what the permission model does not settle is a card. Codex runs in its read-only sandbox, as always.
- **Plan only** reads and proposes and changes nothing. Claude Code starts with `--permission-mode plan`, its settings
  add `"useAutoModeDuringPlan": false` so no classifier runs a command, `ExitPlanMode` is taken away by a bare deny
  rule, and the allow list is replaced by `chat-plan-list.txt`: the allow list's verbs that change nothing, written by
  the CLI's tests, where each verb of the allow list is declared a read or a change. The desktop refuses every other
  request, with no card, but two kinds of card: reading a file or a folder outside the chat's folders, and opening a web
  page. Every deny rule stays. Codex runs as in Ask first. Plan only is kept on the conversation's record (`mode`), so
  it stays with the chat after a restart, an update and Start afresh. A Plan only turn that ends with no error, not
  stopped, outside the Feed and with words last marks those words its plan, `plan: true` on the kept text entry and on
  the turn's `done`; an older page or desktop ignores the field. Words too long to keep whole are no plan: the cut that
  fits them to the record takes the mark off. The mark goes from every entry at each turn's end, at
  Start afresh and when the chat leaves Plan only, so the page's card for it is never offered twice. The card's go is a
  send that names `mode: 'ask_first'`, and the desktop refuses a send whose `mode` is not the chat's mode now, before
  any host starts.
- **Full access** runs commands and changes files on this computer without asking, except what Jason cannot read plainly. It is given only after the person says yes to the desktop's own
  confirmation, a message box the main process shows, with Cancel focused and Escape answering Cancel, each time Full
  access is chosen while no standing choice stands. Its buttons are "Give full access this time" and "Always on this
  computer"; Always writes the standing choice to `<data>/desktop/full-access.json`, one entry per host program
  (`claude`), with the consent number (`fullAccessConsent`), the moment it was given and whether new chats start in Full
  access (the box's checkbox, ignored with This time, as its label says). An entry whose consent number is not the
  current one, lower or higher, is no choice. The file is among the known entries of `<data>/desktop/`, so the data rule
  refuses a chat's read or change of it in every mode. The standing choice skips the box for every Claude Code chat; with
  new chats, `chat.create` given a host that can take Full access opens the grant at once, and a host that cannot leaves
  the chat in Ask first with no notice. No channel of the page gives it: `fullAccess.standing` reads it and
  `fullAccess.takeBack` removes it, leaving the chats in Full access as they are. It is refused before any box where it cannot apply: on Codex, and on a Claude Code older than
  2.1.322 or whose version cannot be read, the menu shows it disabled with the reason and the desktop refuses it. The
  page draws a line under the composer from the mode the desktop holds, never from what it asked for: "Full access: the agent runs commands without asking, except those Jason cannot read." and a Change button. The confirmation says what the agent can reach, that Jason still asks about a command it cannot read plainly, that Jason refuses the agent's commands that approve, reject, answer or clear for you while a program the agent writes and runs can still act as you, that a question left unanswered for thirty minutes is not run, that the risk is the person's, and where the agent works. Full access is
  held in memory alone, for that conversation, and gone when the window closes or the chat is started afresh: a record
  never says it, and one that says so is read as Ask first, so every chat opens in Ask first after a restart; the standing choice only skips the box.
- **How Full access runs.** Claude Code starts in its default permission mode, and the hook answers allow, ask or deny for every tool use. Anthropic says a Bash rule "isn't a security boundary around the
  program", so every deny rule is passed as in Ask first, and the turn's settings, in a file under
  `<data>/desktop/turns/` beside its MCP file and removed with it, add a `PreToolUse` HTTP hook on every tool with
  `"onFailure": "block"`, `"disableAllHooks": false` and the hook's address in `allowedHttpHookUrls`, so no project file
  can switch the hook off or filter it. The hook posts each tool use to the turn's loopback endpoint, behind the turn's
  secret, and the desktop answers by one judgement (`fullAccessJudgement`), which the broker shares for the few calls
  Claude Code still asks about under bypass:
  - **refused** where the permission model refuses, or where a file being written shows what it refuses;
  - **held**, a card titled "Full access asks about this one", wherever Ask first would have
    shown a card because the model cannot rule the act out or the act would change what an agent loads: a `jason` that
    is not the program of a plain `jason <verb>` (handed to `xargs`, inside an interpreter's one-liner or a list literal,
    behind a variable or an expansion), `eval`, a script piped into a shell or an interpreter, a search variable, a
    guarded name (a person's memory files in a campaign's or a project's folder, a folder's marker, a repository's
    hooks, the files Claude Code reads MCP servers from), a `claude mcp` or `codex mcp` command, a local file named
    jason, a `~` or a glob that could expand into the data directory, and every call of a tool of the person's own MCP
    server; and, of what the host still asks about, a deletion;
  - **runs** with no question for what the model allows, the tools that run nothing (skills, web search, subagents, the
    task list) and the plain cards: a command it read, a write in or outside the working folders, a page, a read outside
    the folders.
- **Full access asks about some acts.** A command Jason cannot read plainly, or one that changes what an agent loads, is
  a card titled "Full access asks about this one"; it waits thirty minutes, then is not run and the turn goes on. A
  deletion on a critical path, and a tool the hook never saw, are cards too. A held card offers no "Always", and an
  earlier "Always" grant from Ask first never lets a held command through: each hold is the person's to answer every time.
- **What Ask first refuses is refused in Full access, with no card.** Every `reply` command is refused there but its exact
  no-card forms.
- **PowerShell is not a tool of the chat.** The judgement would still hold any PowerShell command that reached it, behind
  a switch that is off (`powerShellRunsInFullAccess` in `src/chat-modes.ts`).
- **What the reader does not see.** A program the agent writes to a file and runs is not read, and neither is the
  person's own shell configuration (aliases, shell options). Neither is an MCP server or connector of the person's own:
  none reaches a turn.
- **The agent's folder.** In Full access the agent works in a folder of its own. When Full access ends the desktop says
  "Full access has ended. The folder the agent worked in is kept for seven days and then removed: <path>", with an Open
  folder button, and removes it after seven days. Added campaign or project folders whose `.claude` holds skills,
  agents or commands are left out of a turn, and the chat says so once.
- **A re-spelled refusal is still a refusal, in every mode.** Before judging, the deny-list commands, the operations by
  their dots, the confining variable and the data directory's names are looked for a second time over a normalised text:
  quotes and a backslash inside a word removed, a bracket, a comma or a parenthesis read as a space. `appro\ve`,
  `approv''e`, `approval.appr''ove` and `['jason','approval','approve']` are refused as their plain spelling is, in Ask
  first too, and so is the content of a file being written in Full access. The same look reads through an empty `$''`
  or `$""` inside a word, a `$'\xNN'` string, a line continuation and a brace group (`{approve,}`; a word of more than
  64 expansions is refused, never judged on the first ones), and reads `\n`, `\t` and `\r` in a string as the space they print.
  What the plain spelling would not refuse stays a card in Ask first. An
  `env` that clears the environment (`-i`, `-`, `-u`, `--unset`) or `exec -c`, in a command part that runs a jason,
  clears the confining variable without naming it, and is refused in every mode; an `env` elsewhere in a command list
  is not.
- **The chat's own names, by every route.** The chat's instructions, its `.claude`, `.agents` and `.codex` folders and its
  `.mcp.json` directly in the chat folder are refused in every mode to a shell command as to the file tools, and the
  host is denied edits of them (`chatRules`).
- **The files and commands that add an MCP server are a card each time.** A write of `.mcp.json` anywhere else, or of
  `.claude.json` anywhere (Claude Code keeps the user's and the local scopes' servers there), is a guarded name's card,
  and so is a command that runs `claude` or `codex` with the word `mcp` after it (`claude mcp add`, `codex mcp add`,
  through any folder or `npx` package name, and as the shell would read a re-spelled word). None is covered by "Allow
  for this chat", and Full access holds each.
- **A tool of the person's own MCP server is a card each time.** No such server reaches a turn in this version; the
  fence stands anyway. A tool named `mcp__<server>__<tool>` of any server but the chat's own is judged before the
  command check, so an argument named `command` or `file_path` is never read as a shell command or a file tool's path:
  the card is "Use one of your own tools", its detail the server and the tool, then the input exactly as the host sent
  it (`JSON.stringify`), never cut. Before the card, an input whose detail would be longer than the card shows whole
  (4,000 characters) is refused, and so is one any of whose strings, at any depth and the keys of its objects among
  them, is refused when read as a command's text: a deny-list verb or operation, plainly or re-spelled, `JASON_CONFINED`,
  the data directory's variable or the data directory in any spelling, the settings file and the run file included.
  "Allow for this chat" is never offered for it, Plan only refuses it, and Full access holds it on every call.
- **The chat folder's top is the desktop's, in every mode.** What the desktop placed in it is recorded outside it,
  under `<data>/desktop/skills-records/`, which the chat neither reads nor writes: the record `jason skills install`
  writes of each deployment, put beside the skills only while the CLI runs and moved out after, and the Reply skills'
  files with their digests, recorded for the folders the Connect card copies in that call and no others. At the start
  before anything is deployed, and before every turn (`src/chat-agent-folder.ts`), the top is put right: `.claude/` and
  `.agents/` hold their `skills/` and in it only the files those records name with the bytes recorded; everything else
  there is removed, a settings file and a record beside the skills included; `.codex/` and the memory files the desktop
  does not write (`CLAUDE.local.md`, `AGENTS.override.md`) are removed. Nothing below the top is looked at in this
  version: an agent folder or a memory file in a subfolder is not removed, and a host may read instructions from a
  folder it works in (in Ask first every write there is a card). A link is removed as a link, never walked, and a deploy
  never goes through one: `.claude`, `.agents` and their `skills` are each made a real folder before anything is
  written into them, so nothing outside the chat folder is deleted or written. A chat skills folder whose deployment
  the desktop keeps no record of, an installation's first start of this version included, is emptied and deployed
  afresh at the start, and the Reply skills' record goes with it, so they are copied in again by the step after. A start
  whose pass over the top cannot finish deploys nothing into the chat folder, the Reply skills included, and marks
  nothing as deployed: the next start tries again, and a turn refuses on the same folder anyway. While no record of the Reply skills is kept, a start whose `reply auth status` says
  signed in deploys them by the sign-in's own routine and records them, in the same step as Jason's skills; a
  `reply-*` folder that stood there is removed and replaced by the Reply CLI's copy, never kept, and an install that
  fails is logged by its exit code, the start goes on without them, and the next start tries again. A turn does not
  start on a folder that could not be put right.
- **When the hook did not run.** The first tool that runs without the hook having seen its use ends the turn and returns
  the chat to Ask first, and takes back the standing choice: that tool has already run. **The limit:** the refusals read what a command's text and a tool's
  input show; they are not a sandbox, and a program the agent writes and runs acts with the person's account. The
  confirmation says so.
- **Auto-edit** is Ask first, except that the six file commands run with no card on paths in the chat folder
  (`autoEditJudgement`, `src/chat-permission-model.ts`, over `src/chat-auto-edit.ts`). Its command line is Ask first's,
  byte for byte: no `--permission-mode`, no `acceptEdits`, no `defaultMode` and no hook; the permission tool alone
  decides, and the broker uses `autoEditJudgement` in place of `judge` for a turn started in it. That judgement returns
  `judge`'s result unchanged unless `judge` says ask, the tool is `Bash`, and the literal reading (`readLiteral`, with
  `~` held) is a run of the six commands joined by `&&`, `;`, `||` or new lines, each named bare, with nothing piped in
  or redirected and no folder of its own; then it allows. Every option is read wherever it stands before `--`, since
  GNU's tools take one after an operand; an option outside the list, an abbreviated long option, a lone `-` or an empty
  word leaves the card:

  | Command | Options it runs with | Operands at least |
  | --- | --- | --- |
  | `mkdir` | `-p`, `-v`, `--parents`, `--verbose` | 1 |
  | `touch` | `-c`, `--no-create` | 1 |
  | `mv` | `-n`, `-v`, `--no-clobber`, `--verbose` | 2 |
  | `cp` | `-n`, `-v`, `-p`, `--no-clobber`, `--verbose` | 2 |
  | `rm` | `-d`, `-v`, `--dir`, `--verbose` | 1 |
  | `rmdir` | `-v`, `--verbose` | 1 |

  Each operand must hold no `..` component, pass the spelling check Ask first applies (`judgeSpelling`), name no device
  on Windows (`con`, `nul`, `com1`, `lpt1` and the like, with any extension), and lie strictly inside
  `<data>/desktop/chat` both as written and once links are followed (`located`), the folder itself excluded; the names
  an agent loads (`guardedPlace`, `namesLocalJason`) and every refusal over the resolved words stay as in Ask first.
  A link or a junction that leads nowhere, at the operand or at any folder above it in the chat folder, is a card: the
  scope's `entry` (`entryOf`, `src/chat-real-path.ts`) finds something there that the real path reader cannot follow,
  and a `touch` or a `cp` through it would write where it leads. A scope without `entry` runs nothing unasked. For a
  `mv` or a `cp`, the path it writes when its last operand is a folder, that operand with each source's name after it,
  is judged as one more operand for every copy and move, whatever the last operand is at the judgement, since an
  earlier part of the line may make it a folder; and a `mv` whose destination or written path ends in the first part of
  a guarded name of two parts (`.github`) is a card, since the folder moved there may hold the rest. A file named like
  one of the six (with a program extension on Windows, folded where names fold) or like jason, directly in the chat
  folder, is a card when an operand names it or when the command runs that program and such a file is already there:
  where the search path holds the current folder the shell would run it in place of the system's.
  The chain rule: once a `mv` or a `cp` names or writes a path, no later operand of the same command line may lie
  inside it, since what the earlier part made there is not what the judgement saw. Auto-edit is refused on Codex (`codexAutoEdit`), on a
  host that does not drive the chat and for the Feed (`feedAutoEdit`); each host's listing carries `autoEdit`, the
  reason or null, and the menu shows the item disabled with it. It has no version floor, no confirmation, no switch and
  no standing entry; it is held in memory alone, for that conversation, like Full access, so a record never says it, a
  restart opens Ask first, Start afresh leaves it, and closing the window forgets it; a Codex send takes it back to Ask
  first. The page draws its line under the composer from the mode the desktop holds, with the same Change button as
  Full access's. **The limits:** the chat folder is shared by the chats that are not in Full access, so a chat in
  Auto-edit can remove a file another chat left there; a hard link already in the folder is not told by its path, so a
  command that writes through it reaches the file it shares; and the judgement is made before the command runs, so a
  link put into the folder by something else between the two is the same limit Ask first's Write has.
- A change to a mode narrower than the running turn's stops that turn, narrow to wide being Plan only, Ask first,
  Auto-edit, Full access, and the page gives the waiting message back as Stop does: no turn follows by itself. So
  leaving Full access, or leaving Auto-edit for a narrower mode (Ask first or Plan only), stops the turn that runs in
  it, and a change to Plan only stops any running turn, so the menu never says a mode while the running turn could
  still do more than it lets; a change to a wider mode, Auto-edit to Full access included, lets the running turn finish
  in the mode it started in.
- **No turn starts by itself.** Every turn is a person's send:
  - a message the person sends while a turn runs waits in the page and goes as the next turn once that one ends well, in
    the mode of the moment it goes;
  - a resumed host session is the same send started again with a recap, in the same mode, and stops with it;
  - a set-up chat's naming run is no turn: it has no tool at all, no hook and no permission mode, in every mode;
  - a line the Feed takes never starts a turn: it reaches the agent with the person's next message, in the mode of that
    turn. The Feed takes the modes as any chat does.
- The log says each change of mode by its two modes (`chat_mode_changed`), the mode of each turn started and of each
  request settled, each refusal of the hook (`chat_request_decided`, refused, by the model, in Full access), a request the hook held (`chat_request_held`), and a turn
  stopped because a tool ran unchecked (`chat_turn_stopped`, `unchecked_tool`); never a conversation's id or title, a
  command, a path or a person's words.

## Models and effort

The chat's box picks the host, a model the host offers, and where that model takes one an effort; each reaches the
turn as the host's own flag, and none is passed when none is chosen, so the host keeps its own default. The conversation's
record keeps what its last turn was sent with, and the box shows it again when the conversation is opened.

| Host | The models offered | Effort |
| --- | --- | --- |
| Claude Code, and Anthropic's key | the vendor's aliases `opus`, `sonnet`, `haiku`, `fable`, which "point to the recommended version for your provider and update over time"; beside them a model's full name the person types, passed as it is, with no effort | `--effort`: the five levels on each alias; on `haiku` only from Claude Code 2.1.293, the version from which, on the Anthropic API, the alias names a model that takes them (the rule is the Anthropic API's: on another provider the box may offer a level Claude Code does not apply) |
| Codex, on the person's sign-in and on the OpenAI key | what `codex app-server` answers to `model/list`, kept per host id (see below) | `-c model_reasoning_effort=<level>`: the levels each model lists among the five |
| DeepSeek, MiniMax, Kimi, GLM | the desktop's table of each provider's models | `--effort` where the vendor's guide names levels: DeepSeek `low`, `high`, `max`; MiniMax the five; Kimi and GLM none |

The effort is one of five words, `low`, `medium`, `high`, `xhigh`, `max`, and the desktop passes no other: never
`ultracode` or `ultra`, which run more agents rather than set a level. A send is refused before any host starts when
its effort is another word; when its model is not one the host offers by the table or the list kept, or, on Claude
Code and Anthropic's key alone, a typed name of the model shape, so a model chosen on one host never reaches another;
and when its effort is not one the model named takes (the host's own default model, a typed name and a model no list
names take none). On Claude Code the effort is decided after the version is read, as the floor is. A turn that names an
effort is started without an inherited `CLAUDE_CODE_EFFORT_LEVEL`, which Claude Code ranks above `--effort`.
DeepSeek's retired `deepseek-v4-flash` is sent, and read from a record, as `deepseek-flash`, the name its page gives
now. A model a conversation kept that its host no longer lists is shown as no longer listed, and its turn goes on the
host's own model with no effort; while "Another model…" is chosen with no name typed, nothing is sent.

Claude Code documents no command that lists its models, so its list is the desktop's own table of the aliases, and
nothing is asked of Claude Code in the background. Codex's list is asked of the host: `codex app-server` is started
with no message, as a turn of that host id is started (found on the PATH, no shell, the chat folder, the turn's
environment, and on the OpenAI key the key and the desktop's own Codex folder), and sent `initialize`, `initialized`
and `model/list` page by page, each after the answer before it; no thread or turn is started, so nothing reaches a
model. The lists are kept in `<data>/desktop/models.json`, one per host id since OpenAI says an API key's models follow
its organization and project, shown at once from what is kept, and asked again in the background when the hosts are
listed, one ask at a time, only for a Codex on the PATH and an OpenAI key stored.

- **Only a list read counts for the day.** A host whose list was read is asked again once a day. An ask that is
  refused, answers what cannot be read, ends early or passes its bound keeps the last list read as it was, is logged by
  its kind alone, and is kept in memory only: its host is asked again at the next start, or an hour later while the
  desktop runs. Where no list was ever read, the box keeps Codex's model control with its own default alone and one line
  that says the models could not be read, or are being read.
- **The bounds.** An ask ends at 30 seconds, after 20 pages, past 200 models, or on a line longer than 1 MB, each read
  as unreadable but the first. A kept list of more than 200 models, or a file larger than two such lists could take, is
  not read, nor is a list the desktop cannot read whole; an entry is shown only when its name is one the desktop passes
  to a host.
- **Quitting.** When the desktop quits, or its last window closes, an ask that runs is ended as a turn's host is, and
  the asks that wait are dropped.
- On a Claude Code turn the desktop's file rules deny the agent the file.

## The window

- The page is the runtime's own, at `http://127.0.0.1:<port>`; navigation is locked to that origin. A link or
  `window.open` that leaves it goes to the system browser only when it is `https:`.
- `contextIsolation` on, `nodeIntegration` off, `sandbox` on; no webview attached, and of the permissions a page asks
  for only `clipboard-sanitized-write` is granted, to the runtime's origin alone, so a Copy button works.
- The preload exposes `window.jasonDesktop` and nothing else. Every call is answered only when it comes from the top frame
  of a page at the runtime's origin (`src/ipc.ts`); a subframe, another origin and a `file:` page are refused.

## Links

A `jason-desktop://` link opens a page of the application and does nothing else: no command, no turn, no text put into
a chat, no card, approval or question answered, no chat mode, no setting (`src/deep-link.ts`). The scheme is the
desktop's own on every platform. `jason:` is the runtime's: on Windows its toast host registers it for the command line,
which handles `jason://attention...` ([`docs/notifications.md`](../docs/notifications.md), section 7); the desktop never
registers, declares or removes it. Where the desktop's registration names a program that is there, the command line
hands the same list or item on as `jason-desktop://attention...`, made from the parsed link, and opens the browser UI
otherwise. The addresses are a closed table:

| Address | Opens |
| --- | --- |
| `jason-desktop://feed` | the Feed |
| `jason-desktop://needs` | Needs you |
| `jason-desktop://campaigns` | Campaigns |
| `jason-desktop://campaign/cmp_…` | that campaign's page |
| `jason-desktop://approval/apr_…` | Needs you, with that approval open; deciding it stays a person's click |
| `jason-desktop://question/dec_…` | Needs you, with that question open |
| `jason-desktop://workitem/wi_…` | Work items, at that item |
| `jason-desktop://person/cnt_…` | People, at that person |
| `jason-desktop://attention` | Needs you, as a toast's click on the list hands it on |
| `jason-desktop://attention/approvals/apr_…` | Needs you, with that approval open |
| `jason-desktop://attention/questions/dec_…` | Needs you, with that question open |

An id is its prefix and 26 uppercase Crockford characters; one trailing `/` is taken, and anything else (a query, a
fragment, a percent escape, another page word, a second id, more than 72 characters) opens nothing. The page receives
the page the desktop parsed, never the address. The log has three lines for links, none of which holds the address:
`deep_link_opened` with the page, `deep_link_refused` with the refusal's code, and `deep_link_registration_failed`
when Windows did not take the registration.

- **Windows.** The installed application registers the scheme at each start under
  `HKEY_CURRENT_USER\Software\Classes\jason-desktop`, with the command as Electron writes it,
  `"<...>\Jason.exe" -- "%1"`: the executable quoted, the `--` bare, the address quoted. What keeps an address from
  acting as a switch is that `--`, which ends Chromium's switches; the packaged build's fuse that refuses Node's
  inspector options; and the desktop's own pick, which takes a link only when the arguments are the address alone or
  `--` and the address. A start whose arguments carry anything beside them (a switch, a flag of the desktop's own, a
  second address, or the pieces a quote in a crafted address splits it into) opens nothing and quits; a start with
  `--no-sandbox` is refused before that. Electron's own check of a link's arguments stops at the first `--`, which
  comes first here, so it plays no part. The uninstall takes the registration off when it names this application.
- **macOS.** The bundle declares the scheme in `Info.plist`, and the system hands a link to the application by its
  `open-url` event, running or not; a link in a start's arguments is refused there.
- **Linux.** The deb's menu entry declares `MimeType=x-scheme-handler/jason-desktop;` and starts the program with `--`
  before the address (`Exec=/opt/Jason/jason-desktop-app -- %U`). The AppImage declares no scheme and takes no link,
  and neither does a development build.

A link that comes before the page is loaded waits for it, the newest replacing an older one; the window comes forward,
also when the link is refused.

## Processes

Every process is started with a file and arguments, never through a shell, and only by the named modules:
`src/cli.ts` (the bundled `jason`), `src/programs.ts` (the agent hosts, `reply`, `node` and `npm`, and a set-up chat's
naming run, whose command line `src/chat-naming.ts` gives), `src/chat-process.ts` (a chat turn's host, and Codex's
model listing, `codex app-server`, which only `src/model-lists.ts` starts), `src/shell-path.ts` (on macOS and Linux
only, the login shell's PATH, read before anything is started) and `scripts/prepare.mjs` (`dotnet publish`).
On Windows, a program npm installed is a batch file; the desktop reads which script it hands to Node and starts that
with Node. `test/process-guard.test.ts` holds this, and also refuses a `require` of `child_process`, an IPC handler
registered anywhere but `src/ipc.ts` (on `ipcMain` or on a `webContents.ipc`), `shell.openPath`, and `shell.openExternal`
outside the navigation lock in `src/main.ts`.

What the desktop tells Reply about itself is one label in the User-Agent of reply-cli's requests and nothing else:
`REPLY_CLIENT=jason-harness/<the bundle's version, less any build metadata after a plus sign>`, set in the environment of
the `reply` the desktop starts and of every agent host's turn in place of any `REPLY_CLIENT` the desktop inherited, and
neither set nor passed on there by a build that carries no bundle version (`src/reply-client.ts`).

## Updating and taking it off

Before an update installs, the desktop stops the runtime its own `jason` runs as, with `jason runtime stop`, and goes no
further when that runtime does not stop, or when the run file names a runtime that did not answer while the process it
names still runs, or when the run file is there and cannot be read (`src/own-runtime.ts`); it asks only the data
directory it was started with.

On Windows, where PowerShell is available, the installer and the uninstaller also look for any process running from the
carried `jason.exe`, whichever data directory started it, and never close one (`installer/uninstall.nsh`): they stop it
through the application's own `--stop-runtime` start, which runs `jason runtime stop` for the data directory their
environment names, or the default one, and exits, and look again. A person running them is asked first; an update and a
silent run are not. A process still running, a runtime that did not stop or one started with another data directory say,
ends the install or the uninstall before a file is replaced, and a person is told to stop it with
`resources\bundle\jason.exe runtime stop` in the application's folder, with `JASON_DATA_DIR` set when it was started
with another data directory. Where PowerShell is not available nothing is looked for: a person is asked to stop the
runtime, and an update goes on without a look. A person's uninstall starts the application with `--uninstall`, which
stops the runtime it carries, takes off the PATH command's files that are its own, Open Jason at Login, the runtime's
start at logon when it starts this application's `jason`, and the `jason-desktop` link registration when it names this
application, and names what it leaves: the data directory, the harness root
and the updater's cache.

On macOS there is no installer, so nothing looks when Jason.app is moved to the Trash, and the desktop's own look before
an update is the whole guard. Before removing Jason.app, choose Remove What Jason Placed… in the Jason menu (or the
tray's), which, once confirmed, takes the Windows uninstall's own steps (`src/uninstall.ts`; its dialogs are
`src/remove-placed.ts`), stopping the runtime first and going no further when it does not stop, says what came of each
and what to do by hand for one that failed, and offers to quit Jason, after which Jason.app goes to the Trash.

When Jason will not start, take off by hand what it placed. Its `jason` is `Contents/Resources/bundle/jason` inside
Jason.app (`/Applications/Jason.app/Contents/Resources/bundle/jason` where it was put in Applications):

1. Read `installed_image` in the run file, `run/runtime.json` in the data directory. Only when it names that `jason` is
   the runtime this application's: stop it with that `jason`, `<that jason> runtime stop`. A runtime of any other
   `jason` is another installation's, and stays.
2. Run `<that jason> runtime autostart status`. When the `executable` it names is that `jason`, run
   `<that jason> runtime autostart disable`.
3. Delete `~/Library/LaunchAgents/ai.jasonharness.desktop.plist`, which is Open Jason at Login.
4. Delete the `jason` (or `jason-desktop`) wrapper in `~/.local/bin` whose `exec` line runs that `jason`.

What stays: the data directory (`~/.jason`, or the one `JASON_DATA_DIR` names), the harness root (`~/Jason`, or the one
`Harness:Root` names) and the updater's cache, `~/Library/Caches/jason-desktop-updater`.

## On Linux

- The window keeps its menu bar, which carries the tray's items, since a session may show no tray icon. Closing the
  window quits the application; the runtime keeps running. A start at sign-in opens the window minimized.
- Open Jason at Login writes `~/.config/autostart/ai.jasonharness.desktop.desktop` (under `$XDG_CONFIG_HOME` when that
  is set), whose `Exec` line starts the AppImage file or the installed executable with `--opened-at-login`; it reads as
  on only while that line is there and the entry is neither hidden nor marked `X-GNOME-Autostart-enabled=false`. A path
  the entry cannot spell, one with a `%` or a control character, leaves the item off. An update that installs the
  AppImage under a new file name moves an entry that is on to it.
- The Command Line submenu places a `jason` wrapper in `~/.local/bin`, as on macOS.
- An AppImage runs from a mount that lasts only while it runs, so each start copies its `jason` to
  `<data>/desktop/bundle/<version>/`, and that copy is the `jason` it runs, starts the runtime from, puts on the PATH and
  gives the chat. A copy already there is used only when every file matches the package's; one that differs, or a link
  at the bundles folder or the version's folder, stops the start and is named, to be removed by hand. Without a copy,
  Start the Runtime at Logon and the Command Line's acts are not offered. A start at logon or a PATH command the person
  set up from another version's copy is set up again from this one at each start, the same act as turning it on; one
  never set up, or one that names a `jason` elsewhere, is left alone. The copies of other versions go after that, but
  the one an update kept to go back to, one something set up still names, and one the running runtime was started
  from. No folder of the mount reaches what the desktop starts: the PATH and the libraries it hands on are the
  session's.
- Jason never runs without Chromium's sandbox. An AppImage's launcher, which electron-builder writes, adds
  `--no-sandbox` wherever the system refuses unprivileged user namespaces; a start that carries it, on any platform,
  is refused before anything else is decided: a dialog says so, one log line says it was refused, and the application
  quits (`src/sandbox.ts`). So the AppImage needs a system that lets it run its sandbox. It is built so that on Ubuntu
  24.04 and later, as shipped, it says so and names the deb, whose install brings an AppArmor profile that lets the
  program make the user namespaces its sandbox uses; neither package has yet been seen on such a machine.
- Updates come to the AppImage and to the deb. An AppImage update keeps the AppImage file itself under
  `<data>/desktop/rollback/<version>/`; a deb update keeps no copy, and if the new version does not start, the page names
  the previous release's deb and `sudo apt install ./jason-desktop-linux-x64.deb`. Anywhere else the update item says
  updates come through the package. When the installer stops before it finishes (a refused password, say), Jason stays
  open and says so; that, the password dialog and an AppImage replacing itself are built so and not yet seen on a real
  machine. The updater's cache is `~/.cache/jason-desktop-updater`, or under `$XDG_CACHE_HOME` when that is set.
- Before removing Jason, choose Remove What Jason Placed… in the Jason menu (or the tray's), which takes the same steps
  as on macOS. On an AppImage it also stops a runtime started from any copy of its `jason`, and last removes the copies:
  the real folders named as versions under `<data>/desktop/bundle/`, when nothing runs from them, and the folder itself
  once that leaves it empty; a link or anything else there stays, and is named. It then says what removes the
  application itself: `sudo apt remove jason-desktop` for the deb, or deleting the AppImage file. What stays after
  that is the data directory (`~/.jason`, or the one `JASON_DATA_DIR` names), the harness root (`~/Jason`, or the one
  `Harness:Root` names) and the updater's cache, `~/.cache/jason-desktop-updater`. When Jason will not start, take
  off by hand what it placed. Its `jason` is `/opt/Jason/resources/bundle/jason` for the deb, and each
  `<data>/desktop/bundle/<version>/jason` for an AppImage:

  1. Read `installed_image` in `<data>/run/runtime.json`. Only when it names that `jason` is the runtime this
     application's: stop it with `<that jason> runtime stop`. A runtime of any other `jason` stays.
  2. Run `<that jason> runtime autostart status`. When the `executable` it names is that `jason`, run
     `<that jason> runtime autostart disable`, which takes off the runtime's systemd user unit.
  3. Delete `~/.config/autostart/ai.jasonharness.desktop.desktop` (under `$XDG_CONFIG_HOME` when that is set), which is
     Open Jason at Login, when its `Exec` line starts the AppImage file or the deb's executable.
  4. Delete the `jason` (or `jason-desktop`) wrapper in `~/.local/bin` whose `exec` line runs that `jason`.
  5. For an AppImage, once nothing runs from them, delete the folders named as versions under `<data>/desktop/bundle/`.

## Building and testing

From `desktop/`:

```sh
npm ci
npm run check         # the type-check of the sources and the tests
npm test              # the Node tests
npm run prepare:bundle -- --pre rc.1   # publishes this checkout's jason into bundle/ as <VersionPrefix>-rc.1
npm run dev           # builds and starts the window
```

A desktop build always carries a candidate's version string (`0.34.0-rc.1`), never `dev`, so `jason ui install` and
`jason plugin install` resolve that candidate's own release directory. `test/candidate-version.test.ts` proves it against
a local feed when `JASON_UI_ZIP` names a UI build packaged with `JASON_VERSION` set to the candidate.

A package is built from `electron-builder.config.mjs` at the version `JASON_VERSION` names, a release's or a candidate's,
around a bundle prepared for each architecture at that same version. On Windows, unsigned:

```sh
node scripts/prepare.mjs --rid win-x64 --output bundles/x64 --version 0.34.0-rc.1
JASON_VERSION=0.34.0-rc.1 npm run package -- --win --x64   # dist/: the nsis installer, latest.yml, win-unpacked/
```

On Linux, where nothing is signed, the same two lines make the deb and the AppImage, and the feed that names both:

```sh
node scripts/prepare.mjs --rid linux-x64 --output bundles/x64 --version 0.35.0-rc.1
JASON_VERSION=0.35.0-rc.1 npm run package -- --linux --x64   # dist/: the deb, the AppImage, latest-linux.yml
```

The program is `jason-desktop-app`, so the deb's `/usr/bin` link never takes `jason-desktop`, the name the PATH command
falls back to. The AppImage is packed on electron-builder's static AppImage runtime, which needs no libfuse2. The two
forms are packed at once from one folder, and the deb writes `resources/package-type` into it, which makes
electron-updater update a package as a deb, so the configuration starts the deb only once the AppImage is built.

The release passes `--executable` with the `jason` it has already built and signed, so the package carries that very
file, and signs the package itself: a Mac package is never built unsigned. Inside `app.asar` are `lib/`, `shell/`,
`resources/` (the application icon as a PNG, and the tray's images; the Mac's icon set is read at build time from `installer/icon.icns`, and the Linux packages install the PNGs of `installer/linux-icons/`, the same icon set's 32, 64, 128, 256, 512 and 1024 px images, under hicolor), the chat's two lists and its instruction text; beside it, in the package's `resources/`, are `bundle/`
(the executable with `LICENSE` and `THIRD-PARTY-NOTICES`, the skills and `version.json`) and `notices/` (the
repository's `LICENSE`, this folder's `THIRD-PARTY-NOTICES` and Chromium's `LICENSES.chromium.html`). The package
carries no UI: `jason ui install` places the UI of the runtime's own release.

electron-builder fetches Electron's distribution for each platform and architecture it packs. The configuration hands it
the sha256 of each file from the `checksums.json` the `electron` package carries, which `package-lock.json` pins, so a zip
that does not match, downloaded or cached, stops the pack; its own tools (nsis, the resource and icon tools, 7-Zip,
dmgbuild) it checks against the sha256 its own source names.

`scripts/check-package.mjs` reads a built package back against this configuration: the feed and, on Windows, the
publisher its `app-update.yml` names, the fuses its binary carries, and on Linux what each form is: a deb must carry
`resources/package-type` saying `deb` and an AppArmor profile that lets `/opt/Jason/jason-desktop-app` make user
namespaces; an AppImage must carry no `package-type`, hold `AppRun`, and have a menu entry at its root whose `Exec`
line is there and carries no `--no-sandbox`. The release runs it on every package it builds:

```sh
node scripts/check-package.mjs --app dist/win-unpacked --platform win32
node scripts/check-package.mjs --app <unpacked deb>/opt/Jason --platform linux --form deb
node scripts/check-package.mjs --app squashfs-root --platform linux --form appimage   # after the AppImage's --appimage-extract
```

To run it against an installation other than your own, set `JASON_DATA_DIR` and `JASON_Harness__Root` in the environment
it is started from.
