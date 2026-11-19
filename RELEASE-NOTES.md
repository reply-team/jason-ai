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

# Jason 0.48.7

## What is new since 0.48.6

- **Copy code is an icon.** A code block in the desktop chat's answers has a copy icon at its top right. Once the
  block is on the clipboard it shows a green check for about two seconds, and a red cross if the clipboard refused it.
  Its name, and the tip a resting pointer shows, still say Copy code, Copied or Not copied. On a block of one line
  the icon stands in the block's middle; on a taller block it stays at the top right.

## Fixed since 0.48.6

- **The tray's menu is narrow again after an update.** The line under the update item, in the tray's menu and in the
  Jason menu, now says only which version is kept, "The previous version 0.48.6 is kept.", without the folder. A start
  that failed and an update that was not installed still name the folder, where you need it.
- **Back sits on the line of Settings' title.** Back, at the left of Settings' head, no longer sits lower than the
  title.

## Upgrading from 0.48.6

Nothing stops, no setting is renamed and no migration runs.

- **The desktop:** Jason Desktop takes this release by its own update.
- **The executable:** `jason update`, as for every release.
- **From 0.48.5 or earlier,** the notes of each release in between apply as well.
