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

# Jason 0.31.0

## What is new since 0.30.0

Any role can now be held to a daily limit of launches per campaign, and you are told when work waits on one; and a plugin is
configured in the settings, without editing its package.

- **A daily limit of launches for any role.** `Launches:MaxPerDay:<role>` in `settings.json` limits how many runs of a role
  one campaign starts in a UTC day, from 1 to 1000. It is the setting that held only the responder in 0.30.0, and the
  responder keeps its default of 25 (`Launches:MaxPerDay:responder`); a role the map does not name has no limit. The
  responder's limit can be changed and not removed. A value that is not a number, or is outside 1 to 1000, stops the
  start and names the setting by its path, and a role's name must be lowercase letters, digits and hyphens starting with
  a letter, at most 64 characters. A run that would pass its role's limit is held, not failed: the work item is `waiting` with no attempt until
  the next 00:00Z, its `last_error` is `launches_spent`, and its `workitem_waiting` line in the chronicle says `held_by`
  with the code, the limit and the role. There are two ways out for a held item: the next 00:00Z, which releases it, though a
  day already spent holds it again; and `jason workitem resume`, by a person, which lets that one item past the limit and
  counts its run. A higher limit reaches work not yet held, at the next scan. A held manager check-in stays the
  campaign's one check-in, and the campaign's other work goes on. See
  [the claim rule](https://github.com/reply-team/jason-ai/blob/v0.31.0/docs/work-execution.md#the-claim-rule).
- **The notification counts what waits on a limit.** The pointer gains a sixth count, `counts.held`, said as "2 items wait
  on a budget." with a place to look, `jason workitem list --status waiting`, and recorded in the `notification_sent`
  line. It counts every held item as the pointer is sent, whichever campaign it is in. See
  [the notifications page](https://github.com/reply-team/jason-ai/blob/v0.31.0/docs/notifications.md).
- **A plugin is configured in the settings.** `Plugins:Config:<id>` in `settings.json` is a flat map from a key to text
  that the plugin reads, and a load freezes it as it freezes grants, so `jason plugin reload` applies a change. No file
  of the package changes, so the package stays the one a release published and `jason update` still moves it. A value
  there is a secret: it reaches the plugin on stdin, a value of 8 characters or more is masked wherever Jason writes it,
  and it is in no log, journal line, attempt or `jason plugin list`, unless a plugin returns it, which is the plugin's
  own, as the plugin-author note says. A `jason doctor` bundle withholds the whole section. A value is text, with no
  defaults and no schema, and a key with children is refused by its path. See
  [the plugin guide](https://github.com/reply-team/jason-ai/blob/v0.31.0/docs/plugins.md#6-the-host-sdk).
- **A package may let you grant a host it does not declare.** A manifest that says `capabilities.http.any_granted_host: true`
  lets a person name a host in `Plugins:Grants:<id>:Http`; `*` still means the declared hosts alone. Such a package asks
  for more than one that does not, because your grant is all that holds it to a host, and `jason plugin list` shows the
  word beside its capabilities. See
  [grants](https://github.com/reply-team/jason-ai/blob/v0.31.0/docs/plugins.md#9-capabilities-and-grants).
- **The journal forwarder is 1.1.0.** A receiver on another host takes a grant and a configuration, with no edit of
  the package: `Plugins:Config:journal-forwarder` holds `url` and `token`, which the plugin reads before
  `JOURNAL_FORWARDER_URL` and `JOURNAL_FORWARDER_TOKEN`, and its manifest says `any_granted_host`. It needs Jason
  0.31.0 or newer. A receiver off the loopback interface is reached over `https`. Its
  [README](https://github.com/reply-team/jason-ai/blob/v0.31.0/plugins/journal-forwarder/README.md) shows both entries.
- **`jason subscription list` says when a kind rests.** A kind whose turn could not finish, for a reason of the
  runtime's own, shows when it is offered again as `rest_until`. A rest is kept in memory, so the field is null after a
  restart, and it is not `in_flight.retry_at`, which is a handler's own retry.
- **`jason runtime run --help` prints its usage.** `--help`, `-h` and `-?` print what the verb takes and exit 0 without
  starting anything. The verb now refuses what it does not know: an unknown argument, a `--data-dir` whose value starts
  with `-`, and a `--data-dir` given twice are named on stderr and exit 2. An unknown argument or a second `--data-dir`
  used to be ignored and a runtime started; 0.30.0 refused a value starting with `--` and took one starting with a single
  `-` as the path. The starts Jason makes itself pass only `--detached` and `--data-dir`, so they are unaffected.
- **`troubleshooting-jason` teaches the hold, and is `draft` again.** Its state 11 says what a hold is, where it is seen
  and the three ways out. No agent host has read that text, so its status went back from `verified` to `draft`. The
  skills marked `draft` are `researcher-in-session`, `roles/responder`, `troubleshooting-jason` and the nine skills of
  the business pack.
- **For a plugin author:** `host` gains `host.config(key)`, which answers the text or `null`, and the invocation gains a
  `config` object, absent where nothing is configured; protocol stays 1. A value is text, so a boolean arrives as
  `True` and a number is parsed by your code. The host masks each value, except one under 8 characters, and a
  successful result is not masked at all: what you return is yours to keep clean.
  [The guide](https://github.com/reply-team/jason-ai/blob/v0.31.0/docs/plugins.md#6-the-host-sdk) states both limits.

## Fixed since 0.30.0

- **The Windows toast no longer refuses the longest pointers.** A pointer saying at once that work waits, protective work failed and
  events were not read was longer than the toast's line bound of 200 characters, so Windows refused it: nothing showed and
  the delivery was recorded as failed with `toast_rejected`. The bound is now the delivery contract's 400, which every
  pointer fits. Windows wraps the text and may cut the end of a long one in the banner.
- **A secret holding a character JSON spells as an escape is masked.** Such a character is, for example, `&`, `+`, a
  quote, a backslash or a non-ASCII letter. A granted variable that held one was masked in a failure's message but not in its details and block, nor in a log line's data, where
  it is written in the form JSON gives it. It is masked in all of them now.

## Upgrading from 0.30.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update` and `jason ui install`
  follow, and `jason plugin install journal-forwarder --replace` too if you installed the forwarder and did not edit it; an
  edited copy is the forwarder bullet's below.
- **Rename the responder's limit before you start 0.31.0.** `Responder:MaxLaunchesPerDay`, from any place the
  configuration is read, stops the start until it is renamed to `Launches:MaxPerDay:responder`, and so does the variable
  `JASON_Responder__MaxLaunchesPerDay`, to be renamed `JASON_Launches__MaxPerDay__responder`; nothing is carried over for you. `jason update apply` leaves `settings.json` as
  you wrote it, so with the old key still there the update stands at `started` with `update_not_healthy` and the
  runtime's sentence after "It said:". Rename the key by hand and run `jason update apply` again. A responder that
  0.30.0 held carries `responder_launches_spent`, and 0.31.0 reads that as held too.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. One skill changed its status line, `troubleshooting-jason`, from
  `verified` to `draft`.
- **The data directory:** this release adds no database migration: your projects, campaigns, work items and chronicle
  stay as they were.
- **The Reply plugin** is unchanged at 0.14.5.
- **The journal forwarder** is 1.1.0. `jason update apply` replaces a copy you did not edit, byte for byte as published,
  and leaves one you edited alone, since it is yours; after the install line or `brew upgrade jason`,
  `jason plugin install journal-forwarder --replace` places it. A copy you edited under 1.0.1 to name another receiver
  can go back to the published package: put the receiver's host in `Plugins:Grants:journal-forwarder:Http` and its
  `url` and `token` in `Plugins:Config:journal-forwarder`, as the README shows, then run `--replace` and
  `jason plugin reload`. A forwarder you never installed is left alone.
- **A runtime older than 0.31.0 rejects the forwarder 1.1.0** over the manifest's `any_granted_host`: the whole reload
  is rejected, as every package problem rejects it, so an older runtime needs the forwarder package its own release published.
- **A script of your own that runs `jason runtime run`** is refused if it passes anything the verb does not know, and now a `--data-dir` whose value starts with a
  single `-`, which 0.30.0 took as the path. A hand-made logon registration carrying another flag fails at its next start with exit 2, and its error is not kept on
  Windows and macOS, so run the registered line by hand to read why.
- **From 0.29.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
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

For a sequence email, Reply keeps at most one reply to each email it sent and drops a finished contact's later replies
on that thread before it stores them, so they reach none of `conversation.list`, `conversation.get` and the change
feed, and Jason's inbound sync says nothing of them. After this side has answered on the thread through
`conversation.reply`, the contact's next replies on it are stored and read as usual; a second reply that arrives
before any answer may never be seen. A LinkedIn thread keeps later messages. All of this is read from how Reply is built
and has not yet been observed on an account.

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
