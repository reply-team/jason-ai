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

# Jason 0.36.0-unwritten

## What is new since 0.35.0

- **Full access** is a third mode in the desktop's chat, beside Ask first and Plan only, chosen from the menu beside the message box after a confirmation shown each time, with Cancel focused; Cancel keeps Ask first. In it the agent runs commands that Jason reads plainly as reads without asking you. Anything else Jason holds as a card that waits thirty minutes and is then not run; what Ask first refuses is refused here too. Each grant works in a folder of its own, which Jason keeps for seven days after Full access ends and then removes. It needs Claude Code 2.1.322 or later and is Claude Code only: Codex keeps its read-only sandbox.

## Fixed since 0.35.0

- **Appending to a file several plugins write at once** no longer fails one of them while the others keep writing: the wait for the file counts as stuck only while nobody makes progress, and a holder that writes nothing still fails the append after ten seconds.
