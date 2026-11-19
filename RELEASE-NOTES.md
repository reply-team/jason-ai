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

# Jason 0.48.6

## What is new since 0.48.5

- **A campaign's manager looks at it when it starts.** When you start a campaign, or start it again after a pause, its
  manager looks at it at once instead of at its first scheduled review, five hours later by default. A role can pause a
  campaign and cannot start or resume one: that is yours, or your own session's on your word.
- **The desktop chat follows the answer.** While you are at the end of the conversation, it follows what is added
  there: your message, the AI's words as they come, a card. Scroll up and it keeps your place until you come back to
  the end or send something; a message you send, and a card that asks for your permission, always bring you back,
  the first message of a new chat included. Approvals waiting under the conversation stay under it, and the end of
  what was said stays in view above them.
- **Approved lines leave once their work is done.** Under an agent's chat, a line that says "It was sent." or "It is
  done." leaves five seconds after it says so. A line whose work is still on its way stays until it gets there, and
  one that says the work was not sent or not done stays, since it is the sign that something failed.
- **Copy code.** A code block in the desktop chat's answers has a **Copy code** button at its top right, which puts the
  block's text on the clipboard exactly and says "Copied", or "Not copied" if the clipboard refused it.
- **A way out of Settings.** **Back**, at the left of Settings' head, and the Escape key return to the place you
  opened Settings from, or to Start.
- **Check for Updates says what it found.** A check you ask for says, under the item in Settings, that Jason Desktop
  is the latest version, that a newer version is available, that a newer version is out but not offered because you
  went back from it, or why it could not check. Asked from the tray, the menu bar or the key, the same sentence comes
  as a notification. The checks Jason makes by itself say nothing.
- **Question chips only in an empty chat.** The desktop chat no longer offers suggested questions under an agent's
  answer; an empty chat still opens with its three.
- **Plain labels.** Settings › Your name and tone names the writer's grammar forms Female, Male and Neutral.

## Upgrading from 0.48.5

Nothing stops, no setting is renamed and no migration runs.

- **The desktop:** from 0.48.5, Jason Desktop takes this release by its own update. A Jason Desktop of 0.48.4 or
  earlier on Windows and macOS installs this release from its installer once; on Linux its own update works.
- **The executable:** `jason update`, as for every release.
- **The manager's triggers:** an installation that wrote its own `Manager:Triggers` keeps its list; add
  `campaign_started` to it to have a start summon the manager. Each start, a resume included, is one more review on
  your agent host and your plan.
- **From 0.48.4 or earlier,** the notes of each release in between apply as well.
