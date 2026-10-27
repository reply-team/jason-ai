# Installing Jason

This page takes a machine with nothing on it to an installation that can start work, and then takes it off
again. It is written to be followed by a person or by an agent, in order, without reading anything else first.

Everything here is per-user. Nothing needs administrator rights, and nothing is installed for other people on
the machine.

---

## 1. What you need

**From a release (§2):** a shell to install it with, and nothing else — the archive carries its own runtime.
Teaching it (§4) is a fetch of its own: a release takes its skills from this repository with `git`, over the
network, at the tag it was published as. A machine without `git` or without the network teaches it from a
checkout instead, with `--source`.

You also need a directory you can write to, and — if you want an agent harness taught — that
harness installed, so §4 has somewhere to deploy.

### If an agent installs it

An act this page marks **Ask first.** needs the word of the person you install for: ask, and do it only on their
yes. With nobody to ask, leave it undone and say so. A question tool that comes back with no answer is nobody to
ask.

- Start your session without external connectors — on Claude Code, `--strict-mcp-config`. The install needs none.
- Do §2, then §4 to §6, in order, and stop there. §3 is for a build from the runtime's repository, and
  §7 takes the installation off again.
- Install by the first route that fits. Where §2 names a package for this platform and its package manager is
  already on this machine, use it (*With a package manager*). Where it is not, where it fails, or where it or your
  harness refuses it, say so once and take §2 *By hand*: download the archive and `checksums.txt` from the release,
  check the digest and stop on a mismatch, unpack, keep the two notice files beside the executable, and run
  `jason --version` by its full path. That is the next route, not a way around the refusal. Never pipe a download
  into a shell. The install scripts are the person's: where your harness refuses even the route by hand, say so and
  give the person the one-liner to type themselves.
- Write only what those steps write: the executable and its install directory; this account's PATH entry for it;
  Jason's data directory, which is where the role skills go; the system's temporary directory, which the download
  goes into; and the directory the executable unpacks its native libraries into when it first runs — under
  the temporary directory on Windows, under `~/.net` in this home directory on macOS and Linux. Nothing in your
  own agent's configuration unless the person says so (§4). A package manager's own PATH change is part of the
  install you were asked for, and so is the one-liner's when the person types it; a PATH edit of your own, by hand
  (§2) or by the repair (§5), is marked **Ask first.**
- With a no to the PATH entry, it is the person's own step: finish everything else, then hand them the exact line
  `jason status` prints for `path`, and say that `jason status` again proves it. Never end on "not ready" alone:
  name the step that remains.
- Install nothing else and build nothing (§3). A 404 from a download is §2's:
  stop and report exactly that. Leave no long-running process behind except the runtime that
  `jason runtime start` starts.
- A refusal — by your harness, or by a package manager — is reported once, as it stands, and never worked around.
  Working around it is a retry, the same command rewrapped, or the refused code run by another means, such as a
  downloaded script run another way. Taking the next route in the order above is not working around it.
- After `jason status` (§5), branch on `ready` in its JSON body, never on the exit code. For each required check
  that is not ok, run the repair it prints. Optional checks are reported, not repaired: their repairs write into
  your agent's configuration or into what starts at logon, which is the person's to allow.
- A profile you append to is read by a login shell, not by your next non-interactive command (§2): carry the
  `export` half yourself, or call `jason` by its full path.
- Stop when `ready` is true, or when a required check still fails after its repair. Report which checks failed,
  exactly what you ran, and what it changed. When `ready` is true, tell the person Jason is installed and can work
  and where it will keep their files — the two lines §2 shows, with the folder the `harness_root` check of
  `jason status` names, which is the one in force — and ask them once what comes next:
  [the wiring check](golden-path.md) first, in this session, or their real work at once. Name the wiring check for
  what it is: a synthetic walk to meet the system, which connects their provider and proves it with their own
  address, and is not the real work. A skip is honoured: do not offer the walk again. The real work, at once or after
  the walk, begins with an invitation to tell you about their first campaign in their own words — typed, a link, a
  file or a voice note, or one of a few examples you offer to start from — and goes on as a campaign planned with the
  `campaign-planning` skill and worked by Jason's roles.

---

## 2. Install from a release

```sh
curl -fsSL https://jasonharness.ai/releases/latest/download/install.sh | sh
```

```powershell
irm https://jasonharness.ai/releases/latest/download/install.ps1 | iex
```

By installing Jason you accept the Jason Runtime Licence: [LICENSE-RUNTIME.md](../LICENSE-RUNTIME.md). If you do
not accept it, do not install Jason.

Both lines install the **latest release**. If either answers 404, the release could not be reached — through the
address or on GitHub — and that is not a failure of your machine: report it as it stands rather than run the line
again, and build nothing instead.

Each script works out your platform, downloads the archive for it, **verifies its SHA-256 before unpacking**,
installs the executable under your own account — with its licence and the third-party notices beside it, as
`jason-LICENSE` and `jason-THIRD-PARTY-NOTICES` — and puts that directory on your PATH. Run again over the version
already installed, it downloads nothing and says so, unless a licence file is missing beside the executable: then
it downloads the archive, checks it the same way, and writes the file, leaving the executable as it is.

On macOS and Linux the PATH line goes into the profile a login shell of yours reads, and for `bash` and `zsh` into
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
`AGENTS.md` for your own words, and one line, `@AGENTS.md`, in the file Claude Code reads (`~/Jason/CLAUDE.md` at the
root), so that it reads them too; a `Jason` folder already there is taken as the root then, its marker written and
nothing it holds changed, unless it is marked as something else, which `jason status` reports. `Harness:Root` set
before that first create is all it takes. A folder you move later is recorded
again with `jason project link`, the root's once `Harness:Root` names where it is now. Jason never writes a word of
those memory files, never runs `git` there and never writes a `.gitignore`. `git init` in the root is yours to run:
it gives the folder a history, and lets Codex — which reads the `AGENTS.md` files from the top of a repository down
to the folder it starts in — read the root's and the project's with a campaign's.

On Windows, `install.ps1` waits up to 10 seconds for a file another program holds open — a virus scanner reading
the executable it has just unpacked or run — before it gives up and names the file, or the directory it was unpacking into. And if the new `jason.exe`
cannot move in, it puts the one you had back — or, when it cannot, says where it is — so an install that fails never
leaves you without the version you started with.

| Default install directory | Platform |
|---|---|
| `~/.local/bin/jason` | macOS, Linux |
| `%LOCALAPPDATA%\Programs\jason\jason.exe` | Windows |

Three flags matter — spelled `--version`, `--install-dir` and `--no-modify-path` for `install.sh`, and
`-Version`, `-InstallDir` and `-NoModifyPath` for `install.ps1`:

- `--version X.Y.Z` installs a particular release instead of the latest. The script checks that the manifest
  it gets back is the release you asked for, rather than trusting the feed to have answered the question you
  put to it.
- `--install-dir <path>` installs somewhere else — into a directory of its own, for the reason below.
- `--no-modify-path` leaves your PATH alone. Then `jason` is only typeable by its full path until you put it
  on the PATH yourself, as below.

A one-liner takes them like this:

```sh
curl -fsSL https://jasonharness.ai/releases/download/v0.21.0/install.sh | sh -s -- --version 0.21.0
```

```powershell
& ([scriptblock]::Create((irm https://jasonharness.ai/releases/download/v0.21.0/install.ps1))) -Version 0.21.0
```

These install 0.21.0 with 0.21.0's own script: a pinned install fetches the script from the release it installs.

**A directory of its own.** `jason uninstall` takes a directory's PATH entry off only where the directory holds
nothing but Jason. Installed into a directory other programs share — on Windows, the `WindowsApps` folder every
account's Path already names — the entry stays, and so does everything else it puts on the PATH.

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

On Windows the PowerShell you installed from is told about the new PATH entry. On macOS and Linux the script ran
in a shell of its own, so the one you typed it into has not heard: open a new terminal — or sign out and back in, where
the script said so — or type it by its full path — `~/.local/bin/jason --version`.

A started runtime asks the release feed whether a newer version exists five minutes after it starts, and once a
day after that, at the default settings: one `GET`, carrying nothing about you or your work — it identifies only
the runtime's version, including its build. `Update:CheckEnabled` set to `false` in
`~/.jason/config/settings.json` turns that off. That request, and the downloads the install scripts,
`jason update apply` and `jason ui install` make, go to `jasonharness.ai`, an address Reply.io operates, which redirects them to this
repository's releases on GitHub. What that address keeps, and for how long, is section 7.2 of the
[licence](../LICENSE-RUNTIME.md).

### With a package manager

On macOS, with Homebrew: `brew install reply-team/tap/jason`.

Every macOS route on this page — the one-liner, Homebrew and the archive by hand — is for Apple silicon only: no
build for an Intel Mac is published, so on an Intel Mac Homebrew answers "formula requires at least a URL",
`install.sh` names the platforms it has, and the way to Jason today is another machine: Linux x64, Windows x64 or an
Apple silicon Mac.

Installing this way accepts the same licence. The package manager owns that installation: it updates it and takes it
off, and `jason update apply` refuses it — it "would replace a file the package manager owns" — and names the
manager's command. That command is typed between `jason runtime stop` and `jason runtime start`, because Homebrew's
upgrade removes the previous version's keg by default, the path a running runtime starts its plugin hosts from.

### By hand

To install without running a downloaded script, take the steps the script takes, yourself:

1. Download the archive for this machine and `checksums.txt` beside it: for the latest release,
   `https://jasonharness.ai/releases/latest/download/jason-win-x64.zip`, `…/jason-linux-x64.tar.gz` or
   `…/jason-osx-arm64.tar.gz`, and `https://jasonharness.ai/releases/latest/download/checksums.txt`. For a
   particular release, use the address the pinned lines above use, with the file's name in place of the script's.
2. Check the archive's SHA-256 against its line in `checksums.txt` (`Get-FileHash -Algorithm SHA256` on Windows,
   `sha256sum` or `shasum -a 256` elsewhere), and stop if they differ.
3. Unpack it into a new temporary directory, and move the executable into the install directory —
   `%LOCALAPPDATA%\Programs\jason` on Windows, `~/.local/bin` elsewhere — with `LICENSE` and `THIRD-PARTY-NOTICES`
   beside it, named `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`. On Windows a downloaded archive may carry the
   mark of the web, and hands it to what it unpacks: `Unblock-File` the executable, as `install.ps1` does.
4. Run `jason --version` there, by its full path. It prints the version you downloaded.
5. Put that directory on the PATH, as *Putting it on the PATH yourself* above says: that is the act it marks.

Once `jason --version` answers there, the archive, `checksums.txt` and the temporary directory you unpacked into
have done their work: you may delete those three — only those — with `Remove-Item -Recurse <path>` on Windows and
`rm -r <path>` elsewhere.

---

## 3. Build from source

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

Look first. Nothing is deployed, and the one target root is printed — the runtime's own, inside its data
directory:

```sh
jason skills install --roles-only --dry-run
```

On a first install the dry run fetches the skills first, with `git`, into Jason's staging directory inside the
data directory, so that there is something to show; it deploys nothing. The line below deploys them: it prints
its one root before it writes a skill there, and writes nowhere else.

Then do it:

```sh
jason skills install --roles-only
```

Both fetch the skills with `git` from this repository, at the tag this build was published as (§1).

Then place the browser UI, the page `jason ui open` opens:

```sh
jason ui install
```

It downloads `jason-ui.zip` from the release this build was published as, checks it against that release's
`checksums.txt`, and places it in the data directory as the default UI. A runtime works without it. A build from
source has no published UI, so it refuses and says where a build of `ui/` goes instead: `ui/custom/` in the data
directory, which is served first whenever it holds one.

The role skills are everything a runtime needs, and none of these lines writes outside its data directory. The other two packs go into your agent's own
configuration — for Claude Code, `~/.claude/skills` — which is yours to allow rather than Jason's to assume.

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

`jason update apply` ends by running `jason skills update` at the new release's tag, and then places the release's
UI as `jason ui install` does; an update applied by a build before 0.12.0 needs one `jason skills update` after it,
and one applied by a build older than the UI step places no UI, so `jason ui install` follows it. A package
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

One question — "can this installation start work?" — answered check by check. It is one of eight names in
this CLI with no API operation behind it — `status`, `uninstall`, `logs` and `doctor`, the `skills` and
`update` nouns, and `ui install` and `plugin install`, which change this installation's files as those two do — and it cannot be
otherwise: half of what it reports is not the runtime's to know.

| Required — a failure exits 1 | Optional — absent never fails |
|---|---|
| the runtime answers, and no update stopped part-way from its swap on — its repair then is `jason update apply`, never `jason runtime start` | no update is waiting to be resumed |
| the running runtime is the build this CLI is | a provider plugin is installed |
| migrations are applied | each plugin package this release publishes is the one installed |
| the plugin registry is alive (zero plugins is alive) | every plugin installed is available |
| role skills are present for every role Jason ships — a role of your own is reported, never failed — named after their directories and within the runtime's live cap | a route is configured |
| `jason` resolves on PATH — in this shell, or in every shell started from now on — and which file answers | a binding names an account |
| | a provider CLI **you name** answers |
| | the skill packs are deployed to an agent harness, and the files their record names are there |
| | autostart is registered |
| | notifications reach you |
| | a UI is installed, and the default one is this release's |
| | the harness root can take the first project or campaign |

**It exits 0 or 1 and never 3.** Exit 3 means "I could not ask the runtime", and this is the verb whose whole
job is to answer when the runtime cannot be asked. The JSON body carries `ready` and a state per check, so
**an agent branches on the body, never on the exit code**.

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

A fresh installation that has done §2 through §4 but has not yet started the runtime is expected to report
`runtime`, `runtime_version`, `migrations`, `plugin_registry` and `role_skills` as failed-or-unknown with
`jason runtime start` as the repair. That is §6.

`runtime_version` compares the version the running runtime reports with this CLI's own. It fails when a runtime
was left running across an upgrade that replaced the executable — a package manager's, or a file copied over by
hand — because that runtime is still the old build and starts its plugin hosts from the old image. Its repair is
`jason runtime restart`.

`plugin_package` compares each plugin package the release you run publishes — today the Reply plugin — with the package
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

To check a provider's own CLI as well, name it — this build knows no vendor's command by heart:

```sh
jason status --provider-cli my-provider-cli
```

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

**Ask first.** To have the runtime start when you log on:

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

That is the installation. What comes next is yours to choose, and an agent that installed Jason asks you once:
[the wiring check](golden-path.md) — a page of its own, a synthetic walk to meet the system, your provider connected
and proven with your own address, and not the real work — or the real work at once. Skip the walk and it is not
offered again. The real work, at once or after the walk, begins with you telling your agent about your first campaign
in your own words — typed, a link, a file or a voice note, or an example to start from — and becomes a campaign
planned with the business skills, [`campaign-planning`](../skills/business/campaign-planning/SKILL.md) first, and
worked by Jason's roles.

---

## 7. Take it off again

```sh
jason uninstall --human --dry-run
```

Look first: it prints exactly what it would remove and changes nothing.

```sh
jason uninstall
```

**Skills go only by receipt.** Every root a deployment wrote into carries a record listing each path written
there, and this verb removes those skills and nothing else. A skill you copied into a harness by hand has no
receipt, so it is removed by nobody — including this. The rest of the installation goes by rule, in a fixed
order: this account's logon registration first — on Windows including the task `\Jason` an earlier version
registered for it — so a logon part-way through cannot start what is going; then the runtime, and if it will not
stop — or is running and does not answer — nothing after that is removed; then, on Windows, the notification
registration — the AppUserModelID key the toast was shown under, and the two entries Windows made for it at the
first toast — after the runtime, so its next toast cannot write them back; then the recorded skills; then the
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
`brew uninstall jason`, or `winget uninstall Reply.Jason` — and `jason uninstall` removes the rest as above, the
runtime stopped first.

**Three things are this account's rather than this installation's**: the logon registration, the notification
registration on Windows, and the agent harnesses. An uninstall of a second installation on the same account — a build from
source beside a release, a copy — takes the registrations and the harness skills the first one made as well. Look
at the dry run first. The notifications already in the notification center stay: they are yours to dismiss.

A recorded file whose bytes have changed since is reported and kept, because the record says what Jason
wrote and a mismatch says somebody else wrote it afterwards. `--force` removes it anyway:

```sh
jason uninstall --force
```

**The data directory is kept.** It holds the database, the settings, the plugins, the logs and the work
directories — the record of what you did — and the copies an update keeps — the executable it replaced and the
copy of the program kept for finishing an interrupted update, under `update/` — so a copy of the program stays there after an uninstall until
`--purge-data` removes it; and the verb says in one line that it kept it and where. To remove
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
