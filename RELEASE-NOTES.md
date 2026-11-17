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

# Jason 0.48.0

## What is new since 0.47.2

This release changes what a browser tab with no desktop shows, and how the Models and Connect card, the Reply card, the
Open in menu, the chat's bar and its choice card look. It gives Jason Desktop on Linux a frame of the console's own. It
also corrects the Demo's clicks on macOS and the words the window says after an editor is opened.

- **A browser tab with no desktop never draws the console.** A tab that no desktop opened, as `jason ui open` opens it,
  reads what waits on its first load. With nothing waiting it opens on the Demo, as a press of **Demo** does. With
  something waiting, or when the read cannot be made, it opens on one centred page: "Live Mode is only available in
  Jason Desktop.", then "To access Live Mode, download Jason Desktop. The desktop app starts an AI session locally on
  your computer." with a link to the releases page, then that you can build your own web interface or work directly with
  Jason from a terminal session with your AI using the installed Jason skills. The console's **Demo | Live** switch
  stands at the top right: **Demo** opens the demo in that tab, and **Live** brings the page back. Until the read
  answers, only the switch is drawn. A press of **Live** or **Demo** is your choice for that tab, kept for as long as
  the tab's session lasts, so a reload keeps it; `#/demo` in the address still means the Demo.
  - **While approvals or questions wait,** a quiet line under the words counts them and says where to decide: in Jason
    Desktop, or ask your agent what Jason is waiting for, or run `jason approval list` and `jason decision list`. With
    nothing waiting, or when the read fails, there is no line.
  - **The console is drawn in two places:** Jason Desktop's window, and a tab the desktop opens. An unpaired tab still
    shows the pair-again page.
  - **`jason ui open`** still opens and pairs whatever UI is installed, a build of your own included, and `jason ui`
    and `jason ui open --help` say where this release's console is drawn.

- **Models and Connect tell what is on this computer apart from what is not.** The card is one drawing, on Connect and
  in Settings' Models, and it draws two groups.
  - **On this computer:** each program found is a card of its own, with its name, its version as a pill and **In use**
    on the program the agents' roles run on by default; what the program does is set apart from the how-to lines under
    it. One row, **Model keys**, shows each provider whose key is set; with no key saved the row is absent.
  - **Add or change,** a tinted panel under it: the programs not found are named on one quiet line, those that can be
    installed first, each with its own **Install**. The model keys are one panel: a **Provider** select that says
    "key set" in the option of a provider that has one, an API key field with **Save key** for the provider shown, and,
    for a provider that has a key, "Key set" with **Remove the key** at once. The panel opens on the first provider with
    no key, and a key typed and not saved is forgotten when another provider is chosen.
  - **The button that saves a typed key says Save key,** where it said Keep.
  - In Settings, the card's own "AI" heading is left out under the section's "Models" heading.

- **The Reply card says what Reply is for, and its button reads Sign in with Reply.** On the Reply card of Settings'
  Accounts and of the chat's Connect card, which are one control, the card shows the name "Reply" with a **Recommended**
  mark, and under it one sentence: Reply.io is the service Jason sends through, your mailbox and LinkedIn account are
  connected to it, and Jason sends from them once you approve. At the right stands **Sign in with Reply**, which is the
  primary button only while nobody is signed in. Below it one row has a field, "Or paste a Reply API key", and **Use
  key**. The plain "Reply CLI" line with its version is gone. The sentences a person acts on stay: the CLI not found,
  and the CLI older than the account acts need. Settings' Accounts no longer draws a "Provider" heading around the card,
  since the card carries its own name; the browser page without the desktop keeps its heading.

- **The chat's opening chips are drawn in an empty chat only.** "What is waiting for me?", "How are my agents doing?"
  and "Help me start a new agent." no longer stand under an answer in the Feed or in a set-up chat. An agent's chat
  keeps the chips that follow the agent's state.

- **Settings' Accounts says what the notification channels are for.** The line under the table reads: "This is how Jason
  tells you that an approval or a question is waiting for you. To turn a channel on or off, use the Connect panel or ask
  your AI." In a browser tab, which has no Connect panel, the second sentence says "ask your AI" alone.

- **Jason Desktop on Linux has no title bar of its own.** As on Windows, the window's minimize, maximize and close
  buttons stand over the right end of the console's top strip, drawn in your desktop's own style, as many as its button
  layout names for windows, and in the console's ink in the theme in force. A layout that puts them at the left stands
  them over the sidebar's brand.
  - **Two keys,** for a window with no menu bar and a desktop that shows no tray icon (Cmd instead of Ctrl on a Mac):
  **Ctrl+Shift+D** for the update item, which asks, naming the version, before it installs, since the install stops the
  runtime and restarts the desktop; and **Ctrl+Shift+L** for Back to Your Own Installation, in the Demo. The sentences
  that send you to them name them.
  - **The chat's commands on Linux run without Claude Code's environment scrub,** so `jason` in a chat there reaches the
  runtime, which the scrub's sandbox hid. macOS and Windows keep the scrub. What this lets through: a key connected on a
  Connect card, and the credentials the turn carries from the desktop's environment, reach the commands the agent runs,
  as in a terminal; `docs/desktop.md` says what reaches them.

- **The Open in menu has an icon column, one-line hints and dividers, and stays inside the window.** Every item starts
  with an icon. On Windows and macOS a row for an installed application shows that application's own icon, read from the
  application itself at run time (on macOS from its bundle). Where that icon cannot be read, on Linux, and on every row
  with no application (Claude Code, Codex, Browser, Copy path, and Terminal on Windows), the row shows a glyph for its
  kind. The hint under Claude Code and Codex is the one line "A new session in a terminal"; the whole sentence is still
  the item's description and its tooltip. A divider follows the Agents group and another the Editors group, beside the
  one before Change Jason itself. The menu is 300 pixels wide, so names and one-line hints no longer wrap, and a menu
  taller than what is left below the strip scrolls inside itself instead of running past the window's bottom edge.

- **After an editor is opened from Open in, the window says which Jason a terminal there finds.** It reads the PATH the
  desktop gives the editor, and says one of three things: "jason on your PATH is this window's Jason, so a terminal
  there works with the same data."; "jason on your PATH is another Jason, not this window's: what you run there may work
  with other data."; or "jason is not on your PATH, so a terminal there does not find Jason." Each is followed by the
  way out that the Jason menu's Command Line item offers, named by its label (**Put jason on the PATH…**, **Put
  jason-desktop on the PATH…**, **Point jason at This Jason…** or **Point jason-desktop at This Jason…**), or says to
  type `jason-desktop` there; where no act would help, none is named. While `jason` on your PATH is another one or
  none, the Open in menu carries one quiet line under Editors that says so. After the file manager the window says
  nothing about data. The message opens under the split button, as wide as the menu.

- **The chat bar's Rename, Start fresh and Remove are icon buttons:** a pencil, a circular arrow and a bin, quiet in
  the muted colour. Each keeps its name and has a tooltip saying what it does; Remove's reads "Remove this
  conversation". What each does and when it is off are as before.

- **The chat's choice card is compact.** The question is the card's title; options without a detail are chips in a
  wrapping row, and options with one are a grid of equal cells, two across and one where the card is narrow. An answered
  card is quieter, with the chosen option outlined and checked. An open card, the last one in the chat, draws a field,
  **Something else…**, with an arrow: what you type is sent as your next message, as a send from the composer is.

- **The New GTM agent button and its drop-down are two controls.** **New GTM agent** is a light button across the
  sidebar's row; the drop-down is a small square button of its own beside it, which opens the Create menu.

- **The composer's menus are lighter.** In the model menu an item with no description is drawn at a regular weight and
  the chosen one a little heavier; the host tabs follow. The access menu, whose items carry a description, keeps its
  weight.

## Fixed since 0.47.2

- **In the Demo on macOS, clicks reach the page again.** The sidebar, the prompt box and the buttons took no click,
  because the band above the Demo's page was marked as a drag region over the whole window. It now drags over its own
  26 pixels alone. The Demo on Windows and on Linux was not affected.
- **Inside an agent's People tab, the person panel's tabs look like the link tabs.** There the panel's tabs are buttons,
  not links, and they showed as the browser's grey boxed buttons; they now have the link tabs' height, font, colour,
  hover and underline.
- **The act that drops an agent's sessions and keeps its record is worded "Start fresh"** where a person reads it,
  where it said "Start afresh": the chat bar's button and its tooltip, the agent's menu ("Start conversation fresh") and
  the notice after it ("Started fresh: ..."). The act itself is unchanged.

## Upgrading from 0.47.2

Nothing stops, no setting is renamed and no migration runs.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason ui install` brings the
  console's changes.
- **A bookmark, a link or a notification's click into the browser UI** now opens the Demo in a tab no desktop opened,
  or, while something waits, the one page described above; the console is in Jason Desktop. A build of your own in
  `ui/custom` draws whatever it draws.
- **The desktop is a separate install:** `jason update` does not install it. Download the installer or the disk image
  for your machine from this release, or let an installed desktop offer the update.
- **From 0.47.1 or earlier,** the notes of each release in between apply as well.
