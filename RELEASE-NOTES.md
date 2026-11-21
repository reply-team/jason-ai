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

# Jason 0.49.3-unwritten

Jason Desktop is now the one window onto Jason. `jason ui open`, and a click on Jason's notification on Windows and now
on Linux, open Jason Desktop at what waits for you, and never a browser: the browser tab's own screens and the chat in a
tab are gone. A web interface of your own keeps its door: the runtime serves your build from `<data>/ui/custom/`,
`jason ui pair` prints the link that pairs it, and `jason ui open --web` opens it in your browser.

## What is new since 0.49.2

- **`jason ui open` opens Jason Desktop.** It opens the window on Needs you, and with `--approval <id>` or
  `--question <id>` at that one item, through the desktop's own `jason-desktop:` link; `--no-open` prints that address
  and opens nothing, for an agent to hand to you. It asks the runtime nothing and never opens a browser, so an id is
  checked only for its kind and shape: one that is neither an approval's nor a question's is refused as the runtime
  refuses an id it does not know (`approval_not_found`, `decision_not_found`). Where no Jason Desktop that takes its
  links is installed for your account, as for a command-line install still waiting to move into it, it opens nothing
  and refuses with `desktop_not_installed`. It no longer passes its own output streams to the program it starts, so a
  program that reads its output to the end, an agent's tool call among them, is not kept waiting until the window
  closes.
  [Opening it](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/ui.md#opening-it) says the rules on each
  system.
- **`jason ui pair` prints the link that pairs a page.** It sends the Runtime API's `ui.open`, which `jason ui open`
  sent in 0.49.2, and prints what that printed: a link that pairs one page, once, within 60 seconds, with
  `--approval <id>` or `--question <id>` at that item's address. It opens nothing. Jason Desktop pairs its own window
  with it at each start.
- **`jason ui open --web` opens a web interface of your own.** Where `<data>/ui/custom/` holds a build of your own,
  an `index.html` and a `manifest.json` the runtime takes, it hands your default browser a link that pairs that
  browser, as `jason ui pair`'s does, and `--web --no-open` prints the link. Without such a build it asks the runtime
  nothing, opens nothing and refuses with `custom_ui_not_installed`: the release's own build is never opened in a
  browser.
  [A web interface of your own](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/ui.md#a-web-interface-of-your-own)
  says how such a page pairs, calls the Runtime API and follows the journal.
- **On Linux, a click on Jason's notification opens Jason Desktop.** It opens at the one approval or question when
  exactly one waits, and at Needs you otherwise, as `jason ui open` does; in 0.49.2 a click opened nothing of Jason's on
  Linux. The notification waits on the session bus until it is clicked or dismissed, and the item's id stays with the
  runtime: the notification server never receives it. Where notify-send hands on the click's activation token
  (libnotify 0.8.8 and later), the window comes to the front. Where no Jason Desktop takes its links, nothing opens,
  never a browser, and a second notification says why, in the words `jason ui open` would print. A click on a
  notification from a runtime started at logon, before your desktop session set its display, opens the desktop all the
  same, and a Jason Desktop a click started keeps running when the runtime stops. With dunst, the click is its middle
  button.
- **An AppImage's Jason Desktop takes its links.** Its menu entry, as the install line and the move from a command-line
  install write it, now declares the `jason-desktop:` scheme and starts the file in a form xdg-open starts outside GNOME
  and KDE too, and the folder's menu cache is written after it (`update-desktop-database`). So `jason ui open` and a
  notification's click open an AppImage the install line placed or a command-line install moved into; an AppImage you
  run from where you downloaded it has no menu entry and takes no links. The first start of this version brings the
  entry an earlier version wrote for its file to this form, and leaves any other entry alone; `jason uninstall` writes
  the menu cache again once the entry is gone.
- **On Linux, a link goes where xdg-open would send it.** `jason ui open` and a click open Jason Desktop only where
  xdg-open would start it for its scheme. Where your account's own default for `jason-desktop:` links names another
  program, or a menu cache names an entry that is gone, they open nothing and refuse with a new code,
  `desktop_links_elsewhere`, whose sentence names the file that sends the links elsewhere and the command or the line
  that gives them back.
- **A notification's click never opens a browser.** On Windows, with no Jason Desktop registered or its program gone,
  the click opens nothing, where it opened the browser UI before, and where Windows cannot be handed the desktop's
  address nothing else opens either.
- **No second notification from Jason Desktop on Windows and Linux.** The runtime's notification opens Jason Desktop
  on what waits, so the desktop no longer posts its own "Jason needs you" when its count rises; the tray's count and
  Needs you stay. On macOS the desktop's own notification stays as it was.
- **Jason Desktop is the one window.** Jason's screens, the Demo among them, are drawn in its window alone. Open in no
  longer offers Browser, which opened the desktop's chat in a tab of your browser, and the desktop no longer listens on
  `127.0.0.1` for such a tab. Over the release's build, a browser tab at the runtime's `/ui/` draws one sentence and
  nothing else, whatever the address names: "This is the place for a web interface of your own. To build one, ask your
  agent how." It has no link, no Demo and no console, reads no pairing and asks the runtime nothing. The page the
  runtime serves may connect to its own origin and no other.
- **The pages, the skills and the `ui` verbs' help say one window.** `docs/ui.md` is now the page of Jason Desktop's
  screens, with a section on a web interface of your own, and the README, the other pages, the skills and the help of
  the `ui` verbs say Jason Desktop, or a paired page, where they said the browser UI.

## Fixed since 0.49.2

- **Jason Desktop puts the role skills back after `jason uninstall` kept the data.** Installing the same version of
  Jason Desktop again after such an uninstall, the AppImage started by hand or the deb installed with
  `sudo apt install ./jason-desktop-linux-x64.deb` say, set up the terminal command and the agents' skills but not the
  role skills, so `jason status` read Not ready, failing `role_skills`. On every system, a start of Jason Desktop where
  `jason status` finds no record of the role skills' deployment now deploys them from the package it carries, however
  they went, so `jason status` no longer fails `role_skills`.

## Upgrading from 0.49.2

- **Where you used the browser UI.** Jason's screens and its chat are in Jason Desktop: open it as any application, or
  with `jason ui open`, which opens it at what waits, and with `--approval <id>` or `--question <id>` at one item. A
  browser you paired before keeps its token until you revoke it, though the release's page there no longer uses it:
  `jason ui tokens` lists the pages paired, and `jason ui revoke <token-id>`, or Revoke all other browsers on the System
  screen, ends one.
- **A web interface of your own.** Its build, an `index.html` and a `manifest.json` that holds exactly `version` and
  `api_version`, goes in `<data>/ui/custom/` (`~/.jason/ui/custom/` by default), where the runtime serves it in place of
  the release's build at once. Jason Desktop's window is served it too and draws it in place of Jason's screens;
  deleting `custom/` brings them back. `jason ui open --web` opens it in your browser, paired, and `jason ui pair`
  prints a link that pairs one page, for a browser you open yourself. Its page may connect to the runtime's own origin
  alone: a connection to another port on `127.0.0.1`, which 0.49.2 allowed, is refused.
- **Scripts and agents that call `jason ui open`.** It now prints Jason Desktop's address,
  `{"address":"jason-desktop://attention"}` say, where it printed a pairing link, and asks the runtime nothing. It exits
  1 where no Jason Desktop takes its links (`desktop_not_installed`, under `--no-open` too), and on Linux outside a
  graphical session, over ssh say, it opens nothing and exits 1 with `opener_not_started`. Take the pairing link from
  `jason ui pair`, whose output is what `jason ui open` printed in 0.49.2.
- **Going back.** This version adds no migration to the database, so 0.49.2 opens the same data as it is.

## Known issues

- **On GNOME, a link without the click's token does not bring a running Jason Desktop to the front.** `jason ui open`,
  and on Ubuntu 24.04 a notification's click (its libnotify, 0.8.3, hands on no activation token), change the running
  window's page where it is; GNOME says Jason Desktop "is ready", and a click on that brings it forward. On Hyprland
  the window keeps its place unless `misc:focus_on_activate` is set.
- **On macOS, a click on Jason's notification opens nothing of Jason's**, as before; `jason ui open` opens Jason
  Desktop at what waits.
- **On Linux, a click reaches Jason only while its notification waits.** At most ten wait at once, a newer one ending
  the oldest one's wait; the runtime ends every wait when it stops, so a notification shown before a stop opens
  nothing; and notify-send before 0.7.10, or gdbus where there is no notify-send, shows a notification no click
  reaches.
- **Outside GNOME and KDE, a `jason ui open` that starts Jason Desktop ends after ten seconds**, saying on stderr that
  it handed the address on (`opener_timed_out`), and exits 0: xdg-open there runs the desktop until its window closes.
  The desktop opens all the same.
- **An AppImage in a folder whose path holds a space or a quote takes no links.** Its menu entry can start it only by a
  quoted path, which xdg-open outside GNOME and KDE cannot start, so `jason ui open` and a click refuse with
  `desktop_links_elsewhere`.
