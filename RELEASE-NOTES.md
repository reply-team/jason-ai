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


# Jason 0.27.0

## What is new since 0.26.0

A notification now lands on what waits, and one click each takes you from it to a brief and to your own agent. The agent
proposes; approving, rejecting and answering stay yours.

- **A click on a Windows notification opens what waits.** With one approval or question waiting, a click on the banner
  or on its entry in the notification centre opens that item in your UI; with several, it opens the list. Jason registers
  a `jason:` link scheme under your account for this, and a link that is not one of its few forms is refused before
  anything is asked of the runtime. Only the item's opaque id travels, never anything of what it says. A toast for one
  item also has a **Copy prompt** button, which puts that item's brief on the clipboard. This is Windows only; on macOS
  and Linux a notification opens nothing of Jason's, and `jason ui open` reaches the same page. [The notifications
  page](https://github.com/reply-team/jason-ai/blob/v0.27.0/docs/notifications.md) says what a click opens on each platform.
- **Copy prompt.** `jason approval get <id> --prompt` and `jason decision get <id> --prompt` print, in place of the JSON
  answer, a short brief for an agent session on that one item: what waits, its campaign and the folder to work from, the
  `approvals-and-questions` skill and the line that installs it, what to read and bring back, and that the session
  decides nothing. The brief holds nothing of the item itself; the session reads it through the command line. It cannot be
  combined with `--human`. The UI has the same text behind a **Copy prompt** button on an open approval or question.
- **Open an AI session.** `jason session open --approval <id>` or `--question <id>` starts your own agent host in a new
  terminal window at the campaign's folder (the home folder where none is recorded), with that brief as its first
  message; `--dry-run` prints the command and starts nothing. The host is the one your named preference
  `agent.interactive_host` says, and Claude Code when it is not set. The preference is optional, and you set it as
  yourself, for example `jason preference set agent.interactive_host --value-file host.json --actor human:<your-name>`
  with a file `host.json` holding `{"host":"codex"}`; `jason preference unset agent.interactive_host --actor human:<your-name>` takes it back.
  [The preference's page](https://github.com/reply-team/jason-ai/blob/v0.27.0/docs/contracts/preferences/agent.interactive_host.md) lists the host words. The attention
  screen of the UI shows this command beside Copy prompt, and on Windows an **Open an AI session** link that runs it.
  [The UI page](https://github.com/reply-team/jason-ai/blob/v0.27.0/docs/ui.md#open-an-ai-session) says how each system opens the terminal.
- **Antigravity CLI roles no longer need your two settings rules.** Each attempt's work directory now carries a workspace
  hook, and the host asks it about the tools a role uses: the role may call `jason` back, read its memory files and write
  its `result.json`, may not edit any other file, and gets web fetch and search only where its profile grants them. The
  hook route is built from the host's own hooks page and has not yet been run on an account. The two rules of 0.26.0
  stay as the fallback, and [the Antigravity
  section](https://github.com/reply-team/jason-ai/blob/v0.27.0/docs/execution-profiles.md#antigravity-cli) says what is not measured.

## Fixed since 0.26.0

- **An agent host installed with npm is found on Windows.** Claude Code, Codex CLI, Gemini CLI and GitHub Copilot CLI,
  installed with `npm install -g`, were answered `host_not_available` by a role's launch and not found by
  `jason status`. Jason now reads npm's shim of a scoped package, and the shim that starts an executable the package ships,
  without starting either through a shell.
- **A profile stored under an older build is checked again when work launches.** A profile revision written before an update
  can hold a word this build's rules for its host refuse, such as a reserved argument. Work under it now ends before
  any child starts with the code `profile_revision_refused`, naming the field and never the value; `jason profile get`
  and `jason profile list` show it as `problems`, and `jason status` names the profile. `jason profile update` is the repair.
- **A runtime start writes the import files a recorded folder lacks.** The `GEMINI.md` that imports `AGENTS.md` is now
  written, at every start, into the harness root and every recorded project and campaign folder that lacks it, where 0.26.0
  wrote it only at a create. A file that is there is never opened.

## Upgrading from 0.26.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update`, `jason ui install`
  and `jason plugin install reply --replace` follow.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. The approvals, installation, campaign-work, troubleshooting, in-session
  planner and in-session researcher skills and the nine role skills changed in their wording.
- **The data directory:** this release adds no database migration: your projects, campaigns, work items and chronicle stay
  as they were.
- **The preference `agent.interactive_host`** is new and optional; without it `jason session open` starts Claude Code.
- **Antigravity CLI:** if you added the two rules to your own settings in 0.26.0, you may keep them; they remain the
  fallback.
- **The Reply plugin** is unchanged.
- **From 0.25.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the known issue about that update below says.

## Known issues

The click on a notification and its Copy prompt button are Windows only. Where another program already holds `jason:` links,
Jason leaves that registration alone and the toast shows without a click or a button.

On Windows, `jason session open` starts the console host, which hands the window to Windows Terminal where that is your
default terminal; where your default terminal is the console host itself, you get a plain console window.

The macOS form of `jason session open`, a script opened in Terminal, has not been watched on a Mac.

Antigravity CLI's hook route has not been run on an account.

Under Windows PowerShell 5.1 and cmd.exe the inner quotes of `--value '{"host":"codex"}'` are lost, which is why the
example above reads the value from a file.

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

Reply's routes behind 0.22.0's send features - a send's moment and its sending schedule, reading and cancelling a send
by its reference, and the inbox change feed - reach Reply's accounts on Reply's own schedule. Until an account's Reply
has a route, the Reply package answers `capability_not_offered` for that operation or argument, and the account's
replies are read thread by thread in place of the feed, as its README says; nothing in Jason needs updating when the
route arrives.

For a sequence email, Reply keeps at most one reply to each email it sent and drops a finished contact's later replies
on that thread before it stores them, so they reach none of `conversation.list`, `conversation.get` and the change
feed, and Jason's inbound sync says nothing of them. After this side has answered on the thread through
`conversation.reply`, the contact's next replies on it are stored and read as usual; a second reply that arrives
before any answer may never be seen. A LinkedIn thread keeps later messages. All of this is read from how Reply is built
and has not yet been observed on an account.

A send or an invitation through Reply carries no sending time in its own result: `sent_at` is null, because Reply's
answer to a LinkedIn send gives a local time marked as UTC and its answer to an email gives none. `message.status`
reports when a send went, and a message's own moment is read from its thread, where it is true UTC.

Through Reply, a sending schedule times a LinkedIn message only once the account's Reply times deferred LinkedIn sends
itself. Until then the Reply package answers a LinkedIn message that names one `capability_not_offered`, and nothing is
sent, whether the account's Reply has no direct sends yet or has them without a schedule on LinkedIn.

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
