# Installing Jason

This page takes a machine with nothing on it to an installation that can start work, and then takes it off
again. It is written to be followed by a person or by an agent, in order, without reading anything else first.

Everything here is per-user. Nothing needs administrator rights, nothing is installed for other people on the
machine, and the one exception — registering the runtime to start at logon on Windows — is called out where it
happens.

---

## 1. What you need

**From a release (§2):** a shell, and nothing else. The archive carries its own runtime.

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

Three flags matter:

- `--version X.Y.Z` installs a particular release instead of the latest. The script checks that the manifest
  it gets back is the release you asked for, rather than trusting the feed to have answered the question you
  put to it.
- `--install-dir <path>` installs somewhere else.
- `--no-modify-path` leaves your PATH alone. Then `jason` is only typeable by its full path until you put it
  on the PATH yourself — see §3, which has the same problem for the same reason.

Both lines install the **latest release**. A repository with no published release has no latest, so if they
answer 404 there is nothing to install from and §3 is the way in. That is a fact about the repository, not a
failure of the tool or of your machine, and it is worth saying that way to whoever asked you to install it.

Confirm what landed:

```sh
jason --version
```

---

## 3. Build from source

From the repository root:

```sh
dotnet publish runtime/src/Jason.App -c Release -r <rid> --self-contained -p:PublishSingleFile=true
```

`<rid>` is your platform's runtime identifier — `win-x64`, `linux-x64`, `osx-arm64`. The single file it
produces is the whole product: the CLI, the runtime service and the plugin host are three modes of one
executable, chosen from its arguments. Copy it somewhere on your PATH and call it `jason`.

**Putting it on the PATH yourself.** No installer ran, so nothing did this for you. On Windows, add its
directory to your account's `Path`; on macOS and Linux, append an `export` line to the profile your shell
reads when it logs in — `~/.profile` for `sh` and `bash`, `~/.zprofile` for `zsh`, which does not read
`~/.profile` at all. `jason status` prints the exact line for your platform when it finds that `jason` does
not resolve (§5), so the simplest path is to run it and type what it says.

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

Jason's skills are what teach an agent the job — one pack for operating the installation, one for the SDR
profession, and one directory per launched role that the runtime reads at every launch. **A runtime with no
role skills launches every role untaught**, so this step is not optional.

Look first. Nothing is written, and the target roots are printed:

```sh
jason skills install --dry-run --source .
```

Then do it:

```sh
jason skills install --source .
```

From a release, leave `--source` off: the build knows the repository and tag it was published from.

It refuses a deployment the runtime would later refuse to launch, rather than writing one — a role whose
directory and skill name disagree, or a tree over the runtime's live size cap, stops the whole deployment
before a byte is written. A file you have since edited is reported and kept, not overwritten; `--force`
overwrites it, and overwrites every edited file in every root of the plan, so read what it says first.

Later, to move an existing deployment to a newer set:

```sh
jason skills update
```

[`skills/README.md`](../skills/README.md) is the contract both verbs keep, including the record each root
carries — which is what §7 removes by.

---

## 5. Ask whether it can work

```sh
jason status
jason status --human
```

One question — "can this installation start work?" — answered check by check. It is one of the two commands
in this CLI that no API operation stands behind, and it cannot be otherwise: half of what it reports is not
the runtime's to know.

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
line your platform needs — the same line this product's own installer writes — which you type rather than
hand back to `jason`. Typed twice, it changes nothing the second time. Read §3's note about login shells
before you type it.

A shell that started before the installer put Jason on this account's PATH does not see it — and an agent's
commands usually run in exactly such a shell, because each one inherits the environment the agent itself
started with. That is an old shell, not a broken installation: when this account's PATH carries the
directory, `path` is `ok`, says that this shell predates it, and names the full path to type in the meantime.
A shell started from now on finds `jason` by name.

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

**It removes only what a receipt names.** Every root a deployment wrote into carries a record listing each
path written there, and this verb removes those and nothing else. A skill you copied into a harness by hand
has no receipt, so it is removed by nobody — including this. The order is fixed: the logon registration
first, so a logon part-way through cannot start what is going; then the runtime, and if it will not stop,
nothing after that is removed; then the recorded skills; then the PATH entry, only where this installer wrote
it; then the executable and its install directory.

A recorded file whose bytes have changed since is reported and kept, because the record says what Jason
wrote and a mismatch says somebody else wrote it afterwards. `--force` removes it anyway:

```sh
jason uninstall --force
```

**The data directory is kept.** It holds the database, the settings, the plugins, the logs and the work
directories — the record of what you did — and the verb says in one line that it kept it and where. To remove
that as well you have to say so, in advance when nobody is there to be asked:

```sh
jason uninstall --purge-data --yes
```

Nothing else is touched: no other skill in an agent harness, no provider CLI, no model credential, nothing
outside the paths the receipts name. It exits 0 when everything it set out to remove is gone, and 1 when it
understood and refused — a runtime that would not stop, a record it could not read, a file it could not
remove — with the reason on stderr and the whole report on stdout.
