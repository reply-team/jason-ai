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

# Jason 0.32.0

## What is new since 0.31.0

Jason's notifications reach Slack and Telegram, every notification package is published with the release and placed by
one command, and the journal forwarder can write its lines to a file in a directory you grant.

- **Slack delivers Jason's notifications.** The `slack` plugin, 1.0.0, posts the pointer the runtime composes as plain
  text to one Slack channel with `chat.postMessage`, and nothing else about the work. The binding names the channel, by
  its id (`C0123ABCDEF`), which survives a rename, or by its name; the grant names `slack.com`; the bot token is read
  from `Plugins:Config:slack:token` and, where that says nothing, from a granted `SLACK_BOT_TOKEN`. A delivery Slack
  answered with `ok: false` carries Slack's own error word in the `notification_sent` line's `detail`. Its
  [README](https://github.com/reply-team/jason-ai/blob/v0.32.0/plugins/slack/README.md) says how to make the app and
  every outcome it reports.
- **Telegram delivers Jason's notifications.** The `telegram` plugin, 1.0.0, sends the pointer as plain text to one chat
  with the Bot API's `sendMessage`, by a bot you make. The binding's `chat` is the chat's id, a number such as
  `-1001234567890`, or the public `@name` of a channel or a supergroup; the grant names `api.telegram.org`; the token is
  read from `Plugins:Config:telegram:token` and otherwise from a granted `TELEGRAM_BOT_TOKEN`, and the setting wins when
  both are there. The Bot API takes the token in the path of every request, so the plugin host masks it wherever it
  would write it: the line it writes about the call, a log line and a failure show `[redacted]` in its place. A failure
  carries Telegram's own description, cut at 200 characters. Its
  [README](https://github.com/reply-team/jason-ai/blob/v0.32.0/plugins/telegram/README.md) says how to find a chat's id.
- **The three notification packages are release assets.** `jason-plugin-ntfy.zip`, `jason-plugin-slack.zip` and
  `jason-plugin-telegram.zip` are published beside the Reply plugin's and the journal forwarder's, each the files of its
  directory at the tag, listed in `checksums.txt` and in the manifest with its content digest.
  `jason plugin install <id>` places each in `~/.jason/plugins/<id>` once it matches both, and `jason status` and
  `jason update` read them as they read the other two: `jason update apply` replaces a copy installed as an earlier
  version and places none you did not install. `jason status` names the packages that are not installed in one sentence.
  See
  [the notifications page](https://github.com/reply-team/jason-ai/blob/v0.32.0/docs/notifications.md#8-ntfy-slack-telegram-and-other-plugins).
- **ntfy reaches a server of your own by a grant, with no edit.** ntfy 1.0.3's manifest says
  `capabilities.http.any_granted_host: true`, so a server of your own is granted by name under
  `Plugins:Grants:ntfy:Http` and named in the binding's `server`, and the package stays the one the release published,
  which `jason update` moves on. A grant of `*` means `ntfy.sh` alone. A server that did not answer is named by its
  address in the line's `detail`. See
  [its README](https://github.com/reply-team/jason-ai/blob/v0.32.0/plugins/ntfy/README.md#your-own-server).
- **The journal forwarder writes to a file.** The forwarder, 1.2.0, adds each line to a file in a directory you grant,
  beside its POST or instead of it: the directory goes under `Plugins:Grants:journal-forwarder:Files` and the file under
  the key `file` of `Plugins:Config:journal-forwarder`, or in a granted `JOURNAL_FORWARDER_FILE` where that says
  nothing. What it adds is the POST's body, one JSON object per line. With both, the file goes first; a disk that
  refuses the line still leaves the POST, and the handler then ends with the disk's failure, so the line is offered
  again. The file is only ever added to, and it holds what the journal says, names and reasons included, so it is a
  place you trust with that. Its
  [README](https://github.com/reply-team/jason-ai/blob/v0.32.0/plugins/journal-forwarder/README.md#a-file-instead-of-a-receiver-or-beside-one)
  shows both entries.
- **`jason plugin list` says where a plugin may write.** A plugin whose manifest asks to write a file shows the
  directories it was granted, and its `--human` line counts them as `files <n>`. A `jason doctor` bundle carries no
  grant, this one included.
- **The Reply plugin's page says what is detected of a contact's later replies.** Reply 0.14.6 changes its README alone:
  what Reply detects of later replies by email, and which of it was observed on an account, as the known issue below
  says.
- **`operating-the-installation` says where a plugin may write, and is `draft` again.** It says that the directories
  a plugin may append lines to are `Plugins:Grants:<id>:Files`, each an absolute path, read at a start and at each
  `jason plugin reload`, and what is refused as `grant_path_refused`, which rejects the whole reload: the home directory
  or a folder holding it, the top of a drive, a share or the file system, the data directory or the harness root, a
  folder inside or holding either of those two, a relative path, `~` and `*`. It also says that `jason update check`
  names no package installed as a version before its package's list of published digests begins, which for the Reply
  package is 0.3.2 and for every other is 1.0.0. No agent host has read that text, so its status went back from
  `verified` to `draft`. The skills marked `draft` are `operating-the-installation`, `researcher-in-session`,
  `roles/responder`, `troubleshooting-jason` and the nine skills of the business pack.
- **For a plugin author: a file to append to.** `host.appendLine(path, text)` adds one line to a file directly in a
  directory a person granted, and is the only way a plugin writes a file. A manifest asks with
  `capabilities.files: { append: true }` and names no place; any kind of plugin may ask, and `manifest_version` stays
  `1`. The line is written as UTF-8 with a line feed added and is on the disk before the call returns; the file is made
  when it is not there and is never truncated, renamed or deleted. A file name starts with a letter or a digit and goes
  on in letters, digits, `.`, `-` and `_`, at most 100 of them, with no dot last, and `CON`, `NUL`, `PRN`, `AUX`, `COM1`
  to `COM9` and `LPT1` to `LPT9` are refused on every platform. The refusals end the invocation, `permanent`:
  `capability_not_granted` with no directory granted, `file_path_not_allowed` for a path outside the grant or a name the
  host refuses, `file_line_multiline`, `file_line_too_large` past `Plugins:Files:LineBytes` (1 MiB, the line feed
  counted) and `file_limit` past `Plugins:Files:MaxCalls` (64 lines per invocation). Only `file_write_failed`,
  `transient`, is yours to catch, and a line that passed the rules counts as a call that may have acted, so a timeout
  after one is `ambiguous`. A directory is an absolute path, resolved at the load; the home directory, the top of a
  drive, a folder holding the home directory, the data directory or the harness root, a folder inside either or one
  holding either, and a path that leads to one of them through a link are refused as `grant_path_refused`, a package
  problem that rejects the whole reload. Nothing in a line is masked, and neither the host's records nor a failure name
  the directory. See
  [`host.appendLine`](https://github.com/reply-team/jason-ai/blob/v0.32.0/docs/plugins.md#hostappendline) and
  [the `files` grant](https://github.com/reply-team/jason-ai/blob/v0.32.0/docs/plugins.md#the-files-grant-where-a-plugin-may-write).
- **For a plugin author: what a failure's message carries.** A notification plugin's failure message is now recorded on
  the `notification_sent` line, where people and subscribers read it, so the delivery contract asks that it be a
  diagnostic, never the notification's title or message, nor a credential. The plugin host masks the value of every
  variable the plugin was granted and of its configuration, a value under 8 characters excepted, in a failure's message
  and details before the outcome leaves it, so the runtime records only the masked text. A test now holds this for a
  secret that rides a request's path, as a bot token does: the host's own line about the call, a log line, the failure's
  message and its details all show `[redacted]`, whether the secret came from the configuration or from a granted
  variable. A successful result is still not masked. See
  [`notification.send`](https://github.com/reply-team/jason-ai/blob/v0.32.0/docs/contracts/notifications/notification.send.md).

## Fixed since 0.31.0

- **A failed delivery says why.** A notification plugin that answered with a failure was recorded with its code and
  class alone; the failure's message is now the `notification_sent` line's `detail`, cut to at most 500 characters,
  after the plugin host has masked it as the plugin-author note says. A cut that would fall inside a character written
  as two UTF-16 units steps back one, so the kept text is whole.
- **The Reply plugin's page no longer says that answering keeps a conversation visible.** That holds for a direct email;
  on a sequence thread the known issue below says what is detected.

## Upgrading from 0.31.0

Nothing stops: no setting is renamed, and no start refuses a setting 0.31.0 took.

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update` and `jason ui install`
  follow, and `jason plugin install <id> --replace` for each package you installed and did not edit.
- **The packages:** reply 0.14.6, journal-forwarder 1.2.0, ntfy 1.0.3, slack 1.0.0 and telegram 1.0.0.
  `jason update apply` replaces a copy installed as an earlier version, byte for byte as published, and leaves one you
  edited alone. Slack and Telegram are new: `jason plugin install slack` or `jason plugin install telegram` places one,
  and nothing places them for you. A release is thirteen files.
- **ntfy:** an edited copy of an earlier ntfy reads as behind 1.0.3, and `jason status` advises `--replace`. First
  **grant the host by name** under `Plugins:Grants:ntfy:Http`, since a grant of `*` means `ntfy.sh` alone; a copy
  already granted by name needs nothing here. Keep the binding's `server` as it is. Then
  `jason plugin install ntfy --replace` drops the edit and asks the runtime to reload, which reads the grant you wrote
  and loads the package at once, so no delivery fails in between and no separate `jason plugin reload` is needed; with
  no runtime running, the package loads at the next start. `any_granted_host` needs Jason 0.31.0 or newer.
- **The journal forwarder** is 1.2.0 and asks for more than 1.1.0 did: its manifest asks for `capabilities.files` and
  names no place, so it writes no file until you grant a directory. A runtime older than 0.32.0 rejects the whole reload
  over that word, as every package problem rejects it, so an older runtime needs the forwarder its own release
  published. Its failure code `forwarder_url_missing` is now `forwarder_receiver_missing`, said when neither a URL nor a
  file is named: a filter of your own on `subscription_failed` lines that names the old code needs the new one.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. One skill changed its status line, `operating-the-installation`, from
  `verified` to `draft`.
- **The data directory:** this release adds no database migration: your projects, campaigns, work items and chronicle
  stay as they were.
- **From 0.30.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the known issue about that update below says.

## Known issues

On Windows, a campaign's move to another project can be refused for the instant the background import-file sweep writes
inside that folder, soon after a start; ask again and it succeeds.

`reply skills install --project` does not see an old `reply-adapter@reply-skills` plugin installed at user scope on
Claude Code, so that plugin keeps loading beside the project copy of the skills.

`reply skills install --dry-run` and `reply skills update --dry-run` describe what they would do in the past tense, as
if it had been done; nothing is changed.

The click on a notification and its Copy prompt button are Windows only. Where another program already holds `jason:` links,
Jason leaves that registration alone and the toast shows without a click or a button.

On Windows, `jason session open` starts the console host, which hands the window to Windows Terminal where that is
your default terminal; where your default terminal is the console host itself, you get a plain console window; neither
is yet measured on a machine.

The macOS form of `jason session open`, a script opened in Terminal, has not been watched on a Mac.

Antigravity CLI's hook route has not been run on an account. The host also reads your own global `AGENTS.md` and
`GEMINI.md` under `~/.gemini` for every role.

Two installations on one account, a build of a checkout and the installed one, take the `jason:` link from each other at
each toast, since the registration rewrites a key that is Jason's and names another executable. Uninstall leaves a key
that names another installation alone, so nothing is lost.

On Linux, kitty, foot, gnome-terminal, xfce4-terminal, mate-terminal and wezterm are started in the form each takes;
any other terminal program is given the host after `-e` as separate arguments.

Under Windows PowerShell 5.1 and cmd.exe the inner quotes of `--value '{"host":"codex"}'` are lost, so give the value in a file:
`jason preference set agent.interactive_host --value-file host.json --actor human:<your-name>`.

If Claude Code's npm postinstall never replaced its placeholder executable, `jason session open` reports that it started the host
and the window closes at once.

Codex CLI on Windows needs its elevated sandbox set up once by an administrator before a role can run a command; until
then every command a role runs is refused, so the role cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal; Jason does not check for the
setup when the attempt starts.

Whether `jason` reaches the runtime from inside Codex's sandbox has not been checked on Linux, macOS or Windows, and on
Windows it is not yet checked whether the elevated sandbox reads only the list Jason names. Codex also reads its own global
`AGENTS.md` and offers every role the skills in `~/.agents/skills`, whatever the profile says, so those two reach every
Codex role. Holding Codex's network to loopback rests on its network proxy, an experimental feature in 0.166.0.

Whether Cursor CLI runs the allow-listed `jason` command without asking, in print mode, has not been seen on a signed-in
run. If it does not, a role cannot call the runtime back, so it cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal. Cursor CLI also reads more than the
memory files Jason names, since it takes the union of every configuration layer it finds, yours included: your own allow
rules of any kind, a shell command or a write among them, stand beside Jason's. Each attempt leaves one trust marker under
`~/.cursor` that Jason does not remove.

Gemini CLI reads the whole folder of each level a role's memory files are in, and the client level's folder is the harness
root, so a role reads every memory file under it. A role on Gemini CLI is also refused when the host's system policy folder
holds a `.toml` file, since the host would then ignore the policy Jason gives it.
Gemini CLI's `--session-id` and `--admin-policy`, which Jason passes, are in the 0.66.0 source and not in its options table.
Gemini CLI also reads your own `~/.gemini/GEMINI.md`, `includeDirectories` and skills for every role.

GitHub Copilot CLI's reads outside the work directory, which a role needs for its memory files, are documented but have not
been seen to reach there.
Whether Copilot CLI 1.0.105 accepts every flag Jason composes is not said by its changelog, so its launch is composed from the
reference and not from a run.
A role on Copilot CLI runs under your own user-level hooks, and a `COPILOT_ALLOW_ALL` in your environment reaches it as the
host's own allow-all, minus what the deny list names.

A proxy flag or a proxy variable is yours to set: Jason sets none of them and reserves none of them. A flag that names a
host's own server, its credentials or its certificate check is reserved where the host has one, as the Cursor CLI section
says, and a host whose arguments are an allowlist takes no flag outside it.

On a build older than 0.18.0, the update through 0.19.0 can stop at `started`, saying that a library failed to load.
That update is still performed by the older build, and it happens when the two builds carry different .NET patches.
Type `jason update apply` again: it runs 0.19.0, which finishes the update, and one more `jason update apply` then
takes the newest release. From 0.18.x this does not happen: 0.19.0 is built on the same .NET patch as 0.18.0.

Through Reply, a contact's later replies by email can be lost: a lost reply reaches none of `conversation.list`,
`conversation.get` and the change feed, and Jason's inbound sync says nothing of it. A direct email's replies are all
detected, any number, a reply after an answer sent from Reply's inbox included. A second reply to a sequence email is
not detected: Reply takes the first reply to each email it sent. After an answer sent from Reply's inbox on a sequence
thread, a reply is detected only when its own headers reference that answer, and a reply to the original sequence email
is dropped. No auto-reply or out-of-office is stored on a direct thread; a reply whose `In-Reply-To` and `References`
are both empty is dropped; and a reply to a send older than 40 days is not matched unless a newer send to that address
exists. A LinkedIn thread keeps later messages, so they are read. That a direct email's replies are all detected was
observed on an account; the rest, the LinkedIn statement among them, is read from how Reply is built and has not been
observed.

A send or an invitation through Reply carries no sending time in its own result: `sent_at` is null, because Reply's
answer to a LinkedIn send gives a local time marked as UTC and its answer to an email gives none. `message.status`
reports when a send went, and a message's own moment is read from its thread, where it is true UTC.

The approval preview shows a send's `send_after` and not the provider object beside the arguments, so a send that also
names a sending schedule goes at the schedule's first opening at or after that moment, which may be later than the
moment the approver read.

A template held for a later moment, or queued by Reply behind the account's other sends, is filled in when it goes: a
name Reply cannot fill then fails the send after a person approved it, which `message.status` reads as `failed` with
Reply's reason.

Reply converts a LinkedIn text from HTML to text after filling it in, so markup in a LinkedIn message or InMail, a
final text or a template, does not reach the person as written; what Reply makes of a `<` that starts a tag there is
not known yet, and the Reply package does not refuse one on LinkedIn as it does on email.

Reply does not consult its blacklist when it takes a LinkedIn direct send, and does when it makes it: a send to a
person its blacklist holds is accepted, and fails when Reply makes it, which `message.status` reads as `failed` with
Reply's reason. Jason's own do-not-contact register is checked at the claim as before; record such a stop there with
`jason suppression add` as well, so the send is refused before it is made.

Through Reply, `schedule.list` does not say which campaign runs on which schedule: `campaign.get` names no schedule,
so which one a campaign sends on is read at Reply.

Through Reply, a reply on a thread, `conversation.reply`, is not held to the rule on a `<` that starts a tag yet, and
its text goes as written; whether Reply cuts a reply as it cuts a direct email has not been seen, so write an address
in a reply without its angle brackets.

The responder's skill ships marked `draft`: no agent host has read its text in the state it ships in. Its commands parse
against this build, as every skill's do, but how an agent follows its loop on a real reply is not yet seen.
