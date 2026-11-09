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

# Jason 0.33.0

## What is new since 0.32.0

An installation is taught its role skills as it is installed, from a package of them the release publishes, so a
machine needs no `git` to receive its skills; `jason status` says what a logon registration means and gives the role
skills' deployment as fields; and two new verbs move an installation to another machine.

- **The skills are a release asset.** `jason-skills.zip` holds `skills/` at the tag, both packs, and at its root a
  `source.json` that names the public repository, the tag and its commit. It is listed in `checksums.txt` after the UI's
  package, and a release is now fourteen files. With no `--source`, `jason skills install` and `jason skills update`
  take the package of the release this build was published as, from this repository's releases on GitHub, and check it
  against that release's `checksums.txt` before anything is written, so the machine needs no `git`. `--ref` takes the
  package of another release's tag; a ref that is not a release's tag is refused before any request, naming
  `--source <git url> --ref <branch>` instead. `--source <git url>` still clones with `git`, `--source <dir>` reads a
  checkout where it stands, and `--feed` names "a different release feed: the https address of its manifest.json, or
  http on this machine". See
  [Teach it](https://github.com/reply-team/jason-ai/blob/v0.33.0/docs/INSTALL.md#4-teach-it).
- **The install scripts teach the role skills as their last act.** After the PATH step and before the licence lines,
  `install.sh` and `install.ps1` run the installed executable's `jason skills install --roles-only`, which takes the
  skills from the release's `jason-skills.zip` and writes them into Jason's data directory and nowhere else, and they
  show what it prints. The scripts never start the runtime and never register it at logon: both stay yours, and
  `jason status` names each. When the step fails, with no network say, Jason stays installed and the script still
  succeeds, ending with one line: "The role skills were not deployed; the lines above say why. Run
  `jason skills install --roles-only` once that is dealt with; `jason status` names it as well." A script pointed at a
  feed in a directory, with `--feed` or `-Feed`, leaves the step out and says what to run instead, because the skills
  verb reads no directory.
- **`jason status` says what a logon registration does at the next sign-in.** The `autostart` row says "Registered at
  logon: the runtime starts by itself each time you sign in." or "No logon registration: the next time you sign in, the
  runtime stays off until it is started by hand.", with the same repair as before; whether the runtime runs now is the
  `runtime` row's to say. `jason runtime autostart status` takes the same words: "No logon registration." and, on a
  machine that cannot register one, "No logon registration, and this machine has no way to register one."
- **`jason status` gives the role skills' deployment as fields.** In the JSON, the `role_skills` check carries
  `details`: `ref`, `source`, `commit` (empty where the record names none) and `roles_taught`, how many roles the runtime
  reports it teaches, which is left out where the runtime did not answer or reported that it could not read its role
  root. With no record, or one that cannot be read, there are no `details`, and no other check carries any. `--human`
  prints the facts alone, as before. A `jason doctor` bundle writes a skill source other than the public repository as
  `<skill-source>` in `details` as it does in the fact, and its ref too unless that is a release tag.
- **`jason export` and `jason import` move an installation to another machine.** They are the CLI's tenth and eleventh
  declared exceptions: each changes this installation's files rather than asking the runtime anything, apart from the
  stop, the start and a link per project folder, and neither reaches a network beyond the runtime's own loopback.
  - **What travels.** `jason export [<file>]` writes `jason-export-<UTC stamp>.zip` in the folder it runs in, or the
    file you name, and never over a file that is already there. It carries the database with its `-wal` and `-shm`
    when they are there; `config/`, `plugins/`, `skills/roles/`, `ui/custom/` and `logs/`, each whole, the role skills'
    record included; the harness root's tree as it stands, every folder in it whole, a `.git` included; and
    `export.json`, which names "the Jason version, the moment, the operating system, the data directory, the harness
    root, the home directory, whether the runtime starts at logon, and every file with its size and SHA-256".
  - **What does not.** The backups under `state/backups/`, `run/`, `work/`, the skills' staging folders, the shipped UI
    under `ui/default/`, a `replaced-<stamp>/` directory an import moved aside, "and anything outside the data directory
    and the harness root": the skills deployed into agent hosts among them, which `jason skills install` deploys again.
  - **Secrets travel whole.** "The archive carries the settings file with everything in it, a token under
    Plugins:Config included: keep the archive as you keep that file."
  - **`--stop`.** Without it, both verbs refuse while the runtime runs, naming `jason runtime stop`. With it,
    `jason export` stops the runtime "and starts it again once the archive is written, and starts none that was not
    running"; `jason import` stops it and, as every import does, starts the runtime on what it placed.
  - **`--replace` moves aside and deletes nothing.** Before anything is written, `jason import <file>` refuses an
    archive it cannot read, a name in it that escapes, a file whose size or SHA-256 is not the one `export.json` lists,
    and an archive a newer Jason wrote, naming the version to install first. An installation already here, "a database,
    or a harness root with a marker in it", is refused without `--replace`, naming what it holds. With it, the data
    directory's parts go into `<data>/replaced-<stamp>/` and the harness root to `<root>.replaced-<stamp>`, each named
    as yours to remove.
  - **Paths are made this machine's.** In the settings file, "every absolute path under the other machine's home
    directory, data directory or harness root is rewritten to the same path here, each rewrite said, Harness:Root
    first; a path outside those three that is not on this machine is said and left as written". Then the runtime
    starts, and the import asks it to `project.link` each project folder by its marker, with this machine's path; a
    refused link is said, the folder stays, and the answer's `next` carries
    `jason project link <id> --path <folder>`.
  - **The logon registration travels as a fact.** The import "registers at logon when the other machine did, or says
    this machine cannot". The registration itself is never a file in the archive.
  - **Left to you**, in this order: `jason skills install`, for this machine's agent hosts;
    `jason runtime autostart enable`, if the runtime should start at logon here and the import did not register it;
    `jason status`.

  See
  [Moving the installation to another machine](https://github.com/reply-team/jason-ai/blob/v0.33.0/docs/INSTALL.md#8-moving-the-installation-to-another-machine).
  `operating-the-installation` teaches the same move.

## Fixed since 0.32.0

- **A plugin that cuts text inside a character keeps its outcome.** A plugin may cut a provider's text at a count of
  UTF-16 units, as `slice(0, 200)` does, and leave half of a surrogate pair at the end; the plugin host could not read
  what the plugin handed it, and the work item ended `plugin_no_outcome` instead of the failure or the result the plugin
  declared. The host now writes such a lone half as U+FFFD, the replacement character, in any string a plugin hands
  it: a result, an identifier, a failure's message, details or block, a log line's data, a host function's options, a
  key. A whole pair is written as it is. No package changed. See
  [the plugins page](https://github.com/reply-team/jason-ai/blob/v0.33.0/docs/plugins.md).
- **`jason update apply` names the packages that are not installed on one line.** Two or more are said on the line
  where the first of them stands, as `jason status` says them, such as
  ``plugins: slack and telegram are not installed, so none was placed; `jason plugin install <id>` installs one``; one
  package keeps a line of its own.
- **A release candidate's build reads its own release.** A build such as `1.0.0-rc.1` takes its skills from its own
  tag, which its release publishes, and `jason status` compares its default UI with it, so a stale one is called stale.
  Only a development build points at the release it is on the way to.

## Upgrading from 0.32.0

Nothing stops: no setting is renamed, and no start refuses a setting 0.32.0 took.

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line now ends by deploying the role skills; after it or Homebrew, `jason skills update` and
  `jason ui install` follow, and `jason plugin install <id> --replace` for each package you installed and did not edit.
- **Where the skills come from:** the default skills download goes to this repository's releases on GitHub directly,
  as `git` did before, and not through the address ReplyApp Inc. operates for the runtime's own downloads, so
  [what the responsible-use page says of it](https://github.com/reply-team/jason-ai/blob/v0.33.0/docs/responsible-use.md#update-check-and-downloads)
  holds word for word and the page is unchanged.
- **No `git` any more:** an installation whose record names this repository's git address, which every default
  deployment before 0.33.0 names, is read as the default: from the next `jason update apply` on, or the next
  `jason skills update`, it takes `jason-skills.zip` and its record names the new default. A deployment made at a tag
  chosen with `--ref` stays at its ref, a record of any other source keeps it, and a git repository named with
  `--source` still needs `git`.
- **The packages:** reply 0.14.6, journal-forwarder 1.2.0, ntfy 1.0.3, slack 1.0.0 and telegram 1.0.0, as in 0.32.0;
  no package changed in this release. A release is fourteen files, `jason-skills.zip` the new one.
- **Skills:** the update redeploys them wherever a deployment was recorded. Two skills changed their text and keep
  `draft`: `operating-the-installation`, which now says what the install script deploys and how to move an
  installation, and which no agent host has read against 0.33.0; and the `responder` role, whose reason for acting or
  parking at once now reads "A provider may not detect a contact's later reply unless it answers a message this side
  sent". The skills marked `draft` are `operating-the-installation`, `researcher-in-session`, `roles/responder`,
  `troubleshooting-jason` and the nine skills of the business pack.
- **The data directory:** this release adds no database migration: your projects, campaigns, work items and chronicle
  stay as they were.
- **Going back:** what `jason update apply --version` to an older release does to a newer plugin package, and how to
  load one again, is in
  [Applying an older release](https://github.com/reply-team/jason-ai/blob/v0.33.0/docs/release-and-update.md#applying-an-older-release).
- **From 0.31.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the known issue about that update below says.

## Known issues

An import on the same machine, into a second data directory, "needs the source harness root moved aside first: the
records name its folders, and project.link refuses a folder present elsewhere (harness_folder_present)". The import
names each refused link, and its `next` carries `jason project link <id> --path <folder>` for each.

`jason status` calls autostart `ok` from the registration alone: a registration whose executable is not there any more
reads as registered. `jason runtime autostart status` reads the executable it names, and says "The executable it runs
is not there any more" with its path.

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
