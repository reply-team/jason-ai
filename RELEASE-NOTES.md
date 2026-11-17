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

# Jason 0.48.2

## Fixed since 0.48.1

- **Jason Desktop's Command Line item puts `jason` where a terminal finds it, on macOS and Linux.** The item placed
  the command in `~/.local/bin` and said nothing more, and where that folder was not on the PATH, as on a fresh Mac, a
  terminal did not find `jason`. Now, when the folder is not on your login shell's PATH, the item also adds it there
  by the install script's own rule: your login profile for your shell, and `~/.zshrc` or `~/.bashrc` for zsh and bash,
  one marked line each, never twice. The confirmation names every file it will write and the line, and afterwards it
  says where the line went and to open a new terminal. A profile file it cannot write is named, with the line to add
  by hand. **Remove jason from the PATH…** and the desktop's own removal take back only the line they wrote, and only
  where the folder then holds nothing but what Jason placed there; where other programs live in it, the line stays and
  the item says so. Windows is unchanged.

## Upgrading from 0.48.1

Nothing stops, no setting is renamed and no migration runs.

- **The desktop:** update it from this release's installers or the update an installed desktop offers. Where an
  earlier desktop placed the command, the updated one offers **Put jason on the PATH…** once more, and that act writes
  only the profile lines.
- **The executable:** `jason update apply`, or run the install line again; nothing in it changed for this fix.
- **From 0.48.0 or earlier,** the notes of each release in between apply as well.
