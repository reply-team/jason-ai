# Installing Jason

This page takes a machine with nothing on it to an installation that can start work, and then takes it off
again. It is written to be followed by a person or by an agent, in order, without reading anything else first.

Everything here is per-user. Nothing needs administrator rights, nothing is installed for other people on the
machine, and the one exception — registering the runtime to start at logon on Windows — is called out where it
happens.

---

## 1. What you need

**From a release (§2):** a shell to install it with, and nothing else — the archive carries its own runtime.
Teaching it (§4) is a fetch of its own: a release takes its skills from this repository with `git`, over the
network, at the tag it was published as. A machine without `git` or without the network teaches it from a
checkout instead, with `--source`.

**From source (§3):** the .NET SDK version pinned in `global.json` at the repository root. `dotnet --version`
must satisfy it; the SDK resolves the pin itself and will tell you if it cannot.

Either way you also need a directory you can write to, and — if you want an agent harness taught — that
harness installed, so §4 has somewhere to deploy.

---

## 2. Install from a release

```sh
curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh
```

```powershell
irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1 | iex
```

Each script works out your platform, downloads the archive for it, **verifies its SHA-256 before unpacking**,
installs the executable under your own account, and puts that directory on your PATH.

| Default install directory | Platform |
|---|---|
| `~/.local/bin/jason` | macOS, Linux |
| `%LOCALAPPDATA%\Programs\jason\jason.exe` | Windows |

Three flags matter — spelled `--version`, `--install-dir` and `--no-modify-path` for `install.sh`, and
`-Version`, `-InstallDir` and `-NoModifyPath` for `install.ps1`:

- `--version X.Y.Z` installs a particular release instead of the latest. The script checks that the manifest
  it gets back is the release you asked for, rather than trusting the feed to have answered the question you
  put to it.
- `--install-dir <path>` installs somewhere else — into a directory of its own, for the reason §3 gives.
- `--no-modify-path` leaves your PATH alone. Then `jason` is only typeable by its full path until you put it
  on the PATH yourself — see §3, which has the same problem for the same reason.

A one-liner takes them like this:

```sh
curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh -s -- --version 0.1.0
```

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1))) -Version 0.1.0
```

Both lines install the **latest release**. A repository with no published release has no latest, so if they
answer 404 there is nothing to install from and §3 is the way in. That is a fact about the repository, not a
failure of the tool or of your machine, and it is worth saying that way to whoever asked you to install it.

Confirm what landed:

```sh
jason --version
```

On Windows the PowerShell you installed from is told about the new PATH entry. On macOS and Linux the script ran
in a shell of its own, so the one you typed it into has not heard: open a new terminal, or type it by its full
path — `~/.local/bin/jason --version`.

---

## 3. Build from source

From the repository root:

```sh
dotnet publish runtime/src/Jason.App -c Release -r <rid> --self-contained -p:PublishSingleFile=true
```

`<rid>` is your platform's runtime identifier — `win-x64`, `linux-x64`, `osx-arm64`. The single file it
produces is the whole product: the CLI, the runtime service and the plugin host are three modes of one
executable, chosen from its arguments. Copy it into **a directory of its own** — one that holds nothing else —
call it `jason` (`jason.exe` on Windows), and put that directory on your PATH.

A directory of its own because `jason uninstall` takes a directory's PATH entry off only where the directory
holds nothing but Jason. Copied into a directory other programs share — on Windows, the `WindowsApps` folder
every account's Path already names — the entry stays, and so does everything else it puts on the PATH.

**Putting it on the PATH yourself.** No installer ran, so nothing did this for you. On Windows, add its
directory to your account's `Path`; on macOS and Linux, append an `export` line to the one profile a login shell
of yours reads — `~/.zprofile` for `zsh`; for `bash` the first of `~/.bash_profile`, `~/.bash_login` and
`~/.profile` that exists, because bash never reads `~/.profile` once one of the others does; `~/.profile` for
every other shell. `jason status` prints the exact line for your platform — a PowerShell line on Windows, an `sh`
line elsewhere — when it finds that `jason` does not resolve (§5), so the simplest path is to run it and type
what it says.

One thing that line cannot do for you: a login profile is read by a **login** shell. A person opening a new
terminal gets it; an agent running one command at a time in a non-interactive child shell (`bash -c …`) has
written the profile but will not see it in its own next command. If you are an agent, carry the `export` half
of that line yourself, or call the executable by its full path until a new login shell exists.

A build from source is also why §4 below passes `--source .`: the skill packs are pinned to a tag derived
from the build's own version, and a build from a working tree is a development version whose tag has not been
published. `--source .` says "take the packs out of this checkout", which is what you want when you are
standing in it.

---

## 4. Teach it

Jason's skills are what teach an agent the job — one directory per launched role, which the runtime reads at
every launch, and two packs for your own agent: one for operating the installation, one for the SDR
profession. **A runtime with no role skills launches every role untaught**, so the first half is not optional.

Look first. Nothing is written, and the one target root is printed — the runtime's own, inside its data
directory:

```sh
jason skills install --roles-only --dry-run --source .
```

Then do it:

```sh
jason skills install --roles-only --source .
```

That is everything a runtime needs, and it writes nowhere else. The other two packs go into your agent's own
configuration — for Claude Code, `~/.claude/skills` — which is yours to allow rather than Jason's to assume.
Without `--roles-only` the same verb deploys all three, again printing every root before it writes any:

```sh
jason skills install --dry-run --source .
jason skills install --source .
```

From a release, leave `--source` off: the build knows the repository and tag it was published from, and fetches
the skills from there with `git` (§1).

A build from source knows no published tag. Its version names the release it is on its way to, so the repair
`jason status` prints for `role_skills` — `jason skills install --roles-only`, with no `--source` — asks for a
tag that has not been published yet, and refuses, saying so. Add `--source` and the path of your checkout.

It refuses a deployment the runtime would later refuse to launch, rather than writing one — a role whose
directory and skill name disagree, or a tree over the runtime's live size cap, stops the whole deployment
before a byte is written. A file you have since edited is reported and kept, not overwritten; `--force`
overwrites it, and overwrites every edited file in every root of the plan, so read what it says first.

Later, to move an existing deployment to a newer set:

```sh
jason skills update
```

It updates only where a deployment was made, and each root from the source and ref its own record names: an
installation taught with `--roles-only` stays that way, and a harness installed from one source is not updated
from another.

[`skills/README.md`](../skills/README.md) is the contract both verbs keep, including the record each root
carries — which is what §7 removes by.

---

## 5. Ask whether it can work

```sh
jason status
jason status --human
```

One question — "can this installation start work?" — answered check by check. It is one of four names in
this CLI with no API operation behind it — `status` and `uninstall`, and the `skills` and `update` nouns — and
it cannot be otherwise: half of what it reports is not the runtime's to know.

| Required — a failure exits 1 | Optional — absent never fails |
|---|---|
| the runtime answers | a provider plugin is installed |
| migrations are applied | a route is configured |
| the plugin registry is alive (zero plugins is alive) | a binding names an account |
| role skills are present, named after their directories and within the runtime's live cap | a provider CLI **you name** answers |
| `jason` resolves on PATH — in this shell, or in every shell started from now on — and which file answers | the skill packs are deployed to an agent harness |
| | autostart is registered |

**It exits 0 or 1 and never 3.** Exit 3 means "I could not ask the runtime", and this is the verb whose whole
job is to answer when the runtime cannot be asked. The JSON body carries `ready` and a state per check, so
**an agent branches on the body, never on the exit code**.

A check reports a fact and, where there is one, the command that repairs it. Most repairs are `jason`
commands. One is not: putting an executable on a PATH is the shell's own act, so the `path` check prints the
line your platform needs — a PowerShell line on Windows, an `sh` line elsewhere, the same line this product's
own installer writes — which you type rather than hand back to `jason`. Typed twice, it changes nothing the
second time. Read §3's note about login shells before you type it.

A shell that started before the installer put Jason on this account's PATH does not see it — and an agent's
commands usually run in exactly such a shell, because each one inherits the environment the agent itself
started with. That is an old shell, not a broken installation: when this account's PATH carries the
directory — its Path value on Windows, the one profile a login shell reads elsewhere — `path` is `ok`, says
that this shell predates it, and names the full path to type in the meantime. A shell started from now on
finds `jason` by name. **But everything the old shell started is as old as it is**: an agent host started
before the install, and every command it runs, keeps failing a bare `jason` while `ready` is true — type the
full path the check names, or restart the host.

A fresh installation that has done §2 through §4 but has not yet started the runtime is expected to report
`runtime`, `migrations`, `plugin_registry` and `role_skills` as failed-or-unknown with `jason runtime start`
as the repair. That is §6.

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

Nothing is registered until you ask. To have the runtime start when you log on:

```sh
jason runtime autostart enable
jason runtime autostart status
```

The registration carries the data directory it was made under, and nothing supervises it afterwards: a
runtime that stops stays stopped until somebody starts it. **On Windows, `enable` and `disable` must be run
from an elevated prompt**, and standard accounts are unsupported in this version.
[`docs/release-and-update.md`](release-and-update.md) §8 says what each platform registers and what only a
hand check can prove.

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
order: the logon registration first, so a logon part-way through cannot start what is going; then the runtime,
and if it will not stop — or is running and does not answer — nothing after that is removed; then the recorded
skills; then the PATH entry, only where the installer wrote it; then the executable, its install directory, and
the native libraries a release unpacks the first time it runs — under the temporary directory on Windows, under
`~/.net` on macOS and Linux.

"Only where the installer wrote it" is a rule of its own on each platform. On macOS and Linux it is the block the
installer appends to a login profile — a marker line and the `export` line under it; a line nobody marked is
yours and stays. On Windows the Path carries no marker, so the entry goes only where its directory holds nothing
but Jason — the executable, and the `jason.previous.exe` an upgrade can leave beside it; an entry for a directory
other programs share stays, and the report says why.

On Windows the executable is the file the uninstall is running from, and Windows does not delete the image of a
running process. So it is moved out of the install directory, to a directory on the same volume — which then
goes, before the verb returns — and a cleanup it starts removes the moved copy as soon as the verb has exited.
The report names both, and the line that removes the copy by hand should it still be there; where no cleanup
could be started, that copy is a problem and the verb exits 1.

**Two things are this account's rather than this installation's**: the logon registration and the agent
harnesses. An uninstall of a second installation on the same account — a build from source beside a release, a
copy — takes the registration and the harness skills the first one made as well. Look at the dry run first.

A recorded file whose bytes have changed since is reported and kept, because the record says what Jason
wrote and a mismatch says somebody else wrote it afterwards. `--force` removes it anyway:

```sh
jason uninstall --force
```

**The data directory is kept.** It holds the database, the settings, the plugins, the logs and the work
directories — the record of what you did — and the verb says in one line that it kept it and where. To remove
that as well you have to say so: `--human` prints what will be deleted and asks before anything at all is
removed — and a no removes nothing at all — and when nobody is there to be asked the word is said in advance:

```sh
jason uninstall --purge-data --yes
```

It removes what Jason keeps in the data directory, and the directory itself once nothing else is in it: a file
of yours in there stays, and is named. And a data directory that is not one — a file system's root, a directory
holding your profile, the temporary directory or the installation, which a stray `JASON_DATA_DIR` can name — is
refused before anything is removed.

Nothing else is touched: no skill without a receipt, no other skill in an agent harness, no provider CLI, no
model credential, no PATH entry the installer did not write. It exits 0 when everything it set out to remove is
gone, and 1 when it did not get there — it refused (a runtime that would not stop, a purge you did not confirm),
something it set out to remove is still there (a record it could not read, a file it could not remove, a moved
copy nothing will remove), or it stopped part-way. Every step that did happen is listed either way: in the
report on stdout, or on stderr where the report itself could not be written.
