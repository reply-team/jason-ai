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

# Jason 0.51.1-rc.1

Jason 0.51.1 is a small release. Where npm is on the computer, Jason Desktop now updates an older Reply CLI to the version
Jason needs by itself when it starts, so an update of Jason need not leave the Reply plugin waiting for you to run npm. The
handbook now lists what Jason asks of the network by itself.

## What is new since 0.51.0

**The Reply CLI**

- **Jason Desktop updates an older Reply CLI when it starts.** Where the Reply CLI installed on your computer is older than
  Jason needs, the Reply plugin is unavailable and Connect asked you to update it. Jason Desktop now does that once by
  itself, in the background of a start in Live, with the same npm line as the Update button. It runs nothing
  where a plugin that runs the Reply CLI is loaded and valid, or where the runtime does not say. The Reply card says in one line what came of it. It tries once for each version Jason
  comes to need; after that the button is yours. A missing Reply CLI is still installed only on your click, and
  nothing of this happens in the Demo. `Update:CheckEnabled` set to `false`, in the settings file or in Jason Desktop's own
  environment, turns it off.
  [What it does and what turns it off](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/desktop.md).
- **The Reply card without npm says what to do.** Where neither the Reply CLI nor npm is found, the card says which program
  to install, from where, and to open Jason again.

**The pages**

- **The handbook lists what Jason asks of the network by itself**: when, and what turns it off where something does. The
  responsible-use page names the Reply CLI's update and an identifier that Jason Desktop's updater sends with each
  check. Only the update above is new in what Jason does.
  [The handbook](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/handbook.md).
- **The README says first what Jason is and that it installs from a release,** with the walkthrough after Quick start,
  answers to common questions, a [comparison page](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/comparison.md)
  and an `llms.txt` for assistants.
- **The pages and the install script's help say that the Homebrew tap holds the cask alone,** and how an installation
  that the older formula made moves to Jason Desktop.

**Speed**

- **When Jason checks whether one letter held for later went or is overdue, it finds that letter by its own id,** instead
  of reading through all the finished work of the installation.

## Fixed since 0.51.0

- **A click on Update now has the runtime read its plugins again.** The Reply plugin used to keep the CLI version it last
  found, so the red line stayed after a successful update. Like any reload of plugins, this one also takes up what you
  edited by hand since the last: routes, grants and packages.
- **`jason status --human` names a failed check in its "To repair" block** even where the check has no command to run: the
  line to read, in place of nothing.
- **`jason runtime stop` says it holds until the next logon only where the logon starts this data directory.** With a
  second data directory, the sentence was not true for the one you had stopped.

## Upgrading from 0.51.0

- **Updating from 0.51.0.** On macOS and Windows, Jason Desktop's update item takes this version, and so does
  `jason update apply` in a terminal. On Linux, a deb takes it with the system's own updates, and an AppImage by its own
  update item or `jason update apply`. The first start of this version then sets Jason up again, as after any update.
- **An older Reply CLI is updated at that start**, as said above, and the Reply card says what came of it. Without Jason
  Desktop, or where npm could not, update it by hand with `npm install -g reply-cli`.
- **This version does not migrate the database.**

## Known issues

- A click on Update runs npm even where a plugin that runs the Reply CLI is loaded.
- A quit or a switch of mode while Jason Desktop is updating the Reply CLI is not waited for: the Reply card then offers
  Update. Where npm itself placed an older version, the card's line says the CLI was installed another way.
- The daily ceiling of automatic sends still counts its day in UTC, while the daily limits of launches count your
  installation's own day.
- Your own Claude Code skills, hooks and MCP servers are not loaded in a chat that runs on a key set in Jason, nor in a
  turn that runs in Ask first, Auto-edit or Plan only.
- No screen lists the archived agents yet.
- In a Codex chat in Full access none of Jason's own refusals apply, as in a terminal: the turn runs as Codex runs it.
- An agent has one conversation: there is no second chat for the same agent yet.
- On Windows, a `%NAME%` in the agent host's words may still be expanded by a Windows Terminal that is already
  running, over its own environment.
- On Windows, a dropped file's path whose name holds a single quote together with a `$` or a backtick, or a network
  path whose name holds a single quote, is quoted as PowerShell reads it, and Git Bash reads it back wrong.
- On GNOME, a link without the click's token does not bring a running Jason Desktop to the front. `jason ui open`, and on
  Ubuntu 24.04 a notification's click, change the running window's page where it is; GNOME says Jason Desktop "is
  ready", and a click on that brings it forward. On Hyprland the window keeps its place unless `misc:focus_on_activate`
  is set.
- On macOS, a click on Jason's notification opens nothing of Jason's; `jason ui open` opens Jason Desktop at what waits.
- On Windows, with no Jason Desktop registered or its program gone, a click on Jason's notification opens nothing, and
  nothing that stays on screen says why. `jason ui open` in a terminal refuses with `desktop_not_installed`.
- On Linux, a click reaches Jason only while its notification waits. At most ten wait at once, a newer one ending the
  oldest one's wait; the runtime ends every wait when it stops; and notify-send before 0.7.10, or gdbus where there is
  no notify-send, shows a notification no click reaches.
- Outside GNOME and KDE, a `jason ui open` that starts Jason Desktop ends after ten seconds, saying on stderr that it
  handed the address on (`opener_timed_out`), and exits 0. The desktop opens all the same.
- An AppImage in a folder whose path holds a space or a quote takes no links: `jason ui open` and a click refuse with
  `desktop_links_elsewhere`.
