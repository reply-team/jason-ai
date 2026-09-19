<!--
The body of every release. release.yml fills in {{version}} and hands the result to `gh release create`; edit
this file rather than the release page, so that the next release says the same things.

The button (workflow_dispatch) creates a draft. A draft's tag does not exist until the draft is published:
deleting the draft leaves nothing behind, and publishing it is what creates the tag. So a dry run's draft is
deleted after inspection, unless it is meant as the release.
-->

Jason {{version}}: the runtime, the CLI and the plugin host, one self-contained executable per platform, built
from the tag `v{{version}}` by the tests that passed at it.

## Assets

| File | For |
| --- | --- |
| `jason-win-x64.zip` | Windows on x64 |
| `jason-linux-x64.tar.gz` | Linux on x64 |
| `jason-osx-arm64.tar.gz` | macOS on Apple silicon |
| `manifest.json` | what a running Jason reads to learn that this release exists |
| `checksums.txt` | the SHA-256 of each archive, in `sha256sum` format |

Each archive holds the executable and `LICENSE`, nothing else. Plugins are installed from the repository as
before: a package is copied into `~/.jason/plugins/`. On Linux the executable needs the ICU library
(`libicu`), which desktop distributions carry and minimal images may not; the install script stops, with the
runtime's own message, rather than install a program that does not start.

## Installing

One line, on macOS and Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh
```

and on Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1 | iex
```

Both find this machine's archive, check it against `checksums.txt` before unpacking, and put `jason` on the
PATH — in `~/.local/bin`, or `%LOCALAPPDATA%\Programs\jason`. Running the line again over an installed Jason of
the same version changes nothing and says so. Two options, for a script that must be told:

```sh
curl -fsSL https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.sh | sh -s -- --version {{version}} --no-modify-path
```

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/reply-team/jason-ai/main/install/install.ps1))) -Version {{version}} -NoModifyPath
```

## Checking a download by hand

```sh
sha256sum --check --ignore-missing checksums.txt
```

On Windows, compare `Get-FileHash` of the archive with its line in `checksums.txt`.

## What is not here

Nothing in this release is signed or notarized. An archive a browser downloaded carries the mark that makes
Windows SmartScreen or macOS Gatekeeper stop the executable the first time it runs; the install scripts clear
that mark and check the checksum before unpacking, and that check is the only provenance there is.
