# Installing Jason

This page takes a machine with nothing on it to an installation that can start work, and then takes it off
again. It is written to be followed by a person or by an agent, in order, without reading anything else first.

On macOS and Windows nothing here needs administrator rights or a password; on Linux the deb does, once each time apt
installs or takes it off, where there is apt. On Windows everything is installed under your own account, and so is an AppImage on Linux. On
a Mac the application goes to `/Applications` where your account may write there, else `~/Applications`, and the deb
installs Jason Desktop for every account of the computer, at `/opt/Jason`; the skills and Jason's data are your
account's own everywhere, and the terminal command everywhere but the deb's `/usr/bin/jason`.

---

## 1. What you need

Jason is used from a release: nothing in its repository is built, cloned or tested to use it, which is for changing
Jason (§3). **From a release (§2):** a shell to install it with, and nothing else — the archive carries its own runtime, and the
script ends by teaching it its role skills (§4). On macOS, Windows and Linux they come with Jason Desktop: the set-up reads them
from the folder beside the `jason` it carries, with no network. A command-line install made by hand teaches them by a
fetch of its own, and so does a later `jason skills install` on any system: a release takes its skills from the package of them it published,
`jason-skills.zip`, over the network, at the tag it was published as. A machine without the network teaches it from a
checkout instead, with `--source`, which also takes a git repository at a ref;
only that needs `git`.

You also need a directory you can write to, and — if you want an agent harness taught — that
harness installed, so §4 has somewhere to deploy.

The background roles run on an agent host you installed and signed in to as yourself, through an execution profile, and
an installation starts with none: until a profile is made and named the default, every AI role's work fails at its claim
with `role_not_launchable`, and `jason status` says so under `roles_launchable`. For Claude Code, run
`jason profile create claude-code --host claude_code --program claude`, then
`jason profile set-default claude-code`, which the runtime writes to the settings file and applies without a restart.
Jason Desktop does both itself, in Live, where the installation holds no profile and names no default and
Claude Code or Codex is connected there: on the one its chat is set to run on, else Claude Code, else Codex
([desktop.md](desktop.md#connect)).

To have a host other than Claude Code run the background roles, install it from its own documentation and sign in
as yourself; Jason holds no key, and the profile you make for it is named the default the same way. For Cursor CLI, install it from
[its installation page](https://cursor.com/docs/cli/installation.md), run `agent login` and
`jason profile create <name> --host cursor_cli --program agent`. On Windows, where `agent` is not found by the runtime's
search path, name the launcher by its full path: `--program <full path of agent.cmd>`.
For Codex CLI, install it from [its own page](https://github.com/openai/codex), run `codex login` and
`jason profile create <name> --host codex --program codex`. On Windows, Codex needs a one-time setup by an
administrator first, which [the Codex section](execution-profiles.md#codex-cli) describes.
For GitHub Copilot CLI, install it from
[GitHub's installation page](https://docs.github.com/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli),
run `copilot login` and `jason profile create <name> --host copilot_cli --program copilot`.
For Gemini CLI, install it from [its own page](https://github.com/google-gemini/gemini-cli), sign in as yourself and
run `jason profile create <name> --host gemini_cli --program gemini`.

For Antigravity CLI, install `agy` from the CLI tab of [Google's quickstart](https://antigravity.google/docs/getting-started?tab=cli),
run `agy` once to sign in as yourself ([how Google describes signing in](https://antigravity.google/docs/install/)), and
`jason profile create <name> --host antigravity_cli --program agy`. Each run is given its permissions by a workspace hook the launch
writes (built from the host's hooks page, not yet run on an account). If a role is refused on `antigravity_cli`, add two
rules to your own `~/.gemini/antigravity-cli/settings.json`, in its `permissions.allow` array: `command(jason)` (on Windows
shells, `command(regex:jason .*)`) and `read_file(<harness folder>)`; the
[host matrix](execution-profiles.md#12-the-hosts) has the two lines of JSON and the details.

### If an agent installs it

If the person gave you these three lines:

```text
Install Jason by following https://get.jasonharness.ai/install
When it reports Ready, start its quick-start skill and take me to my first campaign.
If anything stops you, ask me directly and tell me why.
```

they have asked for the install, so do it without stopping to confirm a step. Ask only for an act this page marks
**Ask first.**, or where your own checks stop you, and then ask the person directly and say why. Given only the link,
you do not have that yes: ask before the install and before each act this page marks **Ask first.**

Given only the link, or only the repository's address, that question before the install says what is installed. On
macOS, Windows and Linux, Jason is one installation: the Jason Desktop application, which carries `jason`, the command
line your terminal and your agents use. It is installed once, updated once and removed once. The rest of this page
installs it, and there is nothing to choose. Given the three lines, the person has asked for the install this page
makes: do it, with no choice offered.

An act this page marks **Ask first.** needs the word of the person you install for: ask, and do it only on their
yes. With nobody to ask, leave it undone and say so. A question tool that comes back with no answer is nobody to
ask.

The install script, and on Windows the installer, put Jason's skills where Claude Code and Codex read them, each where
it is found (`~/.claude/skills`, and `~/.agents/skills`, which Copilot, Cursor and Gemini CLI read too): nobody is
asked, and where your own host is one of those the step below changes nothing there.
If the person gave you those three lines, they are also the person's yes to putting Jason's skills where your own
agent reads them: run `jason skills install --host <your host>` (§4; `<your host>` is one of the host words of
[the host matrix](execution-profiles.md#12-the-hosts), such as `claude_code`) as a step of the install, without asking again.
That yes covers your own host and nothing else this page marks **Ask first.** Where this machine has other agent hosts, skills for them
are not done now: you say so at the end, in one line with the command (§4). Given only the link, the mark in §4 stands. That line
names only a host the install script did not give its skills.

This page is served as plain text at `https://get.jasonharness.ai/install`, the address the README's prompt names.

- Start your session without external connectors — on Claude Code, `--strict-mcp-config`. The install needs none.
- Do §2, then §4 to §6, in order, and stop there. §3 is for a build from the runtime's repository, and
  §7 takes the installation off again.
- Install by the first route that fits. Where §2 names a package for this platform and its package manager is
  already on this machine, use it (*With a package manager*); where it names none, take the next route. Where the
  package fails, or where it or your harness refuses it, say so once and take the next route. On macOS, Windows and Linux that is this release's install
  script: download `install.sh` or `install.ps1` from the release (§2) into a file, and run that file (`sh <file>`;
  `powershell -NoProfile -ExecutionPolicy Bypass -File <file>`). It installs Jason Desktop and opens nothing, puts
  `jason` in the terminal, deploys the skills for Claude Code and Codex where each is found and the role skills, and ends non-zero,
  saying which act failed, when `jason` is not in the terminal. On an Intel Mac it stops before it downloads anything,
  since it installs Jason Desktop on Apple silicon only: report that as it stands. On Linux, where there is apt, it
  installs a system package, which asks the person for their password: in their desktop's own dialog when your session
  has no terminal, and where there is neither it stops before anything is installed, saying so: report that as it stands. Its last line is the full path of `jason`: call
  `jason` by that path for the rest of your session, which does not see the new PATH. On Linux, where your harness
  refuses the script, it is the route by hand of §2 *Jason Desktop*: download the deb where there is apt, else the
  AppImage, and `checksums.txt` from the release, check the digest and stop on a mismatch, and hand the file to the
  person to install or open. That is the next route, not a
  way around the refusal. Never pipe a download into a shell. Where your harness refuses the script or the route by
  hand, say so and give the person the one-liner to type themselves.
- Write only what those steps write: what the install script writes — the application in its place (on Linux the
  system package and Jason's apt repository, or the AppImage with its menu entry), the terminal command `jason install`
  places, and the skills for Claude Code and Codex where each is found; the executable and
  its install directory; this account's PATH entry for it;
  Jason's data directory, which is where the role skills go; the system's temporary directory, which the download
  goes into; and the directory the executable unpacks its native libraries into when it first runs — under
  the temporary directory on Windows, under `~/.net` in this home directory on macOS and Linux. Nothing in your
  own agent's configuration unless the person says so (§4), except the skills that the install prompt's yes covers. A package manager's own PATH change is part of the
  install you were asked for, and so is the one-liner's when the person types it; a PATH edit of your own, by hand
  (§2) or by the repair (§5), is marked **Ask first.**
- Where the install's report names a skill left alone (`left_alone`, with its sentence under `left`), tell the person in
  that sentence's sense: the folder is left as it is, Jason's skill of that name is not installed for that host and an
  agent asked to use it reads their folder, and to have Jason's they move or remove that folder, then run the command
  the sentence names. The folder is theirs to move; never move or remove it yourself.
- With a no to the PATH entry, it is the person's own step: finish everything else, then hand them the exact line
  `jason status` prints for `path`, and say that `jason status` again proves it. Never end on "not ready" alone:
  name the step that remains.
- Install nothing but Jason, and build nothing (§3). A 404 from a
  download is §2's: stop and report exactly that. Leave no long-running process behind except the runtime that
  `jason runtime start` starts.
- A refusal — by your harness, or by a package manager — is reported once, as it stands, and never worked around.
  Working around it is a retry, the same command rewrapped, or the refused code run by another means, such as a
  downloaded script run another way. Taking the next route in the order above is not working around it.
- After `jason status` (§5), branch on `ready` in its JSON body, never on the exit code. For each required check
  that is not ok, run the repair it prints. Optional checks are reported, not repaired: their repairs write into
  your agent's configuration or into what starts at logon, which is the person's to allow.
- A profile you append to is read by a login shell, not by your next non-interactive command (§2): carry the
  `export` half yourself, or call `jason` by its full path.
- The install is done when `ready` is true. If a required check still fails after its repair, stop and report which
  checks failed.
- **The hand-over.** The install prompt's "Ready" is `jason status` answering with `ready` true in its body. When it
  is, send one message, in this order: two lines saying that Jason is installed and where it keeps the person's
  files — the folder the `harness_root` check of `jason status` names, which is the one in force; two lines saying
  exactly what you ran and what it changed, never as a question; and one line each, with its command, for what is not done now: starting the runtime at logon (§6) and,
  where this machine has other agent hosts, skills for them (§4). Then start the `quick-start` skill at once, with no question
  about what comes next: its question goes in a message of its own. The last line of that first message names Jason
  Desktop once, as part of what was just installed, the window onto this installation with its
  own chat, which the person opens as any other application and you do not open for them. Where your host does not see a skill installed a
  minute ago, read its file at `<root>/quick-start/SKILL.md`, where `<root>` is the one on the `Target:` line of
  `jason skills install` that names the `jason-runtime-skills` pack and your host.

---

## 2. Install from a release

```sh
curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh
```

```powershell
irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex
```

Jason is released under the MIT licence: [LICENSE](../LICENSE). What it sends off your machine, and what stays your
responsibility when it reaches people, is in [responsible-use.md](responsible-use.md).

Both lines install the **latest release**. If either answers 404, the release could not be reached — through the
address or on GitHub — and that is not a failure of your machine: report it as it stands rather than run the line
again, and build nothing instead.

On macOS, Windows and Linux, Jason is one installation: the Jason Desktop application, which carries `jason`, the command line
your terminal and your agents use. It is installed once, updated once and removed once (§7).

**On macOS and Windows each script installs Jason Desktop**, with `jason` in the terminal, and opens nothing:

- `install.sh` on a Mac reads the machine's architecture (`sysctl hw.optional.arm64`, which a shell running under
  Rosetta does not change) and stops on an Intel Mac before it downloads anything, since it installs Jason Desktop on
  Apple silicon only. On Apple silicon it downloads `jason-desktop-macos-arm64.zip` and `checksums.txt`, **verifies its
  SHA-256**, unpacks it with `ditto` into a folder beside its place, checks with `codesign --verify --deep --strict`
  that the application is whole and signed by a Developer ID certificate Apple issued to the team that signs Jason,
  and moves it into place by one rename: `/Applications` where your account may write
  there, else `~/Applications`, so that its own updates never ask for a password. A `Jason.app` already in either
  folder is never renamed, replaced or deleted from a terminal: one of 0.49.0 or later is kept as it is and set up, once
  its signature passes the same check, the
  last line naming `jason update apply` where it is older than the release; an earlier one is left as it is, and the
  script ends non-zero saying to update it from its own menu (Check for Updates) and run the line again.
- `install.ps1` downloads `jason-desktop-windows-x64-setup.exe` and `checksums.txt`, **verifies its SHA-256** and its
  signature, ReplyApp Inc.'s, runs it silently with `Start-Process -Wait` into `%LOCALAPPDATA%\Programs\jason-desktop`,
  and reads its exit code. The installer sets the machine up itself, at every install and update. A later version
  already installed is kept as it is; an earlier one is installed over, as its own update does, but never while
  Jason Desktop is running, in its window or in the tray: the script says to quit it and run the line again. The
  silent installer never closes it either: finding it running, or unable to look for it, it changes nothing and ends
  with exit code 3, which the script says in the same words.
- Then each runs the `jason` the application carries as `jason install --roles` and shows its one JSON report: `jason`
  put in the terminal — on a Mac the wrapper `~/.local/bin/jason` and the PATH lines below, on Windows the
  application's `resources\bundle` folder on your account's Path — Jason's skills for Claude Code and Codex, where each is found (`~/.claude/skills`, and `~/.agents/skills`, which Copilot, Cursor and Gemini CLI read too), an older command-line install of Jason retired, and the role skills in Jason's data directory. A skill folder of yours under a name Jason ships, one no record of Jason's names, is left as it is, nothing of it written or recorded, and the report names it under `left_alone` and in a sentence under `left`. Where a
  runtime answered before the install and none answers after it, or the report says it stopped one, the script starts
  one again from the application's `jason`.
- It ends non-zero, saying which act failed, when `jason` was not put in the terminal. Run again over the version
  already installed, it downloads nothing and says so, and sets the machine up again, which changes nothing that is in
  place. Its last line is the full path of `jason`, to type in the terminal it ran in, which does not see the new PATH.

**On Linux `install.sh` installs Jason Desktop too**, as the system's users install comparable applications, and opens
nothing. It reads the release's `manifest.json`, installs no release before 0.49.1 (an earlier one's own script installs
the command line alone there), and stops before anything is read when run as root, when given `--install-dir` or
`--no-modify-path`, or from an agent that runs it confined to its sandbox, a process with `NoNewPrivs` set, where
neither `sudo`, `pkexec` nor `fusermount` can act: it says to run the line in your own terminal, or to let the agent run
it outside its sandbox. The
system decides the form:

- **Where there is apt** (`/usr/bin/apt-get` and `/usr/bin/dpkg`: Ubuntu, Debian, Mint): the release's
  `jason-desktop-linux-x64.deb` and `checksums.txt`, **its SHA-256 verified**, installed by apt with the one
  administrator's password a system package needs; every program this route runs as root is named by its path in
  `/usr/bin`, never looked up on your PATH. Where the line
  has a terminal, `sudo` asks for it; otherwise, as when an agent runs it in your desktop session, your desktop asks in
  its own password dialog, through `pkexec`; apt waits up to ten minutes for the package system when Ubuntu's own updates
  hold it, and the install is given twenty. A password not given, the dialog left unanswered for those twenty minutes, no
  session to ask in, the dialog failing, the package system still held are each refused in their own words, and nothing
  is installed; apt failing part-way says so, with the commands that finish the package or take it off. The package puts Jason Desktop in
  `/opt/Jason` for every account of the computer, `jason` in every terminal as `/usr/bin/jason`, and Jason's apt
  repository in `/etc/apt/sources.list.d/jason-desktop.sources`, signed by the key it carries, so its later updates come
  with the system's (§4). A package already at the version, or a later
  one, is kept and set up again. A runtime of an earlier version still running once the package is this one is left
  running, since a restart ends a turn it may be running: the line says that Restart Jason and Its Runtime to Finish the
  Update, which Jason Desktop offers when it opens, or `jason runtime restart`, brings it up.
- **Where there is no apt** (Arch, Fedora and the rest): `jason-desktop-linux-x64.AppImage`, **its SHA-256 verified**,
  for your account alone and with no password: placed at `~/Applications/jason-desktop-linux-x64.AppImage` by one
  rename, its menu entry and icons written under `~/.local/share`, and the `jason` it carries copied under
  `~/.jason/desktop/bundle/<version>/`, which is the one your terminal runs. Before anything is downloaded it stops where
  the system refuses the unprivileged user namespaces the application needs for its sandbox, which Jason never runs without (on Arch,
  the `linux-hardened` kernel or a setting that turns them off), or has no setuid `fusermount`, which opens the file
  (install `fuse3`). A file of that name that is not Jason Desktop is left as it is, and nothing is installed; a Jason
  AppImage already there stays the installation, apt or not, and one older than 0.49.1, whose `jason` cannot set Linux up,
  is refused before anything is written: update it from its own menu (Check for Updates), then run the line again.

Either way it changes nothing while Jason Desktop runs and the install would change it: it says to quit Jason first.
Then, as on a Mac, the `jason` the application carries runs `jason install --roles` and the script shows its report:
`jason` in the terminal — for the deb the package's `/usr/bin/jason`, with no PATH line; for an AppImage the wrapper
`~/.local/bin/jason` and the PATH lines below — Jason's skills for Claude Code and Codex where each is found, an older
command-line install retired, and the role skills, read from the folder beside the carried `jason` with no network. It
ends non-zero, saying which act failed, when `jason` was not put in the terminal, and its last line is the full path of
`jason`. The command-line archive, `jason-linux-x64.tar.gz`, is still published, for the update of a command-line install
made before; the script does not install it.

**On a Linux machine with no desktop** — a server reached over ssh, a container, an account that is root — this is what
the line and `jason update apply` do today. The line refuses before anything is read when run as root, which a
container's shell often is, and where `NoNewPrivs` is set, as an agent's confinement sets it. Where there is apt it
installs the deb only where the line has a terminal for `sudo` to ask in, an ssh session with one (`ssh -t`) say; with
none, as when ssh runs the line with no terminal, there is no desktop dialog to ask in either, and it refuses, naming the
commands that install the deb by hand. Where there is no apt it installs the AppImage only where the system lets it make user namespaces and
has a setuid `fusermount`, which a container often does not. Once installed, `jason` and its runtime need no display;
Jason Desktop's window does. `jason update apply`: the deb's `jason` refuses with `update_managed_by_package_manager` and
prints apt's commands, which `sudo` runs over ssh; an AppImage's refuses with `update_carried_by_desktop` where there is
no display, since the Jason Desktop it hands the update to needs one, and as root with `update_elevated`, changing
nothing; a command-line install of an earlier version is updated, and its move into Jason Desktop takes the form the
line would, refused where the line refuses: the deb's password is asked through `sudo` where there is a terminal, and
with none, or as root, the move stays pending and the command line answers as before.

On macOS, and for an AppImage on Linux, the PATH line goes into the profile a login shell of yours reads, and for `bash` and `zsh` into
`~/.bashrc` or `~/.zshrc` as well: the terminal a desktop opens — GNOME's, for one — is not a login shell, reads
that file and not the login profile, and so finds `jason` without your signing out. For any other shell the script
says to sign out and back in, because only a new sign-in reads the login profile again. Every run ends with the
licence it installed under, where the release has one, and then the one command to run next, `jason status` (§5),
which says what this installation still needs and the command for each.

Then, before the PATH step, each script says where Jason will keep what it learns about your business, and where a
campaign goes that names no project. On Windows, for an account whose home directory is `C:\Users\Marta`:

```text
Jason keeps what it learns about your business in plain files you can read and edit, one folder per project and campaign, in `C:\Users\Marta\Jason` by default; to keep them somewhere else, set `Harness:Root` in the settings before your first campaign.
Campaigns you create without naming a project go to the project `Default`; rename it, or make one project per product.
```

That folder, the harness root, is `Jason` in your home directory — `/home/marta/Jason` on Linux, `/Users/marta/Jason`
on macOS — unless `Harness:Root` in `~/.jason/config/settings.json` names another; a script cannot know what the
runtime makes of its settings, so it says "by default", and `jason status` names the root in force. The install makes
no folder there. The runtime makes the root the first time a project or a campaign is created, and each project and
campaign a folder of its own below it, each with the marker that makes it Jason's, `.jason/marker.json`, an empty
`AGENTS.md` for your own words, and one line, `@AGENTS.md`, in each file a host reads in its place (`~/Jason/CLAUDE.md`
at the root for Claude Code; the [host matrix](execution-profiles.md#12-the-hosts) lists them), so that it reads them
too; a `Jason` folder already there is taken as the root then, its marker written and
nothing it holds changed, unless it is marked as something else, which `jason status` reports. `Harness:Root` set
before that first create is all it takes. A folder you move later is recorded
again with `jason project link`, the root's once `Harness:Root` names where it is now. Jason never writes a word of
those memory files, never runs `git` there and never writes a `.gitignore`. `git init` in the root is yours to run:
it gives the folder a history, and lets Codex — which reads the `AGENTS.md` files from the top of a repository down
to the folder it starts in — read the root's and the project's with a campaign's.

| Where it goes | Platform |
|---|---|
| `/Applications/Jason.app`, or `~/Applications/Jason.app`; `jason` at `~/.local/bin/jason` | macOS |
| `%LOCALAPPDATA%\Programs\jason-desktop\`; `jason.exe` in its `resources\bundle` | Windows |
| `/opt/Jason/`; `jason` at `/usr/bin/jason` | Linux, where there is apt (the deb) |
| `~/Applications/jason-desktop-linux-x64.AppImage`; `jason` at `~/.local/bin/jason` | Linux, elsewhere (the AppImage) |

The line runs as you. Jason Desktop and everything it sets up are your account's own, so `install.sh` run as root, with
`sudo`, and `install.ps1` run in an elevated PowerShell — an administrator's, or another account's than the one signed
in — stop before anything is downloaded, and say to run the line again as yourself. On Linux, where the system's package
needs an administrator's password once, the line asks for it itself.

`--version` (`-Version` for `install.ps1`) is the one flag taken, from 0.49.0 on macOS and Windows and from 0.49.1 on
Linux: `--install-dir` and `--no-modify-path` (`-InstallDir`, `-NoModifyPath`) are refused before anything is read or
written, since the application has its place and every install puts `jason` in the terminal. A release's own script
before 0.49.1 still takes them on Linux, where it installs the command line:

- `--version X.Y.Z` installs a particular release instead of the latest. The script checks that the manifest
  it gets back is the release you asked for, rather than trusting the feed to have answered the question you
  put to it.
- `--install-dir <path>` installs somewhere else — into a directory of its own, for the reason below. A directory
  whose name holds `:`, the character that separates the entries of PATH, is refused before anything is written, with
  `--no-modify-path` too: no PATH entry can name it, so neither you nor the agents Jason starts could reach that
  `jason` by name. Where the script writes the PATH entry itself, it also refuses a name that entry cannot carry — a
  `"`, `$`, backtick, backslash or line break, which the profile line reads inside double quotes — and says the two
  ways on: another directory, or `--no-modify-path` and the entry put on the PATH your own way.
- `--no-modify-path` leaves your PATH alone. Then `jason` is only typeable by its full path until you put it
  on the PATH yourself, as below.

A one-liner takes them like this:

```sh
curl -fsSL https://get.jasonharness.ai/releases/download/v0.49.2/install.sh | sh -s -- --version 0.49.2
```

```powershell
& ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v0.49.2/install.ps1))) -Version 0.49.2
```

These install 0.49.2 with 0.49.2's own script: a pinned install fetches the script from the release it installs.

**A directory of its own.** `jason uninstall` takes a directory's PATH entry off only where the directory holds
nothing but Jason. Installed into a directory other programs share, the entry stays, and so does everything else it
puts on the PATH.

**Putting it on the PATH yourself.** With `--no-modify-path` nothing did this for you.

**Ask first.** On Windows, add its directory to your account's `Path`; on macOS and Linux, append an `export` line
to the one profile a login shell of yours reads — `~/.zprofile` for `zsh`; for `bash` the first of
`~/.bash_profile`, `~/.bash_login` and `~/.profile` that exists, because bash never reads `~/.profile` once one of
the others does; `~/.profile` for every other shell — and, for `bash` and `zsh`, to `~/.bashrc` or `~/.zshrc` as well,
which a terminal opened without a login reads. `jason status` prints the exact line for your platform — a
PowerShell line on Windows, an `sh` line elsewhere — when it finds that `jason` does not resolve (§5), so the
simplest path is to run it and type what it says.

One thing that line cannot do for you: a profile is read by a shell that starts after it is written. A person
opening a new terminal gets it — where the shell has no file of its own for that, as `bash` and `zsh` do, only after
signing out and back in; an agent running one command at a time in a non-interactive child shell (`bash -c …`) has
written the profile but will not see it in its own next command. If you are an agent, carry the `export` half
of that line yourself, or call the executable by its full path until a new login shell exists.

Confirm what landed:

```sh
jason --version
```

The terminal you typed the install into has not heard of the new PATH entry: open a new terminal — or sign out and
back in, where the script said so — or type `jason` by the full path the script's last line gives:
`~/.local/bin/jason --version` on macOS and for an AppImage on Linux, `/usr/bin/jason --version` for the deb, and on Windows
`& "$env:LOCALAPPDATA\Programs\jason-desktop\resources\bundle\jason.exe" --version`.

A started runtime asks the release feed whether a newer version exists five minutes after it starts, and once a
day after that, at the default settings: one `GET`, carrying nothing about you or your work — it identifies only
the runtime's version, including its build. `Update:CheckEnabled` set to `false` in
`~/.jason/config/settings.json` turns that off. That request, and the downloads the install scripts,
`jason update apply` and `jason ui install` make, go to `get.jasonharness.ai`, an address Reply.io operates, which redirects them to this
repository's releases on GitHub. What that address keeps, and for how long, is in
[responsible-use.md](responsible-use.md#update-check-and-downloads). The skills are the exception: their package is
read from this repository's releases on GitHub directly, by the install scripts' last step, by `jason update apply`'s
skills step and by `jason skills`.

### With a package manager

On macOS, Windows and Linux the route is the install script above, which installs Jason Desktop. On an Apple silicon
Mac, from the candidate that carries it, Homebrew's cask installs the same Jason Desktop, from the same release:

```sh
brew install --cask reply-team/tap/jason
```

Open Jason once from Applications: its first start sets up the terminal command and the skills, as the disk image does,
and Jason updates itself. `brew uninstall --zap --cask jason` takes it off with what Jason set up; the data directory and
the harness root stay. Homebrew's older formula is the command line alone, without Jason Desktop: where it is
installed, `jason runtime stop` and then `brew uninstall --formula jason` take it off first, or its `jason` stays in the
way.

Where a package manager installed Jason, it is under the same MIT licence, and the package manager owns that
installation: it updates it and takes it off, and `jason update apply` refuses it — it "would replace a file the package manager owns" — and
names the manager's command. That command is typed between `jason runtime stop` and `jason runtime start`, because Homebrew's
upgrade removes the previous version's keg by default, the path a running runtime starts its plugin hosts from.

### By hand

On Linux, to install without running a downloaded script, take these steps yourself. On macOS and Windows the install
is Jason Desktop, which the script installs and sets up; there is no route by hand. On Linux they install the command
line alone, from the archive a release still publishes, not Jason Desktop: its update to a later release, `jason update
apply`, moves it into Jason Desktop (§4), and Jason Desktop's own package is the route of *Jason Desktop* below.

1. Download the archive and `checksums.txt` beside it: for the latest release,
   `https://get.jasonharness.ai/releases/latest/download/jason-linux-x64.tar.gz` and
   `https://get.jasonharness.ai/releases/latest/download/checksums.txt`. For a
   particular release, use the address the pinned lines above use, with the file's name in place of the script's.
2. Check the archive's SHA-256 against its line in `checksums.txt` (`sha256sum`), and stop if they differ.
3. Unpack it into a new temporary directory, and move the executable into the install directory, `~/.local/bin`, with
   `LICENSE` and `THIRD-PARTY-NOTICES` beside it, named `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`.
4. Run `jason --version` there, by its full path. It prints the version you downloaded.
5. Put that directory on the PATH, as *Putting it on the PATH yourself* above says: that is the act it marks.

Once `jason --version` answers there, the archive, `checksums.txt` and the temporary directory you unpacked into
have done their work: you may delete those three — only those — with `rm -r <path>`.

### Jason Desktop

[Jason Desktop](desktop.md) is a window onto the installation, with its own chat. On macOS and Windows it is the
installation itself: one installation, Jason Desktop and the `jason` it carries, which the install script above
installs. Its installer is also one download from the latest release, `jason-desktop-windows-x64-setup.exe` for Windows
on x64 and `jason-desktop-macos-arm64.dmg` or `jason-desktop-macos-x64.dmg` for macOS, at
`https://get.jasonharness.ai/desktop/windows`, `/desktop/mac` and `/desktop/mac-intel`. Installed by hand from one of
them, it sets the machine up as the script does (§2), once it is in its place: on macOS, drag Jason into Applications, or
into the Applications folder in your home, and open it there. Opened from its disk image, or where it was downloaded,
which macOS runs from a temporary place of its own, it sets nothing up: nothing is placed, retired or stopped, and its
Command Line item says to move Jason first. From its place, the application's first start of each version runs the
`jason install` it carries, which puts `jason` in the terminal and Jason's skills for Claude Code and Codex, where each
is found, and its Command Line item says what that placed
([desktop.md](desktop.md#the-command-line-and-the-login-items)); the Windows installer runs it too, at every install
and update. The Windows and macOS packages are signed.

On Linux it is the installation too, which the install script above installs: the deb where there is apt, the
AppImage elsewhere. Each is also one download, `jason-desktop-linux-x64.deb` (`sudo apt install
./jason-desktop-linux-x64.deb`, the one to take on Ubuntu 24.04 and later) or `jason-desktop-linux-x64.AppImage`, at
`https://get.jasonharness.ai/desktop/linux-deb` and `/desktop/linux-appimage`, which send you to the latest release's
file. Installed by hand, the deb puts `jason` in every terminal as `/usr/bin/jason` and adds Jason's apt repository,
and the application's first start of each version runs the `jason install` it carries, as on macOS and Windows: for an
AppImage the wrapper `~/.local/bin/jason` and the PATH lines (§2), for the deb nothing more in the terminal, and Jason's
skills for Claude Code and Codex where each is found; its Command Line item says what that placed
([desktop.md](desktop.md#the-command-line-and-the-login-items)). The Linux packages are not signed.

On Linux, where an agent's harness refuses the install script (§1, *If an agent installs it*), the route by hand is
this one: download the file for the system, check its SHA-256 against the release's `checksums.txt` as *By hand* does,
and hand it to the person to install or open — the deb with `sudo apt install ./jason-desktop-linux-x64.deb`, which
asks for their password, the AppImage opened where they keep it. That ends the install: at a start the desktop starts a runtime when none is up, and deploys the role
skills and its chat's own skills from the package it carries ([desktop.md](desktop.md#what-it-does-at-a-start)), and its
first start puts Jason's skills where Claude Code and Codex read them; another host's come when the person asks:
`jason skills install --host <their host>`, run with the command its first start put in the terminal.

---

## 3. Build from source

This section is for people changing Jason; to use Jason, skip it: section 2 installs a release.

A build of the executable from the runtime's repository, rather than a release (§2), needs two things from this page:

- **The executable alone, in a directory of its own, on the PATH by hand** — as §2 says for `--install-dir` and
  `--no-modify-path`. Copy that file alone: the `.pdb` files beside it are debugging symbols, and a directory that
  holds them as well is not one of its own.
- **`--source` when you teach it** (§4). The skill packs are pinned to a tag derived from the build's own version,
  and a build from a working tree is a development version whose tag has not been published. `--source <checkout>`
  says "take the packs out of this checkout of this repository", which is what you want when you are standing in
  it.

---

## 4. Teach it

Jason's skills are what teach an agent the job — one directory per launched role, which the runtime reads at
every launch, and two packs for your own agent: one for operating the installation, one for the SDR
profession. **A runtime with no role skills launches every role untaught**, so the first half is not optional.

The install script did the first half as its last step (§2), and what it printed says where the role skills went.
Installed with a package manager or by hand, or where that step said it could not, do it now with the lines below;
they are also how it is done again.

Look first. Nothing is deployed, and the one target root is printed — the runtime's own, inside its data
directory:

```sh
jason skills install --roles-only --dry-run
```

On a first install the dry run fetches the skills first, from the release's package of them, into Jason's staging
directory inside the data directory, so that there is something to show; it deploys nothing. The line below deploys them: it prints
its one root before it writes a skill there, and writes nowhere else.

Then do it:

```sh
jason skills install --roles-only
```

Both take the skills from `jason-skills.zip`, the package of them the release this build was published as holds,
checked against that release's `checksums.txt` (§1). `--source` takes them from somewhere else instead: a checkout,
or a git repository at a ref, which needs `git`.

Then place the browser UI, the page `jason ui open` opens (this release's build draws its console in Jason Desktop and in a tab the desktop opens; any other tab opens on the demo, or, while something waits, shows where to get the desktop and what waits):

```sh
jason ui install
```

It downloads `jason-ui.zip` from the release this build was published as, checks it against that release's
`checksums.txt`, and places it in the data directory as the default UI. A runtime works without it. A build from
source has no published UI, so it refuses and says where a build of `ui/` goes instead: `ui/custom/` in the data
directory, which is served first whenever it holds one.

The role skills are everything a runtime needs, and none of these lines writes outside its data directory. The other two packs go into the
skills root of each agent host found on this machine (the [host matrix](execution-profiles.md#12-the-hosts) names
them) — which is yours to allow rather than Jason's to assume.

**Ask first.** Without `--roles-only` the same verb deploys all three, again printing every root before it writes
any:

```sh
jason skills install --dry-run
jason skills install
```

**From a working tree.**
A build from source knows no published tag. Its version names the release it is on its way to, so the repair
`jason status` prints for `role_skills` — `jason skills install --roles-only`, with no `--source` — asks for a
tag that has not been published yet, and refuses, saying so. Add `--source <checkout>`, the path of your checkout
of this repository, to each of the lines above.

It refuses a deployment the runtime would later refuse to launch, rather than writing one — a role whose
directory and skill name disagree, or a tree over the runtime's live size cap, stops the whole deployment
before a byte is written. A file you have since edited is reported and kept, not overwritten, and
everything else is written around it: the run names each file it kept and succeeds. The skill holding it is
written file by file where it stands, and is a blend of two hands, yours and the release's, which is yours to
reconcile. `--force` overwrites it,
and overwrites every edited file in every root of the plan, so read what it says first.

Later, to move an existing deployment to a newer set:

```sh
jason skills update
```

It updates only where a deployment was made, and each root from the source its own record names: an
installation taught with `--roles-only` stays that way, and a harness installed from one source is not updated
from another. A deployment made at the tag of the build that installed it moves to the tag this build carries. One
made with `--ref` stays at its ref, a directory is read where it stands, and a development build keeps the ref the
record names, because the tag its own version names is not published yet.

From 0.20.0, Jason's own installation on 0.18.x or earlier updates through 0.19.0: `jason update apply --version 0.19.0`,
then `jason update apply`. Its own `jason update check` names 0.19.0 as `install_first` and its apply as
`upgrade_command`, and a plain `jason update apply` refuses with `update_not_directly_applicable`, naming 0.19.0. A
package manager's upgrade goes straight to the newest release.

**A command-line install moves into Jason Desktop.** On macOS, Windows and Linux, a command-line install of an earlier version
moves into Jason Desktop through its own update: `jason update apply` updates it to this version, then installs Jason
Desktop, or takes one of 0.49.0 or later that is already there, and moves the command line into it, so that the
terminal's `jason` is the one the application carries. The update never fails because of the move. Where the move cannot
go on, the command-line install is left as it was and still answers as `jason`, `jason status` says where the move
stands in its `move` check, and where a command helps, the check names it: `jason update apply` typed again carries the
move on. On Windows the move first verifies this `jason`'s own signature; where that does not verify, it fetches nothing
and says to install Jason Desktop from the release page, whose first start takes the command-line install in. Once the
install is moved, `jason update rollback` does not take it back
([release-and-update.md](release-and-update.md), "Going back from 0.49"). On Linux the move takes the form the install
script takes: the deb, installed by apt with the one administrator's password, where there is apt, and the AppImage
placed elsewhere. It waits, saying why, where no password is given or there is no session to ask in, where the system
refuses user namespaces or has no setuid `fusermount`, while Jason Desktop runs, and beside an AppImage older than this
version or one whose version cannot be read.

`jason update apply` ends by running `jason skills update` at the new release's tag, and then places the release's
UI as `jason ui install` does; an update applied by a build before 0.12.0 needs one `jason skills update` after it,
and one applied by a build older than the UI step places no UI, so `jason ui install` follows it. On macOS and
Windows the `jason` Jason Desktop carries hands the update to the application's own updater, and the new version's
`jason` takes these steps when it finishes it ([release-and-update.md](release-and-update.md) §7); on Linux an
AppImage's does the same. The deb's updates come with the system's, from Jason's apt repository: Software Updater offers
them and installs them when you say so, as `sudo apt update && sudo apt upgrade` does, and Ubuntu's automatic updates
leave them alone; there `jason update apply` refuses and names apt, and Jason Desktop's next start, or `jason update apply --finish`, takes these steps. A package
manager's upgrade does not run `jason update apply`, so `jason skills update` and `jason ui install` follow it. Either way, `jason status` is the check: role skills a
default install took at another release's tag fail
`role_skills`, and the repair it prints moves them to the runtime's own tag. That repair is
`jason skills install --roles-only`, with the `--source` they came from when it was not this repository. A
deployment made with `--ref` stays where it is and passes, in the runtime's own root and in an agent's, and so does
one an older Jason's `jason skills update` touched, which recorded it as though it had been made with `--ref`. The
check's fact names both tags and the line that moves it: `jason skills install --roles-only` for the role skills,
and `jason skills install --pack <pack>` for a pack in an agent's root, each with the `--source` it came from when
that was not this repository. The line for `jason-runtime-skills` moves its role half as well.

[`skills/README.md`](../skills/README.md) is the contract both verbs keep, including the record each root
carries — which is what §7 removes by.

---

## 5. Ask whether it can work

```sh
jason status
jason status --human
```

One question — "can this installation start work?" — answered check by check. It is one of twelve names in
this CLI with no API operation behind it — `status`, `uninstall`, `install`, `logs`, `doctor`, `export` and `import`, the `skills` and
`update` nouns, `ui install` and `plugin install`, which change this installation's files as those two do, and
`session open`, which starts your own agent host, or a plain shell, in a terminal — and it cannot be
otherwise: half of what it reports is not the runtime's to know.

| Required — a failure exits 1 | Optional — absent never fails |
|---|---|
| the runtime answers, and no update stopped part-way from its swap on — its repair then is `jason update apply`, never `jason runtime start` | no update is waiting to be resumed |
| the running runtime is the build this CLI is | a provider plugin is installed |
| the runtime is not draining — a drain, by `jason runtime drain` or an update or install that ended part-way, lasts until `jason runtime resume`, its repair, or a restart of the runtime | each plugin package this release publishes is the one installed |
| migrations are applied | every plugin installed is available |
| the plugin registry is alive (zero plugins is alive) | a plugin is granted a journal kind |
| role skills are present for every role Jason ships — a role of your own is reported, never failed — named after their directories and within the runtime's live cap | every plugin is granted something of each capability it asks for |
| `jason` resolves on PATH — in this shell, or in every shell started from now on — and which file answers | a route is configured |
| the data directory is laid out as this jason runs it (`layout.json`; none means layout 1) — a layout a newer Jason set up, or a stamp that cannot be read, fails it with the sentence the refused verbs say | |
| | a binding names an account |
| | a provider CLI **you name** answers |
| | the skill packs are deployed to an agent harness, and the files their record names are there |
| | every enabled profile names an agent host detected on this machine |
| | a default execution profile is named for the AI roles to run on, and is enabled |
| | autostart is registered |
| | notifications reach you |
| | a UI is installed, and the default one is this release's |
| | the harness root can take the first project or campaign |

**It exits 0 or 1 and never 3.** Exit 3 means "I could not ask the runtime", and this is the verb whose whole
job is to answer when the runtime cannot be asked. The JSON body carries `ready` and a state per check, so
**an agent branches on the body, never on the exit code**.

Under its checks, `jason status --human` prints where this installation's parts are: the role skills, plugins and
custom UI folders, the settings file and the layout. Its JSON carries the same under `setup`, whether or not a runtime
answers. While `setup.settings_file_holds_secrets` is true, the settings file is also where plugin secrets are kept, and
`--human` names `jason route set --global` and `jason plugin grant` for its routes and grants. `setup.layout` is the
layout the data directory's `layout.json` names, 1 where there is none, and null where it cannot be read: `jason status`
never guesses one.

A check reports a fact and, where there is one, the command that repairs it. Most repairs are `jason`
commands. One is not: putting an executable on a PATH is the shell's own act, so the `path` check prints the
line your platform needs — a PowerShell line on Windows, an `sh` line elsewhere, the same line this product's
own installer writes — which you type rather than hand back to `jason`.

**Ask first.** That line writes a login profile — for `bash` and `zsh`, `~/.bashrc` or `~/.zshrc` beside it — or this
account's `Path` on Windows. Typed twice, it changes
nothing the second time. Read §2's note about login shells before you type it.

A shell that started before the installer put Jason on this account's PATH does not see it — and an agent's
commands usually run in exactly such a shell, because each one inherits the environment the agent itself
started with. That is an old shell, not a broken installation: when this account's PATH carries the
directory — its Path value on Windows, the one profile a login shell reads elsewhere — `path` is `ok`, says
that this shell predates it, and names the full path to type in the meantime. A shell started from now on
finds `jason` by name. **But everything the old shell started is as old as it is**: an agent host started
before the install, and every command it runs, keeps failing a bare `jason` while `ready` is true — type the
full path the check names, or restart the host.

One installation `path` fails with no line at all: a `jason` in a directory whose name holds the separator of PATH,
`;` on Windows and `:` elsewhere. No PATH entry can name that directory, since every reader splits it in two, so no
shell finds that `jason` by name and the agents Jason starts are not given it first on their PATH, whatever else
answers to `jason`; a line would only write the same split entry again. The check says which character, and the way
on is to install Jason into a directory whose name does not hold it. The installers refuse such a directory.

On macOS, Windows and Linux, where a machine holds one installation, `path` fails, naming both files, where `jason` by
name is another file than the `jason` answering — Jason Desktop's own wrapper at `~/.local/bin/jason`, and the deb's
`/usr/bin/jason`, count as the `jason` they run. Typed by name, `jason` runs that other file, so you and every agent use
it and not this one, and `ready` is false until that file is removed or its folder taken off the PATH. On macOS and
Windows that is also what a published build of this repository says, run by its full path while a release answers by
name. On Linux a `jason` Jason Desktop does not carry, such a build among them, passes the check there and says that two
installations are on this machine.

A fresh installation that has done §2 through §4 but has not yet started the runtime is expected to report
`runtime`, `runtime_version`, `dispatcher`, `migrations`, `plugin_registry` and `role_skills` as failed-or-unknown with
`jason runtime start` as the repair. That is §6.

`runtime_version` compares the version the running runtime reports with this CLI's own. It fails when a runtime
was left running across an upgrade that replaced the executable — a package manager's, or a file copied over by
hand — because that runtime is still the old build and starts its plugin hosts from the old image. Its repair is
`jason runtime restart`. Where the runtime is the newer of the two, the fact says so and the repair is
`jason update apply`, since a restart from this older `jason` would start the older build on the newer data, which a
Jason from 0.49.0 on refuses to start on (`data_newer`).

`dispatcher` fails while the runtime is draining: it answers every question and claims no new work. A drain is what
`jason runtime drain` does, and what an update or an install leaves where it ended part-way; it lasts until
`jason runtime resume`, the check's repair, or a restart of the runtime. For a draining runtime of an older build than
this `jason` the repair is `jason runtime restart` alone, as `runtime_version`'s is: where another build has replaced its
executable, a resume would start its plugin hosts from that file. While an update stands unfinished the drain
is the update's: the check says the runtime is draining for the update, and its repair is `jason update apply`, never a
resume in the middle of it. A dispatcher the settings turn off (`Dispatcher:Enabled`) is said there and never failed.

`plugin_package` compares each plugin package the release you run publishes — today the Reply plugin's, the journal
forwarder's, ntfy's, Slack's and Telegram's — with the package
of that id installed, by content digest, offline: the runtime carries its release's package and asks no network. An
earlier release's package fails it: a package manager's upgrade replaces the executable and leaves
`~/.jason/plugins/reply` as it was, and so does an update whose `plugins:` line says the package was not replaced. Its
fact names the installed version, the version this release published, and the command that places it,
`jason plugin install reply --replace`, which swaps the old package for the release's and asks the runtime to load it. A
package with the same version or a later one passes, and the fact says it is a later release's package or one built or
edited on this machine; no package of that id is absent. A development build compares with its own tree, and the fact
names the build it compared with.

`plugin_availability` names each plugin `jason plugin list` reports `unavailable`, with its problem codes and never
their text — `reply is unavailable; its problems: executable_missing.` Such a problem is about this machine rather than
the package, most often a program the plugin runs that is not on the PATH the runtime started with. Work routed to such
a plugin fails at the claim, and a notification plugin delivers nothing; nothing else stops, so the check never decides
`ready`. `jason plugin list` says what each problem is, and once it is repaired `jason plugin reload` reads the plugins
again.

`plugin_subscriptions` names each plugin the settings grant journal kinds to, with those kinds —
`forwarder handles workitem_failed, campaign_created.` — and is absent when no plugin is granted one. It quotes a
plugin's id and its kinds and nothing else, and it never decides `ready`: a kind no plugin handles is an ordinary
installation. `jason plugin list` says which kinds each plugin declares and which it is granted.

`plugin_grants` names each plugin, an unavailable one included, that asks for a capability and is granted nothing of
it, with the keys under `Plugins:Grants:<id>` that would grant it — `reply is granted nothing it asks for under Exec.`
It quotes a plugin's id and those keys and never what the manifest asks for, and it never decides `ready`: such a
plugin runs without what it is not granted, and nothing else stops. `jason plugin list --human` says what each asks
for and the `jason plugin grant` command that grants it, in force at once.

To check a provider's own CLI as well, name it — this build knows no vendor's command by heart:

```sh
jason status --provider-cli my-provider-cli
```

The check looks the name up on the PATH of the process that runs `jason status`, and its answer says so whenever it read
a PATH: *It read the CLI's PATH; the runtime's PATH may differ, see the install page.* The runtime has a PATH of its own, fixed when it
started, and a runtime started at logon or from another shell may not see a program your terminal does. That is the
case `plugin_availability` above names for a plugin's program; this check does not ask the runtime to look. A name
given as a path is looked up in its own directory and reads no PATH, so the answer says nothing of one, with one
exception: an npm shim given as a path takes its `node.exe` from the PATH when none stands beside it, and then the answer
says whose PATH that was.

Under Jason Desktop's chat the check is held tighter: the chat runs `jason` with `JASON_CHAT_TURN` set, which a Claude
Code turn cannot clear and a Codex turn in Full access can ([desktop](desktop.md)), and on a turn that can only read and
propose with `JASON_CONFINED` too, so the program is named by name and found on the search path, and a name with a directory in
it is neither looked up nor started. Run a
path from a terminal.

**When something does not work**, run `jason doctor --dry-run`, then `jason doctor`, and open an
[issue](https://github.com/reply-team/jason-ai/issues/new?template=problem.yml) with the file it wrote attached: the
address it prints fills in the version and the platform. Help with your own data, or a file made with
`--include-journal`, goes to the support address the README gives under Getting help, never to an issue; a
vulnerability goes the way [SECURITY.md](../SECURITY.md) says.

---

## 6. Start it, and keep it started

```sh
jason runtime start
jason runtime status
jason runtime stop
```

`start` launches the runtime in the background and prints the instance it ended up talking to; `stop` asks
that instance to shut down and returns only once it has really gone. Migrations apply automatically on start,
after a backup.

Nothing is registered until you ask.

**Not done at install.** An agent that installs Jason does not register this: it says at the end that the runtime can
start when the person logs on, gives the command, and runs it only on the person's word. To have the runtime start
when you log on:

```sh
jason runtime autostart enable
jason runtime autostart status
```

The registration carries the data directory it was made under, and nothing supervises it afterwards: a
runtime that stops stays stopped until somebody starts it. It needs no administrator rights anywhere, and it
is this account's alone: on a machine several people sign in to, each registers their own. On Windows the
runtime then runs in your own session when you sign in, shows no window on Windows 11 24H2 and later, and ends
when you sign out. On Windows that is a logon task named for this account; on macOS a LaunchAgent,
`ai.jason.runtime`; on Linux a `systemd --user` unit, `jason.service`. `jason runtime autostart status` says what
is registered.

An install, an update or Jason Desktop's set-up that stops the runtime leaves a note that a start is owed,
`<data>/run/start-owed.json`, and Jason Desktop's next start starts one from the `jason` it carries where it finds the
note and no runtime answering ([desktop.md](desktop.md), "The start that is owed"). The note goes in two ways: when a
runtime starts on that data directory again, and when you stop the runtime yourself with `jason runtime stop`, so a
stop of your own is never taken for one an install owes.

That is the installation. An agent that installed Jason tells you where it keeps your files and what it ran, then starts
the [`quick-start`](../skills/runtime/quick-start/SKILL.md) skill, which asks you one question and takes you to your
first campaign. [The wiring check](golden-path.md) — a page of its own, a synthetic walk to meet the system, your
provider connected and proven with your own address, and not the real work — is mentioned in the skill's last line and
taken only if you say so. The first campaign is the real work: you tell your agent about it in your own words — typed,
a link, a file or a voice note — and it becomes a campaign planned with the business skills,
[`campaign-planning`](../skills/business/campaign-planning/SKILL.md) first, and worked by Jason's roles.

---

## 7. Take it off again

```sh
jason uninstall --human --dry-run
```

Look first: it prints exactly what it would remove and changes nothing.

```sh
jason uninstall
```

On macOS, Windows and Linux this is the one uninstall: one installation, Jason Desktop and the `jason` it carries, is removed
once. The same verb takes off the application, what `jason install` placed for it — the terminal command and the
skills it recorded in agent hosts' folders — and an older command-line install, by the rules below and the steps
[release-and-update.md](release-and-update.md) §9 adds for the application. It is also reached from Installed apps in
Settings on Windows and from **Uninstall Jason…** in the Jason menu on macOS and Linux (*Jason Desktop*, below). Run from a
terminal while Jason Desktop is open, it refuses with `desktop_running` before anything is removed: quit Jason first.
On macOS, while Jason Desktop's updater installs an update, it refuses with `update_in_progress` before anything is
removed: wait for the update to end, then run it again. If the update never ends,
`launchctl print gui/$(id -u)/ai.jasonharness.desktop.ShipIt` shows the job and
`launchctl bootout gui/$(id -u)/ai.jasonharness.desktop.ShipIt` takes it out. An account with no login session,
reached over ssh or run by a management tool while it is not signed in at the screen, has no login domain and so no
such job.

**Skills go only by receipt.** Every root a deployment wrote into carries a record listing each path written
there, and this verb removes those skills and nothing else. A skill you copied into a harness by hand has no
receipt, so it is removed by nobody — including this. The rest of the installation goes by rule, in a fixed
order: this account's logon registration first — on Windows including the task `\Jason` an earlier version
registered for it — so a logon part-way through cannot start what is going (on macOS just after the runtime, and on
Linux for the deb just after its package, so a password not given leaves it in place); then the runtime, and if it will not
stop — or is running and does not answer — nothing after that is removed; then, on Windows, the notification
registration — the AppUserModelID key the toast was shown under, the two entries Windows made for it at the
first toast, and the `jason:` link where its command names this executable — after the runtime, so its next toast
cannot write them back; then the recorded skills; then the
PATH entry, only where the installer wrote it; then the executable, its two notice files, its install directory,
and the native libraries a release unpacks the first time it runs — under the temporary directory on Windows, under
`~/.net` on macOS and Linux. On Windows what every other build of Jason unpacked beside them goes too: the versions
this installation was before an update, and any other installation of this account, which nothing there tells
apart. A build unpacks again on its next start, and a copy running now holds its own, which stays and is named. On
macOS and Linux a running copy holds nothing, so what the other builds unpacked stays: the plan and the report count
those directories, name the directory they are in, and leave them for you to remove once nothing runs from them.
With `DOTNET_BUNDLE_EXTRACT_BASE_DIR` set they are not looked for at all. The notice files go by name: on macOS and
Linux it removes those two files by name from a directory other tools share, and a `LICENSE` there that is not
one of them is another tool's and stays.

"Only where the installer wrote it" is one rule on both platforms: the entry goes only where the installer wrote
it **and** its directory holds nothing but what the installer puts there, because other programs are on the PATH
through the same entry. On macOS and Linux the entry is the block the installer appends to a login profile, and to `~/.bashrc` or
`~/.zshrc` beside it — a marker line and the `export` line under it; a line nobody marked is yours and stays, and so does the block where
the directory holds more than `jason` and its two notice files: `~/.local/bin`, the installer's default, is where
other tools install too. On Windows the Path carries no marker, so the entry goes only where its directory holds
nothing but the executable, its two notice files (`jason-LICENSE`, `jason-THIRD-PARTY-NOTICES`) and the
`jason.previous.exe` an upgrade can leave beside it. An entry that stays is named in the
report, with the reason.

On Windows the executable is the file the uninstall is running from, and Windows does not delete the image of a
running process. So it is moved out of the install directory, to a directory on the same volume — which then
goes, before the verb returns — and a cleanup it starts removes the moved copy as soon as the verb has exited.
The report names both, and the line that removes the copy by hand should it still be there; where no cleanup
could be started, that copy is a problem and the verb exits 1.

Where Homebrew or winget installed Jason, the executable, its directory and the PATH entry that reaches it are the
package manager's: the dry run and the report list them as left to it, with the command that takes them off —
`brew uninstall --formula jason`, or `winget uninstall Reply.Jason` — and `jason uninstall` removes the rest as above,
the runtime stopped first. Where Homebrew's cask placed Jason Desktop, `jason uninstall` takes off what Jason set up and
leaves the application, its link and its state under `~/Library` to Homebrew: `brew uninstall --zap --cask jason` takes
them off.

**Three things are this account's rather than this installation's**: the logon registration, the notification
registration on Windows, and the agent harnesses. An uninstall of a second installation on the same account — a build from
source beside a release, a copy — takes the registrations and the harness skills the first one made as well. The
`jason:` link goes only where its command names this executable. Look at the dry run first. The notifications already in the notification center stay: they are yours to dismiss.

A recorded file whose bytes have changed since is reported and kept, because the record says what Jason
wrote and a mismatch says somebody else wrote it afterwards. `--force` removes it anyway:

```sh
jason uninstall --force
```

**The data directory is kept.** It holds the database, the settings, the plugins, the logs and the work
directories — the record of what you did — and the copies an update keeps — the executable it replaced and the
copy of the program kept for finishing an interrupted update, under `update/` — so a copy of the program stays there after an uninstall until
`--purge-data` removes it; and the verb says in one line that it kept it and where. To take it to another machine
instead, `jason export` writes it and the harness root into one archive (§8). To remove
that as well you have to say so: `--human` prints what will be deleted and asks before anything at all is
removed — and a no removes nothing at all — and when nobody is there to be asked the word is said in advance:

```sh
jason uninstall --purge-data --yes
```

It removes what Jason keeps in the data directory, and the directory itself once nothing else is in it: a file
of yours in there stays, and is named. And a data directory that is not one — a file system's root, a directory
holding your profile, the temporary directory or the installation, or a folder of the harness root, one inside it, or
one holding such a folder directly in it or anywhere in a folder the purge would delete, known by its
`.jason/marker.json`, or holding a folder there that cannot be looked into, which a stray `JASON_DATA_DIR` can name —
is refused before anything is removed.

Nothing else is touched: no skill without a receipt, no other skill in an agent harness, no provider CLI, no
model credential, no PATH entry the installer did not write, no other account's logon registration, and none of
the notifications already in the notification center. Nor is anything under the harness root (§2) — your memory
files and every project's and campaign's folder — whatever `--purge-data` says. A link in the data directory, a
symbolic link or a junction, is removed as itself, and what it points to is never touched. It exits 0
when everything it set out to remove is gone, and 1 when it did not get there — it refused (a runtime that would
not stop, a purge you did not confirm), something it set out to remove is still there (a record it could not
read, a file it could not remove, a moved copy nothing will remove), or it stopped part-way. Every step that did
happen is listed either way: in the report on stdout, or on stderr where the report itself could not be written.

### Jason Desktop

On macOS and Windows the application goes with the one uninstall above, from whichever door: `jason uninstall` in a
terminal, Installed apps in Settings on Windows, or **Uninstall Jason…** in the Jason menu on macOS, which asks first,
with a box for the data directory too, quits Jason and runs the same uninstall. The data directory stays unless you say
otherwise, with `--purge-data` or that box, and the harness root always stays.

On Linux the one uninstall is the same: Uninstall Jason… in the Jason menu, or `jason uninstall`. For the deb your
system asks for an administrator's password first, to take the package off; an AppImage goes with no password. The
data directory and the harness root stay, unless `--purge-data` says otherwise for the first.

[desktop.md](desktop.md) says what each takes off, what stays and what to do by hand when Jason will not start.

## 8. Moving the installation to another machine

Install Jason on the new machine first (§2), the same version or a newer one: an archive a newer Jason wrote is
refused, naming the version to install. Then stop the runtime on the old machine and write the archive:

```sh
jason runtime stop
jason export
```

`jason export` writes `jason-export-<stamp>.zip` in the folder you run it in, or the file you name, and never
over a file that is already there. `--stop` stops a running runtime for you and starts it again once the archive
is written; without it the verb refuses while the runtime runs. A link at the top of a carried place, `ui/custom/`
as a junction to a build of your own say, or a harness root that is one, is followed and carried as what it leads
to, unless that is inside the data directory or the harness root, or holds either; a link inside a carried place, a
name another system cannot hold, and two names apart only by the case of their letters are refused by their path.
The archive is written to `<file>.partial` first and moved into place once whole; a `.partial` an export left
behind is refused, as yours to remove. Copy the archive across, then on the new machine:

```sh
jason import jason-export-20261109T100005Z.zip
jason skills install
jason runtime autostart enable
jason status
```

**What travels:** the database, the settings file, the plugins, the role skills with their record, a custom UI
under `ui/custom/`, the logs, the layout stamp `layout.json` when there is one, and the harness root's tree — your memory files and every project's and campaign's
folder, each whole, a `.git` included — with `export.json` listing every file's size and SHA-256 and the layout the data directory is in. Whether the runtime started at logon travels as
a fact: the import registers it again on a machine that can, and on one that cannot says "No logon registration,
and this machine has no way to register one." **What does not:** the backups under `state/backups/`, the run and
work directories, staged skills, the shipped UI under `ui/default/`, the copies an update keeps, anything an
earlier import moved aside, the skills deployed into agent hosts (`jason skills install` deploys them again), a
temporary file a write of the settings file left beside it (`settings.json.<id>.tmp`, or `secrets.json.<id>.tmp` where the secrets are a file of their own, whatever its age; the runtime
removes one at its start once it is ten minutes old and no writer holds it, and an import does not place one an older
export carried), and anything outside the data directory and the harness root. **The archive carries the settings file with
everything in it, a token under `Plugins:Config` included: keep the archive as you keep that file.**

**Before anything is written**, the import refuses an archive it cannot read, a name in it that would land
outside the data directory or the harness root, a file whose size or SHA-256 is not the one `export.json` lists,
an archive a newer Jason wrote, an archive whose data directory is in a layout newer than this jason runs
(`layout_newer`), an archive whose layout stamp cannot be read or names another layout than `export.json` does, and an
installation already here — a database, or a harness root with a folder
marker in it, which a Jason that has run once on the new machine has. The refusal names what is there. Give
`--replace` to move it aside and place the archive instead: the data directory's parts go into
`<data>/replaced-<stamp>/` — `state/` whole, the database's `-wal` and `-shm` files and its backups with it — and
the harness root to `<root>.replaced-<stamp>`, nothing is deleted, and the answer names both as yours to remove.
On a machine with no installation the import still writes over nothing: the same parts that stand there, a
`jason.db-wal` or `jason.db-shm` beside no database among them, go aside the same way and are named, since SQLite
would read such a file as the placed database's. The harness root is `--root` when given, else the one this
machine's runtime would read (`JASON_Harness__Root`, else `Harness:Root` in this machine's settings file, else
`~/Jason`); a `--root` the variable contradicts is refused, naming the variable, and the root is held to the runtime's own rule for one
— an absolute path, not the home directory, a folder holding it or the top of a drive, nor the data directory, a
folder inside it or one holding it — before anything is placed.

**The settings file is made this machine's.** Every absolute path under the old machine's home directory, data
directory or harness root is rewritten to the same place here, `Harness:Root` first, and each rewrite is said; a
path elsewhere that is not on this machine is said and left as written, for you to correct. Every other value,
the secrets among them, is placed as it was. A role skills pack the record says came from a folder that is not
here is named, with `jason skills install` to deploy it again.

**Then the runtime starts on it**, and the import asks it to record each project folder where it now stands, as `jason
project link` does; a link the runtime refuses is named with its code in `link_refused`, the folder stays, the other
projects are still linked, and the answer's `next` carries `jason project link <id> --path <folder>`. An import on the
same machine, into a second data directory, needs the source harness root moved aside first: the records name its
folders, and the runtime refuses to link a folder that is present elsewhere (`harness_folder_present`). The import exits
0 when everything was placed, the runtime answered and every project folder was linked, and 1 when it refused (nothing
written), when the runtime did not start (the files are in place; start it with `jason runtime start`), or when a
project folder was not linked (everything is placed and the whole answer is still printed; `link_refused` names each
one, and `next` carries the line that links it), and 2 on a usage error. Three things are left to you, in this order:
`jason skills install`, `jason runtime autostart enable` if the runtime should start at logon here and the import did
not register it, and `jason status`. A turn-off made with `jason runtime autostart disable` does not travel: the archive
leaves `data/autostart/` out, and an import is a new machine, where the default holds.

---

## 9. What the quick start does

The install prompt ends by starting the `quick-start` skill. In five lines:

1. It asks you one question: what you sell and who should buy it. A line about yourself and how you write is welcome
   and never required.
2. It reads which provider your email and LinkedIn go through, and how many accounts are connected there; it does not
   ask you. The count never holds you up: when it cannot be read at once, the plan goes on without it.
3. It makes a draft of your first campaign and says the plan in one message: the goal, who it will look for, the channels, and
   how it will write.
4. It stops there, until you say what to change or say go. Until you choose otherwise, a first message is never sent without your yes; you may later say that sends to people who have not answered, or to people who have, go on their own.
5. Its text is [`skills/runtime/quick-start/SKILL.md`](../skills/runtime/quick-start/SKILL.md).
