<!--
The body of every release. release.yml fills in {{version}} and hands the result to `gh release create`; edit
this file rather than the release page, so that the next release says the same things. What one release says
of itself is RELEASE-NOTES.md at the root, which release.yml puts where {{notes}} stands, without its comments
and its first line; a draft made without it carries this file alone.

The button (workflow_dispatch) creates a draft, the button's draft. A draft's tag
does not exist until the draft is published: deleting the draft leaves nothing behind, and publishing it is what
creates the tag. So the button's draft is deleted after inspection, unless it is meant as the release. With
dry_run the button makes no draft at all.
-->

Jason {{version}}: the runtime, the CLI and the plugin host, one self-contained executable per platform, built
at this version and released only after its tests passed.

{{notes}}

## Assets

| File | For |
| --- | --- |
| `jason-win-x64.zip` | Windows on x64 |
| `jason-linux-x64.tar.gz` | Linux on x64 |
| `jason-osx-arm64.tar.gz` | macOS on Apple silicon |
| `manifest.json` | what a running Jason reads to learn that this release exists |
| `checksums.txt` | the SHA-256 of each archive and of each package, in `sha256sum` format |
| `install.sh` | macOS and Linux: the install script this release was published with |
| `install.ps1` | Windows: the install script this release was published with |
| `jason-plugin-reply.zip` | the Reply plugin's package: the files of `plugins/reply` at `v{{version}}`, which `jason plugin install reply` places in `~/.jason/plugins/reply` |
| `jason-plugin-journal-forwarder.zip` | the journal forwarder's package: the files of `plugins/journal-forwarder` at `v{{version}}`, which `jason plugin install journal-forwarder` places in `~/.jason/plugins/journal-forwarder` |
| `jason-plugin-ntfy.zip` | the ntfy plugin's package: the files of `plugins/ntfy` at `v{{version}}`, which `jason plugin install ntfy` places in `~/.jason/plugins/ntfy` |
| `jason-plugin-slack.zip` | the Slack plugin's package: the files of `plugins/slack` at `v{{version}}`, which `jason plugin install slack` places in `~/.jason/plugins/slack` |
| `jason-plugin-telegram.zip` | the Telegram plugin's package: the files of `plugins/telegram` at `v{{version}}`, which `jason plugin install telegram` places in `~/.jason/plugins/telegram` |
| `jason-ui.zip` | the browser UI's package: `ui/` built at `v{{version}}`, which `jason ui install` places as the default UI |
| `jason-skills.zip` | the skills' package: `skills/` at `v{{version}}` and a `source.json` naming the tag and its commit, which `jason skills install` deploys when it is given no `--source` |
| `jason-desktop-windows-x64-setup.exe` | Jason Desktop for Windows on x64: the installer, signed |
| `jason-desktop-windows-x64-setup.exe.blockmap` | what the Windows desktop's updater reads to download only the parts of the installer that changed |
| `latest.yml` | the Windows desktop's update feed: this version, the installer's name and its SHA-512 |
| `jason-desktop-macos-arm64.dmg` | Jason Desktop for macOS on Apple silicon, to drag into Applications: signed, notarized and stapled |
| `jason-desktop-macos-arm64.zip` | the same application for Apple silicon, as the Mac desktop's updater downloads it |
| `jason-desktop-macos-arm64.zip.blockmap` | what the Mac desktop's updater reads to download only the parts of the Apple silicon zip that changed |
| `jason-desktop-macos-x64.dmg` | Jason Desktop for macOS on Intel, to drag into Applications: signed, notarized and stapled |
| `jason-desktop-macos-x64.zip` | the same application for Intel, as the Mac desktop's updater downloads it |
| `jason-desktop-macos-x64.zip.blockmap` | what the Mac desktop's updater reads to download only the parts of the Intel zip that changed |
| `latest-mac.yml` | the Mac desktop's update feed: this version, each zip's name and its SHA-512 |
| `jason-desktop-linux-x64.deb` | Jason Desktop for Linux on x64, as a package for Debian and Ubuntu: `sudo apt install ./jason-desktop-linux-x64.deb` |
| `jason-desktop-linux-x64.AppImage` | the same application for Linux on x64 as one file, to make executable and run from wherever you keep it |
| `latest-linux.yml` | the Linux desktop's update feed: this version, the deb's and the AppImage's names and their SHA-512 |

Each archive holds the executable, `LICENSE` (the MIT licence, which covers the whole repository, the runtime
included) and `THIRD-PARTY-NOTICES`, nothing else. The Reply plugin's package is installed by
`jason plugin install reply`, which places it in `~/.jason/plugins/reply` once it matches its line in
`checksums.txt` and the manifest's content digest; step 3 of `docs/golden-path.md` runs it. The journal forwarder's
package is installed the same way, by `jason plugin install journal-forwarder`, into
`~/.jason/plugins/journal-forwarder`; its README says how a receiver on another host takes a grant and a configuration
and no edit of the package. The packages of ntfy, Slack and Telegram are installed the same way, each by
`jason plugin install <id>`, into `~/.jason/plugins/<id>`; each one's README says what it takes to name it as a
notification channel. The UI's package is placed by `jason ui install`, which the install guide runs and
`jason update apply` runs at its end.
On Linux the executable needs the ICU library (`libicu`), which desktop distributions carry and minimal
images may not; the install script stops, with the runtime's own message, rather than install a program that does
not start.

## Desktop

Jason Desktop is a window onto this same Jason, in five packages: Windows on x64, macOS on Apple silicon, macOS on
Intel, and Linux on x64 as a deb and as an AppImage. Each carries this release's executable, signed as the archive's
is, with `LICENSE` and `THIRD-PARTY-NOTICES` beside it, and talks to it as the CLI does. On Windows, run
`jason-desktop-windows-x64-setup.exe`; the installer, its uninstaller, `Jason.exe` and the `jason.exe` it carries are
each signed with Reply's certificate and timestamped. On macOS, open the dmg for your Mac and drag Jason into
Applications; the application, the `jason` it carries and the dmg are signed with Reply's Developer ID and notarized,
and the application and the dmg are stapled. The Intel application's `jason` is built and signed as the Apple silicon one is, and started once under
Rosetta where the release's runner has it; the test suite does not run on an Intel Mac.

On Linux, install the deb with `sudo apt install ./jason-desktop-linux-x64.deb`; it puts Jason in `/opt/Jason` and
in the applications menu. Or make the AppImage executable, `chmod +x jason-desktop-linux-x64.AppImage`, and run it
from wherever you keep it; it is built on the AppImage runtime that needs no libfuse2. Jason never runs without
Chromium's sandbox: where the system does not let the AppImage use it, the AppImage says so and does not start, and the
deb is the package to install. Neither is signed, and neither is
the `jason` they carry, as the Linux archive's is not. Checked beside the deb,
`sha256sum --check --ignore-missing checksums.txt` proves it is the file this release published, not who built it;
checked beside the AppImage, it proves the same of the AppImage and no more. The `jason` they carry needs the ICU
library (`libicu`), as the archive's does.

The desktop updates itself from the latest release's `latest.yml`, `latest-mac.yml` or `latest-linux.yml`, on your
word, and to stable releases only. A candidate's feed files stay on the candidate's own page, which never becomes the
latest release, so an installed desktop, a candidate included, never moves to a candidate.

Installing an update restarts the runtime: the runtime the application carries is stopped first, with
`jason runtime stop`, as the menu's Restart Jason and Its Runtime to Install says, and the new version starts it again
when it opens. A runtime that does not stop leaves the update uninstalled, saying why, and a runtime started from
another `jason` is never stopped. Before the update installs, the application as it was is copied under
`~/.jason/desktop/rollback/<version>/`, and the Jason menu names that copy until the next update replaces it. Nothing
puts it back by itself: to go back, quit Jason and put that folder back in the place of the installed application by
hand. That puts back the previous application and the `jason` it carried; the data directory stays as the newer
version left it.

On Linux the deb's update installs the new deb with `dpkg`, which asks for an administrator's password every time,
through the first of `gksudo`, `kdesudo`, `pkexec` and `beesu` the system has, else `sudo`: an account that cannot give
one cannot install the update, and Jason stays open and says so. A deb update keeps no copy; to go back, install
the previous release's `jason-desktop-linux-x64.deb` the same way. An AppImage update replaces the file where it is,
so you need to be able to write to its folder, and keeps the file as it was under `~/.jason/desktop/rollback/<version>/`.

Uninstalling the desktop on Windows stops the runtime the application carries first, and removes nothing when that
runtime does not stop. It then takes off what it put there - the `jason` command it placed on the PATH, Open Jason at
login, and the runtime's logon start when that start names the application's own folder - and leaves the data
directory (`~/.jason`), the harness root (`~/Jason`) and the updater's cache, `%LOCALAPPDATA%\jason-desktop-updater`,
saying so. On Linux, `sudo apt remove jason-desktop` removes the deb, and deleting the file removes the AppImage; the
data directory and the harness root stay.

## Installing

One line, on macOS and Linux:

```sh
curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh
```

and on Windows, in PowerShell:

```powershell
irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex
```

Jason is released under the MIT licence: <https://github.com/reply-team/jason-ai/blob/v{{version}}/LICENSE>.

Both find this machine's archive, check it against `checksums.txt` before unpacking, and put `jason` on the
PATH — in `~/.local/bin`, or `%LOCALAPPDATA%\Programs\jason` — with its licence and the third-party notices
beside it, as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`. Running the line again over an installed Jason of
the same version changes nothing and says so, unless a licence file is missing beside the executable, which it
then writes. Two options, for a script that must be told:

```sh
curl -fsSL https://get.jasonharness.ai/releases/download/v{{version}}/install.sh | sh -s -- --version {{version}} --no-modify-path
```

```powershell
& ([scriptblock]::Create((irm https://get.jasonharness.ai/releases/download/v{{version}}/install.ps1))) -Version {{version}} -NoModifyPath
```

## Upgrading from 0.1.0

If this machine ran 0.1.0 with autostart enabled, run `jason runtime autostart enable` or `disable` once from an
ordinary prompt — if that answers Access is denied, follow its message first — then `jason runtime stop` once, then
`jason runtime start` (or sign in again, with autostart enabled). 0.1.0's logon task started a runtime that outlives a
sign-out and holds the data directory; until it stops, the runtime this version starts at sign-in finds the directory
taken and exits.

## For scripts that call the CLI

Since 0.34.0, `jason suppression remove`, `jason route set`, `jason route unset` and `jason role set-profile` take no
default actor: outside a run, a command that does not name who acts with `--actor human:<Name>` exits 2 before any
request.

## Checking a download by hand

```sh
sha256sum --check --ignore-missing checksums.txt
```

On Windows, compare `Get-FileHash` of the archive with its line in `checksums.txt`.

If you then put the executable in place by hand, keep `LICENSE` and `THIRD-PARTY-NOTICES` beside it, named
`jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`.

## What is signed

The macOS executable is signed with Reply's Developer ID and notarized by Apple. Before anything is published, the
release's macOS job has `spctl` assess the executable's signature and stops unless that signature is accepted from the
source `Notarized Developer ID`; the same job starts a copy carrying the quarantine flag a browser puts on a download,
and its log says whether that copy started and opened its database. The ticket is not stapled to the file, which a
bare executable has no place for; for a file without a stapled ticket, Apple's documentation says Gatekeeper finds the
ticket online. The quarantine flag is put there by the browser that downloaded the file, not by `curl`, so the
one-liner's download has none and `install.sh` clears nothing; an archive fetched with a browser and unpacked by hand
keeps the flag.

Who signed it is yours to read: `codesign -dv --verbose=2` on the unpacked executable prints an
`Authority=Developer ID Application:` line naming Reply and its team id in parentheses, the same id the
`TeamIdentifier=` line gives. `spctl` accepts the signature of any notarized Developer ID from the source
`Notarized Developer ID`, so it is that name and that team id, not the acceptance, that say the executable is Reply's.

The one native library the executable carries, `libe_sqlite3.dylib`, is signed with the same Developer ID and
timestamped before it is bundled; the executable unpacks it under `~/.net/jason/` the first time it runs, and
`codesign -dv --verbose=2` on it prints the same `Authority=` and `TeamIdentifier=` lines. So the executable keeps the
hardened runtime's library validation, which loads only a library signed by Apple or by the executable's own team, and
it ignores `DYLD_` environment variables: the only entitlements it carries, `com.apple.security.cs.allow-jit` and
`com.apple.security.cs.allow-unsigned-executable-memory`, let the .NET runtime compile code as it runs.

The Windows executable is signed with Reply's certificate through Azure Artifact Signing, and timestamped under
RFC 3161, so the signature stays valid after the certificate expires. Who signed it is yours to read, in Windows
PowerShell or PowerShell alike: `Get-AuthenticodeSignature` on the unpacked executable says `Valid`, and, run where it
was unpacked, `(Get-AuthenticodeSignature .\jason.exe).SignerCertificate.Subject` prints the subject the certificate
is issued to, which begins `CN=ReplyApp Inc.`; `signtool verify /pa /v`, where the Windows SDK is installed, accepts it
and prints the timestamp. An archive a browser downloaded carries the mark of the web, and
SmartScreen looks at the executable unpacked from it the first time it runs. SmartScreen's reputation for a new
publisher builds over downloads, so in the first weeks of signed releases a download may still show its dialog;
`install.ps1` clears that mark from what it unpacks.

The Linux executable is not signed, and nothing else a release carries is either, apart from the desktop's installer
and Mac packages, as "Desktop" above says.

Both scripts check the archive against `checksums.txt` before unpacking. On Linux that check is the only provenance
there is; on macOS and Windows the signature says who built the executable as well.
