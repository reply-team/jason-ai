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

# Jason 0.30.0

## What is new since 0.29.0

The Reply plugin now sends on every account without first asking Reply whether it keeps direct sends, a person's own
decisions can no longer be made from inside a run, and a campaign's responder is held to a daily limit of runs.

- **The journal forwarder is a release asset.** Each release now publishes it as `jason-plugin-journal-forwarder.zip`
  beside the Reply plugin's package, and `jason plugin install journal-forwarder` places the one your Jason's own
  release published, after checking it against the release's `checksums.txt` and its content digest. You no longer copy
  it out of a checkout. The package is version 1.0.1; its code is as it was, and its
  [README](https://github.com/reply-team/jason-ai/blob/v0.30.0/plugins/journal-forwarder/README.md) now installs it with
  that command and says what an edited copy means for later updates. `jason status` and `jason update check` name the
  forwarder as they name the Reply package, and a forwarder you never installed is never a failure.
- **The Reply package is 0.14.5.** It makes one call fewer before a send: it no longer reads whether the account's Reply
  keeps direct sends before a `message.send` or `social_invitation.send` that names a moment ahead or a sending
  schedule, or before a repeat after an attempt that may have sent, since Reply's direct-send routes are on every
  account. A 404 that carries no code of Reply's own now means that Reply's API changed under this package version, no
  longer that the route is missing: the work ends `provider_call_failed`, permanent, with `reply.refused` and the 404 in
  its details and nothing done, so run `jason update apply` and report the ending if it stays. A repeat whose send
  itself answers that 404 ends `send_not_settled` instead, and the inbox change feed ends `events_unsupported`, whose
  account's replies are read thread by thread as before. The endings `send_after_not_offered` and
  `schedule_not_offered` are gone with the read that reached them. A LinkedIn route that refuses a sending schedule is
  still `capability_not_offered`. The package still needs reply-cli 0.18.0. Its
  [README](https://github.com/reply-team/jason-ai/blob/v0.30.0/plugins/reply/README.md) holds the error table.
- **A campaign's responder is held to a daily limit of runs.** `Responder:MaxLaunchesPerDay` in `settings.json` is 25 by
  default and accepts 1 to 1000; it applies at the next scan. Once a campaign's responder has started that many runs
  since 00:00Z, further responder work for that campaign waits, with no attempt of its own and no notification: the work
  item is `waiting` with the error `responder_launches_spent`, and the campaign's other work goes on. It goes on at the
  next 00:00Z, or sooner if a person resumes that item with `jason workitem resume`, which lets that one item past the
  limit under their name, or raises the limit, which applies to work not yet held. Every run counts, a resumption and a
  responder queued by hand included; another role's run and a provider operation do not.
  [The work-execution page](https://github.com/reply-team/jason-ai/blob/v0.30.0/docs/work-execution.md#the-claim-rule)
  has the rule, and [the inbound-sync page](https://github.com/reply-team/jason-ai/blob/v0.30.0/docs/inbound-sync.md)
  says what it means for a burst of replies.
- **A person's word is refused from inside a run.** Approving or rejecting an approval, setting or taking back a
  standing approval, and answering a question are now answered `403` with the code `decided_inside_a_run` when the
  request comes from a `jason` launched inside an attempt, whatever person it names; nothing is recorded. Inside a run
  the CLI names its attempt in a `Jason-Attempt` header on every request, and the refusal says what to do instead: leave
  the decision to the person (`jason approval list`, `jason decision list`) or raise it as a decision. A program of your
  own that does not run inside an attempt is not affected. This is a higher bar against a role deciding its own work,
  not a security boundary: the runtime still cannot tell a person from a program that holds the token. The
  [Runtime API page](https://github.com/reply-team/jason-ai/blob/v0.30.0/docs/runtime-api.md) states it.
- **Two more skills are `verified`.** `operating-the-installation` and `troubleshooting-jason` were read by a real
  agent host against the published 0.29.0 executable, did the job each teaches, and had every `jason` line they quote
  held to that executable's `--help`; no text changed. The skills still marked `draft` are `researcher-in-session`,
  `roles/responder` and the nine skills of the business pack.
- **The README is new, and the former README is the handbook.** The first page of the repository is now written for a
  reader who has not yet decided to install Jason. The former README, whole, is
  [`docs/handbook.md`](https://github.com/reply-team/jason-ai/blob/v0.30.0/docs/handbook.md), and the new
  [README](https://github.com/reply-team/jason-ai/blob/v0.30.0/README.md) links to it.

## Fixed since 0.29.0

- **A start no longer waits for a slow network share.** At every start the runtime writes each host's missing import
  file (`CLAUDE.md`, `GEMINI.md`) into the harness root and into every folder recorded for a project or a campaign.
  That sweep ran inside the start, so a recorded folder on a slow or absent network share held the runtime's first
  answer, and the CLI's wait for the start, for as long as one directory check on it took. The sweep now runs in the
  background and the runtime answers at once. A folder read, or a role launched, in the first moments after a start may
  therefore not have its import file yet, as
  [the projects page](https://github.com/reply-team/jason-ai/blob/v0.30.0/docs/projects.md) says; a folder moved during
  the sweep is skipped and gains its file at the next start.
- **A subscribed kind whose turn gives out now rests.** In 0.29.0 a fault of the runtime's own that stopped a
  subscribed kind's turn was logged and the kind was offered again at the next scan with no pause, so a fault that
  lasted repeated at every scan. The turn now ends as given out and the kind is offered again only after a wait:
  `Subscriptions:RetryBackoffSeconds` after the first give-out, doubled after each one in a row, and never longer than
  an hour. The setting is read at each give-out. The warning is logged once per streak and again only when the failure
  differs, and it names the kind, the failure and the wait. The streak is kept in memory: a reload keeps it, a restart
  begins afresh, and a turn that ends without a give-out ends it.

## Upgrading from 0.29.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update`, `jason ui install`
  and `jason plugin install reply --replace` follow.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. Two skills changed their status line from `draft` to `verified`.
- **The data directory:** this release adds no database migration: your projects, campaigns, work items and chronicle
  stay as they were.
- **The Reply plugin** changed to 0.14.5. `jason update apply` replaces an installed Reply package that is an earlier
  version this release records, byte for byte as published, and leaves one built or edited on your machine alone; after
  the install line or `brew upgrade jason`, which run no apply, `jason plugin install reply --replace` places it.
- **The journal forwarder** is installable from the release with `jason plugin install journal-forwarder`. A copy you
  took from the repository for 0.29.0 and did not edit is replaced by `jason update apply` with 1.0.1; a copy you
  edited, as a receiver other than `localhost:8787` needs, is yours and is left as it is, so there is nothing to do, and
  `--replace` would discard your edit.
- **A responder past 25 runs a day** is held from this release: for a campaign whose responder ran more than 25 times in
  a UTC day, the 26th run waits until 00:00Z. Raise `Responder:MaxLaunchesPerDay` in `settings.json` to keep the old
  pace.
- **A run's agent that approved or answered in a person's name** is now refused; the person decides.
- **From 0.28.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
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

Through Reply, a LinkedIn message that names a sending schedule ends `capability_not_offered` and nothing is sent: Reply's route for a LinkedIn message takes a moment and not a schedule. Propose the message without a schedule, so it goes at once, or on another channel; asking again meets the same answer.

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
