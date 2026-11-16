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

# Jason 0.47.1

## What is new since 0.47.0

This patch release is about what a person meets in the console: the Demo in Jason Desktop is now the console's own Demo,
built to be shown inside another application, a browser tab can show it too, and the console's existing screens look
more like one style.

- **The Demo in Jason Desktop is the console's Demo.** The window shows the same mock of Jason at work on sample data as
  before, now as the console's Demo built for embedding, received as built. It runs on scripts alone, with an opaque
  origin, so it shares nothing with any other page and sets no cookie; what you type or keep in it stays in the window's
  in-memory session, and no request reaches any address.
  - The page's top strip carries the page's own words, and the desktop's **Demo | Live** switch stands alone at its
    right end. The Demo has no installation, so it offers no **Open in**.
  - On Windows the strip's sentence stays clear of the switch and the caption buttons at every width: where the window
    is too narrow for the long sentence the page shows its short one, and at the narrowest widths the switch is drawn a
    little tighter.
- **A browser tab can show the Demo.** In a tab, with or without Jason Desktop behind it, the console's **Demo | Live**
  switch is on. **Demo** moves the address to `#/demo` and shows the same page over the whole tab, with the
  switch at the right end of the page's own top strip; **Live**, the browser's Back or any other address brings the console back where
  it was, and a reload at `#/demo` shows the page again from its beginning.
  - The switch changes that tab only: nothing is kept, the runtime is not told, and the installation's mode and your
    installation stay as they were. In Jason Desktop's own window the switch is the desktop's, as before.
  - The page is sealed in the same way as in the desktop: scripts alone, never the console's origin, so it holds nothing
    of the runtime's, the console's token included. Narrower than 900 pixels the page keeps that width and scrolls
    sideways under a strip that holds the switch. A custom UI build without the page says so in its place.
- **The console looks more like one style.** Nothing here changes what a screen says or does.
  - The sidebar's rows sit a little further apart. The row you are on has its own darker fill and a medium weight, so it
    no longer looks like a row under the pointer.
  - In the access menu each mode carries its own mark at the left, the same mark the button shows.
  - On a plan card, what you do by hand is an amber box, and each step's channel is a coloured tag.
  - On an accounts card an account is its name over its address, the channel has a glyph, and the status is a chip that
    shows the provider's own word; only `connected` and `enabled` are tinted green.
  - On an agent's status card the agent's state is a pill at the top, from the runtime's own status.
  - An agent's empty Inbox tab is centred in the tab.
  - In Needs you, the state where nothing waits is a centred card with a check mark and a **New GTM agent**
    button beside **Open your agents**, and the pane that reads an item is a white rounded card.
  - In Settings the list of sections has a group label and a mark for each entry.

## Upgrading from 0.47.0

Nothing stops and no setting is renamed, and no migration runs.

- **The executable:** `jason update apply`, or run the install line again. After it, `jason ui install` brings the
  console's changes.
- **A custom UI build:** a page it places under `sealed/` is served with scripts alone now, so a form in a page under
  `sealed/` no longer raises its submit event.
- **The desktop is a separate install:** `jason update` does not install it. Download the installer or the disk image
  for your machine from this release, or let an installed desktop offer the update.
- **From 0.46.x or earlier,** the notes of each release in between apply as well.
