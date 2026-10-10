<!--
The body of every release. release.yml fills in {{version}} and hands the result to `gh release create`; edit
this file rather than the release page, so that the next release says the same things.

The button (workflow_dispatch) creates a draft. A draft's tag does not exist until the draft is published:
deleting the draft leaves nothing behind, and publishing it is what creates the tag. So a dry run's draft is
deleted after inspection, unless it is meant as the release.
-->

Jason {{version}}: the runtime, the CLI and the plugin host, one self-contained executable per platform, built
from the runtime's source at `v{{version}}` — which is not published — by the tests that passed there.

## Assets

| File | For |
| --- | --- |
| `jason-win-x64.zip` | Windows on x64 |
| `jason-linux-x64.tar.gz` | Linux on x64 |
| `jason-osx-arm64.tar.gz` | macOS on Apple silicon |
| `manifest.json` | what a running Jason reads to learn that this release exists |
| `checksums.txt` | the SHA-256 of each archive, in `sha256sum` format |
| `install.sh` | macOS and Linux: the install script this release was published with |
| `install.ps1` | Windows: the install script this release was published with |

Each archive holds the executable, `LICENSE` (the runtime's licence; the repository's MIT does not cover the
runtime) and `THIRD-PARTY-NOTICES`, nothing else. Plugins are installed from the repository as before: a package
is copied into `~/.jason/plugins/`. On Linux the executable needs the ICU library (`libicu`), which desktop
distributions carry and minimal images may not; the install script stops, with the runtime's own message, rather
than install a program that does not start.

## Installing

One line, on macOS and Linux:

```sh
curl -fsSL https://github.com/reply-team/jason-ai/releases/latest/download/install.sh | sh
```

and on Windows, in PowerShell:

```powershell
irm https://github.com/reply-team/jason-ai/releases/latest/download/install.ps1 | iex
```

By installing Jason you accept the Jason Runtime Licence:
<https://github.com/reply-team/jason-ai/blob/v{{version}}/LICENSE-RUNTIME.md>. If you do not accept it, do not
install Jason.

Both find this machine's archive, check it against `checksums.txt` before unpacking, and put `jason` on the
PATH — in `~/.local/bin`, or `%LOCALAPPDATA%\Programs\jason` — with its licence and the third-party notices
beside it, as `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`. Running the line again over an installed Jason of
the same version changes nothing and says so, unless a licence file is missing beside the executable, which it
then writes. Two options, for a script that must be told:

```sh
curl -fsSL https://github.com/reply-team/jason-ai/releases/download/v{{version}}/install.sh | sh -s -- --version {{version}} --no-modify-path
```

```powershell
& ([scriptblock]::Create((irm https://github.com/reply-team/jason-ai/releases/download/v{{version}}/install.ps1))) -Version {{version}} -NoModifyPath
```

## Upgrading from 0.1.0

If this machine ran 0.1.0 with autostart enabled, run `jason runtime autostart enable` or `disable` once from an
ordinary prompt — if that answers Access is denied, follow its message first — then `jason runtime stop` once, then
`jason runtime start` (or sign in again, with autostart enabled). 0.1.0's logon task started a runtime that outlives a
sign-out and holds the data directory; until it stops, the runtime this version starts at sign-in finds the directory
taken and exits.

## Checking a download by hand

```sh
sha256sum --check --ignore-missing checksums.txt
```

On Windows, compare `Get-FileHash` of the archive with its line in `checksums.txt`.

If you then put the executable in place by hand, keep `LICENSE` and `THIRD-PARTY-NOTICES` beside it, named
`jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`.

## What is not here

Nothing in this release is signed or notarized. On Windows, an archive a browser downloaded carries the mark of
the web, and the executable unpacked from it is stopped by SmartScreen the first time it runs: `install.ps1`
clears that mark from what it unpacks. On macOS the quarantine flag is put there by the browser that downloaded
the file, not by `curl`, so the one-liner's download has none and `install.sh` clears nothing; an archive
fetched with a browser and unpacked by hand keeps the flag, and Gatekeeper asks about it the first time. Both
scripts check the archive against `checksums.txt` before unpacking, and that check is the only provenance there
is.
