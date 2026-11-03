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


# Jason 0.26.0

## What is new since 0.25.0

A launched role no longer has to run on Claude Code. Each host below is a program you install yourself and sign in to as
yourself, under your own plan: Jason holds no key for any of them. `jason skills install --host <host>` deploys the
skills into that host's folder, and Codex CLI, GitHub Copilot CLI, Cursor CLI and Gemini CLI all read `~/.agents/skills`, so
they share one deployment; Antigravity CLI keeps its own folder, `~/.gemini/antigravity-cli/skills`. `jason status` says which hosts it found on the machine and whether every enabled profile names
one. Every host added in this release is built from the host's own documentation, and from its source or installed files
where there are any, for the version [the host matrix](https://github.com/reply-team/jason-ai/blob/v0.26.0/docs/execution-profiles.md#12-the-hosts)
names; none has yet been run on a signed-in account. The tests prove the command Jason composes and the launch around it
against a stand-in program.

- **Codex CLI.** Install it from [its own page](https://github.com/openai/codex), sign in with `codex login`, then
  `jason profile create local-codex --host codex --program codex`. A role is confined by a Codex sandbox rather than by a
  list of commands: the runtime names a permissions profile of its own on the command line, and every command the role
  runs is held by it. On Windows an administrator sets up Codex's elevated sandbox once first. [The Codex
  section](https://github.com/reply-team/jason-ai/blob/v0.26.0/docs/execution-profiles.md#codex-cli) says what it reads
  and what it cannot hold.
- **Cursor CLI.** Install it from [Cursor's installation page](https://cursor.com/docs/cli/installation.md), sign in with
  `agent login`, then `jason profile create local-cursor --host cursor_cli --program agent`. A role is confined by
  permission rules Jason writes to `.cursor/cli.json` in the attempt's work directory: the runtime's own command is the
  one shell command allowed, the memory files are the reads, and every write is denied. On Windows, Jason starts the
  host's `agent.cmd` launcher itself, and a profile names it by its full path where the runtime's search path does not
  find `agent`. [The Cursor section](https://github.com/reply-team/jason-ai/blob/v0.26.0/docs/execution-profiles.md#cursor-cli)
  says what it reads and what was not measured.
- **GitHub Copilot CLI.** Install it from [GitHub's installation page](https://docs.github.com/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli), sign in with
  `copilot login`, then `jason profile create copilot --host copilot_cli --program copilot`. A role is confined by the
  tool set and the allow and deny rules Jason composes on the host's command line, with nothing written into the work
  directory, and a profile's own arguments are limited to the few documented options a person owns, such as `--model=`.
  [The Copilot section](https://github.com/reply-team/jason-ai/blob/v0.26.0/docs/execution-profiles.md#github-copilot-cli)
  lists them.
- **Gemini CLI.** Install it from [its own page](https://github.com/google-gemini/gemini-cli), sign in as yourself, then
  `jason profile create <name> --host gemini_cli --program gemini`. It runs on a Code Assist Standard or Enterprise
  sign-in; individual Google plans run Antigravity CLI. A role is confined by a policy file,
  `.jason-policy/jason.toml` in the work directory, which the host reads above the person's own rules: everything is
  denied first, then the reads, the runtime's own command and the tools the profile grants are allowed, and writing is
  denied above every allow. The host reads a memory file through a `GEMINI.md` that imports `AGENTS.md`. A role's run sends no telemetry, whatever your own setting says. [The Gemini
  section](https://github.com/reply-team/jason-ai/blob/v0.26.0/docs/execution-profiles.md#gemini-cli) says what was not
  measured.
- **Antigravity CLI.** Install it from [Google's quickstart](https://antigravity.google/docs/getting-started?tab=cli), run
  `agy` once and sign in as yourself, then `jason profile create antigravity --host antigravity_cli --program agy`. It
  is the host individual Google plans run. Jason cannot give a launched session its permissions run by run on this host,
  so for a role to work you add two rules to your own `~/.gemini/antigravity-cli/settings.json`, in `permissions.allow`:
  `command(jason)` so the role can call the runtime back (on Windows shells `command(regex:jason .*)`) and
  `read_file(<your harness folder>)` so it can read the memory files. [The Antigravity
  section](https://github.com/reply-team/jason-ai/blob/v0.26.0/docs/execution-profiles.md#antigravity-cli) shows both and says what was not measured.
- **Claude Code refuses more arguments in a profile.** A host's parser reads a short flag with more letters, or with a
  value after it, as several flags or as a flag and its value, so a reserved flag could stand inside one: `-pf` is `-p`
  and `-f`. A short flag with more letters or a value after it, a lone dash and a double dash are now refused when a
  profile's `args` are written, naming the host. A flag and its value as two words are still taken. A profile stored
  before this release is not read again.
- **`cli_command` is a bare word of letters, digits, dots, dashes and underscores,** starting with a letter or a digit.
  That is the word a role calls the runtime back with, and it is refused when it holds anything else.
- **A profile moved to another host must name a program that host can run.** `jason profile update` with a new `--host` and
  no `--program` keeps the stored program only if it is that host's own command word, or its launcher where the host is
  installed as one; otherwise it is refused, and the arguments the profile carries are held to the new host's rules too.
- **A host that is not signed in ends the attempt as `host_not_available`,** with one sentence of Jason's own and none of
  the host's words, on every host whose row names the signal Jason reads. Only you can sign in to it, as yourself.
- **A harness folder holds one import file for every host that reads a file of another name:** `CLAUDE.md` for Claude Code
  and `GEMINI.md` for Gemini CLI, each a single line importing `AGENTS.md`, and neither written over where it is there.

## Upgrading from 0.25.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update`, `jason ui install`
  and `jason plugin install reply --replace` follow.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. Three skills changed in their wording. The manager, the campaign-work skill and the troubleshooting skill say that
  `host_not_available` can also come after a host started and said it is not signed in, which only you can repair by
  signing in; the troubleshooting skill also says that on Antigravity CLI an `executor_exited` after the host exited 0
  usually means your `command` rule is missing from its settings. The skills' READMEs point to the host matrix for the
  other hosts' skill folders.
- **The data directory:** this release adds no database migration: your projects, campaigns, work items and chronicle stay
  as they were.
- **Harness folders:** the root gains a missing import file, `GEMINI.md`, at the next project or campaign create, over no file
  that is there. A project's or a campaign's folder made before 0.26.0 keeps its `CLAUDE.md` alone unless you add a
  `GEMINI.md` holding `@AGENTS.md`. Jason writes no word of a memory file.
- **The Reply plugin** is unchanged.
- **From 0.24.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the first known issue below says.

## Known issues

Codex CLI on Windows needs its elevated sandbox set up once by an administrator before a role can run a command; until
then every command a role runs is refused and the attempt fails by the role's report, and Jason does not check for the
setup when the attempt starts.

Whether `jason` reaches the runtime from inside Codex's sandbox has not been checked on Linux, macOS or Windows, and on
Windows it is not yet checked whether the elevated sandbox reads only the list Jason names. Codex also reads its own global
`AGENTS.md` and offers every role the skills in `~/.agents/skills`, whatever the profile says, so those two reach every
Codex role.

Whether Cursor CLI runs the allow-listed `jason` command without asking, in print mode, has not been seen on a signed-in
run. If it does not, a role cannot call the runtime back and ends by its own report. Cursor CLI also reads more than the
memory files Jason names, since it takes the union of every configuration layer it finds, yours included, and each attempt
leaves one trust marker under `~/.cursor` that Jason does not remove.

Gemini CLI reads the whole folder of each level a role's memory files are in, and the client level's folder is the harness
root, so a role reads every memory file under it. A role on Gemini CLI is also refused when the host's system policy folder
holds a `.toml` file, since the host would then ignore the policy Jason gives it.

GitHub Copilot CLI's reads outside the work directory, which a role needs for its memory files, are documented but have not
been seen to reach there.

A role on Antigravity CLI works only once you have added the two rules to your own settings, `command(jason)` and
`read_file(<your harness folder>)`; per-run permissions for that host are not built in this release. Without the
`command` rule a role is denied the call that reports, ends with the host's exit 0 and without a report, and the
attempt is retried against the same denial. Whether the host runs an allowed command unattended, and whether the plain
`command(jason)` covers the report on Windows, has not been seen on a signed-in run. The host also reads your own global
`AGENTS.md` and `GEMINI.md` under `~/.gemini` for every role.

Flags and variables that move a host's own traffic, a proxy among them, are yours to set: Jason sets none of them and
reserves none of them.

A profile is held to its host's rules when it is written, not when a run starts, so a profile stored under one release is
not read again by a later release whose rules for its host are stricter.

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
