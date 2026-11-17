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

# Jason 0.48.3

## What is new since 0.48.2

This patch release tidies what an agent tells you in your first campaign, and a few of Jason Desktop's screens: an
edited message stays where you left it, a dialog is never painted under the page, and an optional reason no longer
asks to be filled first.

- **Where to see what waits.** After a send, an agent no longer points you to the browser tab `jason ui open` opens:
  without Jason Desktop that tab shows the Demo, or a page of text while something waits, not your work. It names
  what does show it: **Needs you** in Jason Desktop, or `jason approval list`, `jason decision list` and
  `jason workitem list --campaign <id>` in a terminal.
- **"Did they answer?" is answered now.** Asked whether somebody replied, an agent no longer says "nothing yet" and
  waits for Jason's next read. It reads the person's threads from the provider at once (`jason contact threads`, then
  `jason contact thread`), says what they show and that such a read records nothing, and says from the journal and
  the responder's items whether Jason has noticed the reply yet. Where it has not, Jason notices it at its own next
  read, by default within ten minutes for a provider that reports events and fifteen for one Jason reads on its own
  cadence, and only then wakes its responder.
- **The plan says that messages can later go on their own.** The quick-start's plan says once, as a fact and not a
  question, that every message waits for your yes and that you can later let messages go on their own, for people who
  have not answered yet and for people who have: in Jason Desktop under Settings, "How messages go", and anywhere else
  by telling your agent.
- **Jason Desktop's chat loads a skill before a command it has not used.** Before the chat types a `jason` command it
  has not used in the conversation, it loads the skill for that work and takes the command from it. It no longer
  guesses one, which could fail, or wait unseen as a card for your permission.
- **An edited message stays in the list.** When you edit a message waiting for your yes and save it, its row stays
  where it was, selected, with the new text, wherever a message waits for you: Needs you, the console's Needs you, a
  campaign's Sequence and the chat's waiting card. It says that the text is saved and that Jason asks again with it
  when it next takes this work; nothing on it can be approved or rejected until then. When Jason asks, the row becomes
  the new approval in place, and says so if the text it carries is not the one you saved. A reload forgets the kept
  row until Jason asks again.
- **A dialog stands above the page.** A dialog such as Start, Pause or Add people is drawn on a layer above the page's
  menus and cards, so the chat's answer choices no longer cover a dialog's title. The search palette still opens above
  it.
- **An optional reason is small and waits its turn.** The "Reason (optional, kept in the journal)" field is one line
  high and grows as you type, up to about six lines. A dialog no longer opens with the focus on it: the focus goes to
  the dialog's first other field, else to Cancel, and never to the button that acts, so Enter as it opens does
  nothing. Where a browser cannot grow a field by itself, it stays one line and scrolls.
- **The Mac Demo window's top strip.** On macOS, the strip above the Demo page, right of the sidebar, is drawn in the
  page's own ground, so the yellow Demo band is as tall as its own line.

## Upgrading from 0.48.2

Nothing stops, no setting is renamed and no migration runs.

- **The executable and the skills:** `jason update apply`, or run the install line again; the update redeploys the
  skills it recorded. After it, `jason ui install` brings the console's changes.
- **The desktop is a separate install:** update it from this release's installers or the update an installed desktop
  offers. It brings the chat's new rule and the screens above.
- **From 0.48.1 or earlier,** the notes of each release in between apply as well.
