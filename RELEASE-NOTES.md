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

# Jason 0.48.5

## What is new since 0.48.4

- **Settings shows Jason Desktop's version and its update.** The foot of Settings' list, in Jason Desktop's window,
  shows the desktop's version and the same update item the tray's menu (on a Mac, the menu bar) shows: Check for
  Updates…, Download, or Restart to Install. Pressing it does exactly what the menu's item does.
- **Turn one notification channel on or off.** Settings › Notifications has a switch for each channel, and
  `jason notification enable <channel>` and `jason notification disable <channel>` do the same from the command line,
  for the system's own notifications (`toast`, on Windows, macOS and Linux) or a notification plugin. Whoever
  switches a channel is named, and the change is recorded. This release publishes the journal forwarder 1.7.0, whose
  manifest declares the kind a channel's switch writes (`notification_channel_updated`): a grant of every kind
  forwards it from the first load, and a grant that names kinds does not until you add it, with
  `jason plugin grant journal-forwarder --subscription notification_channel_updated`.
- **A Full access card says why it asks.** In the desktop chat, a card titled "Full access asks about this one" now
  says in one line why this request still asks, for example a command whose wildcard the shell expands only as it runs.
- **Approvals in plain words.** An approval's detail says what the act does in words a person reads at a glance:
  whether it contacts anyone, whether it can be undone, and what it costs. A message that was doubted is shown under
  "Why this message was flagged". A reply in a conversation says it goes from the account the conversation is in.
- **The chat queues what you send while it works.** While a turn runs, the round button sends your words when the
  turn ends, and what you send waits below the conversation, where **Take back** returns it to the box. Messages sent
  meanwhile go together as the next turn. Escape in the message box stops the turn.
- **The chat reads the command line's own help.** The desktop chat may run `jason <noun> --help` for the nouns it
  works with, instead of guessing at a verb or an option.
- **Change Jason itself opens the program you use.** Open in › Change Jason itself opens the agent host your
  preference names, else the one marked **In use** in Settings › Models, else Claude Code, as before.
- **A wiring check for a direct send.** The golden path has a section that checks a single email sent outside any
  provider campaign: one real send, to your own address, run only when you ask for it.
- **The Live start page** says the interface is optional and links the open-source repository.

## Fixed since 0.48.4

- **Jason Desktop's update installs.** The update the desktop offers no longer stops with "a copy of this version
  could not be kept aside to go back to". If keeping that copy ever fails, the message now says where to download the
  new version to install it yourself. See **Known issues** for a desktop of 0.48.4 or earlier.
- **The Demo | Live switch at start.** The Demo's page enters the window once its styles are in, so the switch no
  longer answers as the window's drag area while the Demo starts.
- **`jason status` and Windows' notification settings.** On Windows, `jason status` reads Windows' notification
  switches as they stand when it runs: a switch that is off now is said so, whatever the last notification did, and
  one switched back on is no longer reported as off. A failed notification is still reported first.

## Upgrading from 0.48.4

Nothing stops, no setting is renamed and no migration runs.

- **The desktop:** on Windows and macOS, install this release from its installer once (see **Known issues**). On
  Linux, and from 0.48.5 on everywhere, the desktop's own update works.
- **The executable:** `jason update`, as for every release.
- **From 0.48.3 or earlier,** the notes of each release in between apply as well.

## Known issues

- **A Jason Desktop of 0.48.4 or earlier on Windows and macOS cannot take this release by its own update,** since
  the code that fails is the installed one. Install 0.48.5 from its installer once; from 0.48.5 on, the
  application's own update works.
