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

# Jason 0.48.4

## Fixed since 0.48.3

- **The Mac Demo window has no empty strip at its top.** On macOS the Demo page now starts at the window's top edge,
  as it does on Windows and Linux. The empty white strip above the yellow Demo line is gone. The window's buttons
  stand over the top of the sidebar, and the sidebar's contents start below them. You move the window by its top
  strip, up to a little way before the **Demo | Live** switch, and the switch takes your click.

## Upgrading from 0.48.3

Nothing stops, no setting is renamed and no migration runs.

- **The desktop:** update it from this release's installers or the update an installed desktop offers.
- **The executable:** nothing in it changed for this fix.
- **From 0.48.2 or earlier,** the notes of each release in between apply as well.
