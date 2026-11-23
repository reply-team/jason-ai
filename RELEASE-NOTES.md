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

# Jason 0.50.0-rc.2

Jason 0.50 tunes how the work is done. A campaign's manager keeps two clocks, a quick look every six hours and a deep
review once a day, each starting from where the last one stopped, and you can ask it for a review at any moment. Each
person in a campaign has a line of their own: a reply pauses it, a ceiling of touches bounds it, and one command each
ends, continues and reopens it. Every role has a daily limit of launches, counted by your installation's own day, and
one word lifts it for today. The agent sets a campaign up with you and starts it on your yes, and your word outranks a
rule of the skills.

## What is new since 0.49.3

**Your word, in the chat and in a terminal**

- **Your word outranks a rule of the skills.** Where what you plainly ask departs from a rule the skills teach, the
  agent says once what follows from departing and asks, then does what you answer. What the runtime itself refuses
  stays refused, and the agent says so.
- **A new chat from the Chats plus is a plain chat.** It answers what you ask and offers no set-up, and it sets an agent
  up only when you ask for one. Start, with its box "Tell Jason what you sell and who you want to reach", still sets an
  agent up at once.
- **"Pause all" pauses at once.** A bare "pause all" pauses each campaign without asking first, and the agent then says
  which were paused. A bare "all" for starting or archiving is still asked once.
- **The command you name is the one run.** When you name a `jason` command yourself, `jason runtime status` say, the
  agent runs that one. Where it is refused or does not exist, the agent says exactly what was answered, and never runs
  another in its place.
- **"All of these" covers what was shown.** Said about a list the agent showed, it is your word for each item whose
  content was shown. Where some were not shown, the agent says how many would go out unread and asks whether to show
  them first.
- **A memory file is written on your word.** Otherwise the agent says the exact line and the file it would go in, and
  asks once.
- **Who decided, in the lists.** `jason approval list --human` has a DECIDED BY column, with the reason beside the actor
  where a send mode or a standing approval let an approval go, for example `system:dispatcher (automatic_mode)`.
  `jason decision list --human` has an ANSWERED BY column.
- **What you read is in your language.** The chat's short lines between actions are in your language too.
- **A memory file is what the agents plan by.** They read it and plan by it. It is never a rule Jason enforces: a
  message you approved on a Friday goes out whatever a file says about Fridays, and the chat says so.
- **On Codex, Ask first and Auto-edit read and propose.** Codex never asks in these chats, so they only read and
  propose, and the chat now says so: nothing written there reaches a person without your approval.
- **Plan only on Claude Code reads a folder with its own tools.** Asked what is in a folder, the chat reads it with its
  reading tools instead of stopping at the refused shell line.
- **In Plan only, a folder outside the chat's folders is not read.** The host refuses the read with no card, and the chat now
  says so and that changing the mode lets it read there.

**Setting a campaign up and starting it**

- **The set-up, then the start on your yes.** The agent sets a campaign up with you. It asks for the sender's name once,
  first; says how messages go out; checks that the roles can launch, and speaks only when something is wrong now; says
  once if nothing will tell you when Jason waits for you; and records a first batch of twenty, or the number you name.
  Then it asks whether to start, and starts on your yes, under your name. A missing tone of voice holds nothing back:
  the work goes on in a plain, neutral voice, and you are told once that you may name one. The question and the
  confirmation of the start are said in the language you write in.
- **A draft that stands is named at once.** The set-up reads the campaign list before its first question and names a
  matching draft.
- **The quick start takes up a draft that stands.** Where a draft campaign already stands for what you sell, the quick
  start fills it in and makes no second one.
- **A plan card's steps say when and by which channel.** A subject and a body go on a step only where you asked to see
  a letter, and are called an example.
- **After the start, the agent says what happens next, once.** What the set-up already said, about a check, the roles
  or the notifications, is not said again.
- **A reserve at the first start.** A campaign whose people come from a search gets a reserve threshold of twenty when
  it first starts, so its manager hears that people are running low while a top-up still has time to land. The number
  is `Manager:AudienceThreshold`. A threshold you set is kept, and `jason campaign update <cmp> --clear audience_threshold`
  leaves a campaign with none.

**A campaign's manager**

- **A quick look every six hours, and a deep review once a day.** A quick look reads the counts and acts on what is
  off. A deep review, on a daily grid that starts when the campaign goes live, widens the first batch, goes over each
  person's line and carries any cause that is waiting. `Manager:ReviewSeconds`, now 21600, sets the quick look's pace,
  and the new `Manager:DeepReviewSeconds`, 86400 by default, the deep review's. `jason campaign get --human` says when
  the next deep review is due.
- **One entry for each reason it is woken.** The manager's skill is a head and five entries: the first start, a resume,
  a quick look, a deep review and a reply. Each review begins from the note the last one left, so a deep review a few
  hours after another covers only what changed since. To change one kind of review, edit that entry's file.
- **Ask for a review now.** `jason campaign review <cmp>` asks the manager for a quick look now, and `--depth deep` for
  a deep review, which leaves the day's own deep review where it is. The chat runs it on your word.
  [The cadence](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/campaign-manager.md#4-the-cadence) says
  when a manager looks in.

**The line with a person**

- **Each person has a line in the campaign.** By default it is a first letter and two follow-ups, each written when it
  is due, one at a time. `jason campaign list-contacts` shows each person's `touches` and `last_touch_at`, and its
  summary counts the people whose line is paused or finished.
- **A ceiling of touches.** `Lines:MaxTouches`, 5 by default and from 1 to 20, is the most touches one person has in a
  campaign. A touch past it is refused before anything is sent, and the manager ends the line. A letter Jason took back
  is no touch.
- **A reply pauses the line.** When a person replies, their line is paused: Jason writes nothing more to them until the
  manager continues the line or ends it, and the person is still answered. A reply the provider names as the other
  side's always pauses the line. An out-of-office reply continues it, with the next letter moved to the date the reply
  names.
- **A letter held for later is taken back on a reply.** Where the provider holds a letter for a later moment and says
  it can be cancelled, a reply takes it back, and so does ending the person's line or removing them.
- **Finish, continue and reopen.** `jason campaign finish-contacts` ends a line, always with a reason, and cancels the
  person's waiting touches in that campaign. `jason campaign continue-contacts` takes a paused person back into their
  line. `jason campaign reopen-contacts` reopens an ended line, which is your decision: on your own word, or by the
  manager naming a question you answered yes. Where the person's touches already stand at the ceiling, the agent says
  that reopening alone sends nothing more.
  [A person's line in a campaign](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/work-execution.md#a-persons-line-in-a-campaign)
  says what each state means.
- **The people in a campaign, in a table.** `jason campaign list-contacts --human` shows each person's touches and the
  last one.
- **An opt-out with no provider sequence is completed.** Where the campaign holds no provider's campaign, as
  with Jason's own direct letters, recording the opt-out completes it, and the summary says nothing was stopped at the
  provider, with no stop that could only fail.

**Daily limits of launches**

- **Every role has a daily limit of launches.** `Launches:MaxPerDay` now has a default for each role Jason ships, 25 for
  the manager and the responder among them. A limit you set wins, entry by entry.
- **The day is your installation's own.** It runs from one local midnight to the next, in the system's time zone or in
  the IANA zone `Runtime:TimeZone` names, so the count no longer starts again in the middle of your afternoon.
- **Lift today's limits with one word.** Tell your agent "ignore the limit", or run
  `jason launches lift --actor human:<your name>`: what the limits hold is released at once, and nothing new is held
  until the installation's midnight. The agent says until when, in your own time and naming the zone, and how long from now. The notification
  of an item held at a daily limit says how to go on today.
- **"Ignore the limit" lifts what is held.** Where something is held at a daily limit, that is what the word means and
  it is lifted as you; the agent asks which limit only where nothing is held. Asked what the daily limits are, the chat
  runs `jason launches list`.
- **Read the limits.** `jason launches list` says the installation's day and when it ends, each role's limit and
  whether it comes from the default or from your settings, a lift that stands and how many items are held. With
  `--campaign`, it also says how many each role started there today. A limit is still set in the settings file.
- **A line on Needs you while work is held.** It says how many items are held at a daily limit and until when, with a
  button that lifts today's limits as you. It is not counted among what waits for you.

**Notifications**

- **A reply is notified, once.** When a role judges a reply to be the person's own, Jason tells you once, at the end of
  the run that judged it: "1 reply came in." Automatic replies, bounces and opt-outs are not counted, and under "every
  message" the reply and the approval its answer needs come as one notification. `Notifications:Replies` turns it off.
- **An old outcome is said as history.** A channel's last notification outcome that is older than the switches' present
  reading is told as what happened then; what `jason status` says now is the answer.

**Jason Desktop**

- **The plus buttons are smaller.** The plus beside Agents and the one beside Chats are drawn smaller and thinner, on
  the centre of their square.
- **The mark opens Start.** A click on Jason's mark at the top of the sidebar shows Start again, with the words you
  typed there and did not send, and closes nothing.
- **A chat you open has the caret in its box.** By any act of your own, from a row, Search, a plus, the Agents page, a
  link or Back and Forward, you can type at once. Space on an agent's tab now opens it, as a click does, and a screen
  reader hears which chat it enters.

## Fixed since 0.49.3

- **Words in another alphabet piped to `jason` on Windows are kept as written.** Standard input was read by the
  console's code page, so Cyrillic, CJK and symbols were stored garbled. It is now read as UTF-8 on every system, a
  file given by `--file`, `--value-file`, `--note-file` or `--result-file` is read the same way, and bytes that are not
  UTF-8 are refused, never guessed. The skills' PowerShell examples set `$OutputEncoding` first, since Windows
  PowerShell before 7 replaces such characters before Jason sees them.
- **The refusal at a daily limit says the limit as a limit.** It names how many runs started, the limit beside it and
  `jason launches list`, where it printed the limit as if it were the count.
- **An act's reason is the agent's own short line**, never the text you pasted.
- **The skills' examples pass long text on standard input.** A campaign's context, a role's brief or a note goes through
  a here-document, so an agent copying an example no longer breaks it on an apostrophe or shows the text in the list of
  running processes.
- **A reply Jason drafts in somebody's thread is plain text:** a draft that carried the thread's own HTML, such as
  `<br>`, is refused when it is written instead of reaching you with its tags on the card, and with the Reply plugin an
  email reply now goes out as HTML that shows exactly the text you approved, line breaks included.

## Upgrading from 0.49.3

- **Updating from 0.49.3.** On macOS and Windows, Jason Desktop's update item takes this version, and so does
  `jason update apply` in a terminal, which hands the update to Jason Desktop. On Linux, a deb takes it with the
  system's own updates, Software Updater or `sudo apt update && sudo apt upgrade`, and an AppImage by its own update
  item or `jason update apply`. The first start of this version then sets Jason up again, as after any update.
- **This version migrates the database.** Its first start writes a backup under `~/.jason/state/backups/` first, then
  adds the new record of a work item's entry, of the deep review's grid and of a day's lift. Each active campaign's
  first deep review comes one day after the update.
- **Going back.** From 0.49.0 on, a runtime refuses a database holding a migration it does not know (`data_newer`), so
  0.49.x refuses data that 0.50 has opened; a version before 0.49.0 has no such check and runs its own model over the
  newer schema, so it is not run on such data.
  [Going back](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#going-back) says
  when `jason update rollback` restores the backup the migration took, and
  [Restoring a database by hand](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#restoring-a-database-by-hand)
  how you put it back yourself where it does not. The journal forwarder is now 1.8.0, which a runtime before 0.50.0
  refuses;
  [Applying an older release](https://github.com/reply-team/jason-ai/blob/v{{version}}/docs/release-and-update.md#applying-an-older-release)
  says how to put back the package an older release published.
- **What changes for the campaigns you have.** A quick look comes every six hours, unless a campaign sets its own
  `review_seconds`. Every role has its daily limit, unless you set your own, and the day is the installation's from the
  update on. The ceiling of touches counts the touches already made, so a person who has had five touches in a campaign
  is sent no more there. A campaign already live is given no reserve threshold: only a first start sets one.
- **Your chats.** A chat about no agent made before this version reads as a plain chat.
- **A reply drafted before this release with HTML in its text, such as `<br>`, still waits for your approval.**
  Approved as it is, it is refused before anything is sent; edit the tags out on its card first, and the card says what
  to remove, or have the responder draft it again.

## Known issues

- The daily ceiling of automatic sends still counts its day in UTC, while the daily limits of launches now count your
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
