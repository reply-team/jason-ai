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


# Jason 0.29.0

## What is new since 0.28.0

The runtime's journal is now a bus for plugins: a plugin that a person grants journal kinds is handed each line of those
kinds after the runtime wrote it, and may act back only through the public Runtime API and only as far as the person
granted.

- **The journal's kinds are published.** Every kind the runtime reserves for its own lines has one document under
  [`docs/contracts/journal/`](https://github.com/reply-team/jason-ai/blob/v0.29.0/docs/contracts/journal), saying what a
  line of the kind records, which things it names and whether a plugin may subscribe to it, and every line the runtime
  writes is held to its document as it is written. `jason journal kinds` reads them, a page at a time, in kind order.
  [The contracts page](https://github.com/reply-team/jason-ai/blob/v0.29.0/docs/contracts/README.md#the-journals-kinds)
  states the rules a document follows.
- **A plugin subscribes by declaration and grant.** A manifest lists the kinds it wants under `subscriptions`; you admit
  it to some or all of them in `Plugins:Grants:<id>:Subscriptions` of `settings.json`, and `*` admits every kind it
  declares. Where several plugins are admitted to one kind, they run one after another, in plugin-id order unless
  `Subscriptions:Order:<kind>` lists some of them first. Grants and orders are frozen at each load, so a change takes
  effect at the next `jason plugin reload`. A plugin that does only this is the new plugin kind `subscriber`, which
  implements no operation and delivers no notification; a provider or a notification plugin may subscribe as well.
  [The plugin guide](https://github.com/reply-team/jason-ai/blob/v0.29.0/docs/plugins.md#16-subscriptions-declaration-grant-order)
  has the manifest, the grant and the order.
- **What a handler is handed, and how it runs.** The runtime hands a handler one line of a kind at a time, in the order
  the journal holds them, with the envelope the handler before it in the chain returned; the handler answers an
  envelope, which goes on to the next handler. The runtime never reads what an envelope means. A transient failure is
  tried again, up to `Subscriptions:TransientRetries` times, after a wait of `Subscriptions:RetryBackoffSeconds` that
  doubles; any other failure ends the chain at that handler for that line, writes one `subscription_failed` line naming
  the kind, the line, the plugin, the class and the code, and the kind moves on. What a handler does is never handed to
  another handler as a new line. Delivery is at least once: a line may be handed twice, after a restart in the middle of
  a chain too, and a handler tells a repeat by `attempt` in its input, which counts how many times that plugin has been
  handed the line, this time included. The numbers under `Subscriptions` apply live. See
  [the plugin guide](https://github.com/reply-team/jason-ai/blob/v0.29.0/docs/plugins.md#17-writing-a-subscriber).
- **A handler reads and writes Jason through `host.call`.** A handler of a plugin you granted a Runtime API family calls
  Runtime API operations through a door the runtime opens for that one invocation, about that one line. The families
  are the ones the manifest's `capabilities.api` requested and you listed in the plugin's `Api` list under
  `Plugins:Grants`: `read`, `campaign`, `contact`, `workitem`, `suppression`, `report` and `journal.append`. It acts as
  the plugin, never as a person, with the line as the reason, so the chronicle names the plugin and the line. The
  installation's own doors, a person's decisions and an executor's verbs are in no family, so no grant opens them.
  [The guide's capabilities section](https://github.com/reply-team/jason-ai/blob/v0.29.0/docs/plugins.md#9-capabilities-and-grants)
  lists them.
- **`jason subscription list`, `runs` and `rewind`.** `list` shows, for each subscribed kind, the plugins of its chain,
  where its position stands, the journal's last line of the kind, the lag between the two, and the members the current
  load cannot run. `runs --kind <kind>` pages the records of that kind's chains, newest first, each handler's outcome
  included. `rewind --kind <kind>` with `--line <id>` or `--since <moment>` moves the position back so the kind's lines
  are handed again; only a person may, so it takes `--actor human:<your-name>`, it refuses a target ahead of the
  position, and each rewind writes one `subscription_cursor_rewound` line. `jason doctor` gains a `subscriptions`
  section with where each kind stands and how its newest chains ended, and it never carries an envelope.
- **The `journal-forwarder` plugin pushes each granted line to one URL.** It is a reference subscriber: for each line of
  a kind you granted, it POSTs the line, the envelope and the attempt as JSON to the URL named in
  `JOURNAL_FORWARDER_URL`, and answers the envelope as it was handed. It is in the repository's plugin marketplace under
  [`plugins/journal-forwarder`](https://github.com/reply-team/jason-ai/blob/v0.29.0/plugins/journal-forwarder/README.md),
  not a release asset: you copy the package into `~/.jason/plugins`, as its README says. What leaves the machine is what
  the journal says, which includes names, fields as they changed and the reasons people typed, so the receiver is a
  place you trust with that. Its manifest names `localhost:8787`; a receiver elsewhere is your edit of the package, as
  the README says, and the copy is yours to keep current.
- **Two skills teach the new verbs.** `operating-the-installation` and `troubleshooting-jason` now teach
  `jason journal kinds` and `jason subscription`, and both are `draft` again until a host reads their new text. The
  skills marked `draft` in this release are those two, `researcher-in-session`, `roles/responder` and the nine skills of
  the business pack.

## Upgrading from 0.28.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. The
  install line and Homebrew place the executable alone, so after either, `jason skills update`, `jason ui install`
  and `jason plugin install reply --replace` follow.
- **Skills:** the update redeploys them wherever a deployment was recorded; after the install line or
  `brew upgrade jason`, run `jason skills update`. Two skills changed their text and their status line is `draft`
  again.
- **The data directory:** this release adds one database migration, `SubscriptionRunner`, which adds the tables that
  hold each subscribed kind's position and the records of its chains. The first start on 0.29.0 runs it after taking a
  backup, as every migration does. Your projects, campaigns, work items and chronicle stay as they were.
- **Nothing subscribes until you say so.** No plugin is handed a journal line until you install a subscriber and grant it
  kinds, so an upgrade with no subscriber changes no behaviour.
- **The Reply plugin** is unchanged.
- **From 0.27.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the known issue about that update below says.

## Known issues

`reply skills install --project` does not see an old `reply-adapter@reply-skills` plugin installed at user scope on
Claude Code, so that plugin keeps loading beside the project copy of the skills.

`reply skills install --dry-run` and `reply skills update --dry-run` describe what they would do in the past tense, as
if it had been done; nothing is changed.

The click on a notification and its Copy prompt button are Windows only. Where another program already holds `jason:` links,
Jason leaves that registration alone and the toast shows without a click or a button.

On Windows, `jason session open` starts the console host, which hands the window to Windows Terminal where that is
your default terminal; where your default terminal is the console host itself, you get a plain console window; neither
is yet measured on a machine.

The macOS form of `jason session open`, a script opened in Terminal, has not been watched on a Mac.

Antigravity CLI's hook route has not been run on an account. The host also reads your own global `AGENTS.md` and
`GEMINI.md` under `~/.gemini` for every role.

Two installations on one account, a build of a checkout and the installed one, take the `jason:` link from each other at
each toast, since the registration rewrites a key that is Jason's and names another executable. Uninstall leaves a key
that names another installation alone, so nothing is lost.

On Linux, kitty, foot, gnome-terminal, xfce4-terminal, mate-terminal and wezterm are started in the form each takes;
any other terminal program is given the host after `-e` as separate arguments.

Under Windows PowerShell 5.1 and cmd.exe the inner quotes of `--value '{"host":"codex"}'` are lost, so give the value in a file:
`jason preference set agent.interactive_host --value-file host.json --actor human:<your-name>`.

If Claude Code's npm postinstall never replaced its placeholder executable, `jason session open` reports that it started the host
and the window closes at once.

Codex CLI on Windows needs its elevated sandbox set up once by an administrator before a role can run a command; until
then every command a role runs is refused, so the role cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal; Jason does not check for the
setup when the attempt starts.

Whether `jason` reaches the runtime from inside Codex's sandbox has not been checked on Linux, macOS or Windows, and on
Windows it is not yet checked whether the elevated sandbox reads only the list Jason names. Codex also reads its own global
`AGENTS.md` and offers every role the skills in `~/.agents/skills`, whatever the profile says, so those two reach every
Codex role. Holding Codex's network to loopback rests on its network proxy, an experimental feature in 0.166.0.

Whether Cursor CLI runs the allow-listed `jason` command without asking, in print mode, has not been seen on a signed-in
run. If it does not, a role cannot call the runtime back, so it cannot report, and the attempt ends as `executor_exited` and is retried against the same refusal. Cursor CLI also reads more than the
memory files Jason names, since it takes the union of every configuration layer it finds, yours included: your own allow
rules of any kind, a shell command or a write among them, stand beside Jason's. Each attempt leaves one trust marker under
`~/.cursor` that Jason does not remove.

Gemini CLI reads the whole folder of each level a role's memory files are in, and the client level's folder is the harness
root, so a role reads every memory file under it. A role on Gemini CLI is also refused when the host's system policy folder
holds a `.toml` file, since the host would then ignore the policy Jason gives it.
Gemini CLI's `--session-id` and `--admin-policy`, which Jason passes, are in the 0.66.0 source and not in its options table.
Gemini CLI also reads your own `~/.gemini/GEMINI.md`, `includeDirectories` and skills for every role.

GitHub Copilot CLI's reads outside the work directory, which a role needs for its memory files, are documented but have not
been seen to reach there.
Whether Copilot CLI 1.0.105 accepts every flag Jason composes is not said by its changelog, so its launch is composed from the
reference and not from a run.
A role on Copilot CLI runs under your own user-level hooks, and a `COPILOT_ALLOW_ALL` in your environment reaches it as the
host's own allow-all, minus what the deny list names.

A proxy flag or a proxy variable is yours to set: Jason sets none of them and reserves none of them. A flag that names a
host's own server, its credentials or its certificate check is reserved where the host has one, as the Cursor CLI section
says, and a host whose arguments are an allowlist takes no flag outside it.

On a build older than 0.18.0, the update through 0.19.0 can stop at `started`, saying that a library failed to load.
That update is still performed by the older build, and it happens when the two builds carry different .NET patches.
Type `jason update apply` again: it runs 0.19.0, which finishes the update, and one more `jason update apply` then
takes the newest release. From 0.18.x this does not happen: 0.19.0 is built on the same .NET patch as 0.18.0.

Reply's routes behind 0.22.0's send features - a send's moment and its sending schedule, reading and cancelling a send
by its reference, and the inbox change feed - reach Reply's accounts on Reply's own schedule. Until an account's Reply
has a route, the Reply package answers `capability_not_offered` for that operation or argument, and the account's
replies are read thread by thread in place of the feed, as its README says; nothing in Jason needs updating when the
route arrives.

For a sequence email, Reply keeps at most one reply to each email it sent and drops a finished contact's later replies
on that thread before it stores them, so they reach none of `conversation.list`, `conversation.get` and the change
feed, and Jason's inbound sync says nothing of them. After this side has answered on the thread through
`conversation.reply`, the contact's next replies on it are stored and read as usual; a second reply that arrives
before any answer may never be seen. A LinkedIn thread keeps later messages. All of this is read from how Reply is built
and has not yet been observed on an account.

A send or an invitation through Reply carries no sending time in its own result: `sent_at` is null, because Reply's
answer to a LinkedIn send gives a local time marked as UTC and its answer to an email gives none. `message.status`
reports when a send went, and a message's own moment is read from its thread, where it is true UTC.

Through Reply, a sending schedule times a LinkedIn message only once the account's Reply times deferred LinkedIn sends
itself. Until then the Reply package answers a LinkedIn message that names one `capability_not_offered`, and nothing is
sent, whether the account's Reply has no direct sends yet or has them without a schedule on LinkedIn.

The approval preview shows a send's `send_after` and not the provider object beside the arguments, so a send that also
names a sending schedule goes at the schedule's first opening at or after that moment, which may be later than the
moment the approver read.

A template held for a later moment, or queued by Reply behind the account's other sends, is filled in when it goes: a
name Reply cannot fill then fails the send after a person approved it, which `message.status` reads as `failed` with
Reply's reason.

Reply converts a LinkedIn text from HTML to text after filling it in, so markup in a LinkedIn message or InMail, a
final text or a template, does not reach the person as written; what Reply makes of a `<` that starts a tag there is
not known yet, and the Reply package does not refuse one on LinkedIn as it does on email.

Reply does not consult its blacklist when it takes a LinkedIn direct send, and does when it makes it: a send to a
person its blacklist holds is accepted, and fails when Reply makes it, which `message.status` reads as `failed` with
Reply's reason. Jason's own do-not-contact register is checked at the claim as before; record such a stop there with
`jason suppression add` as well, so the send is refused before it is made.

Through Reply, `schedule.list` does not say which campaign runs on which schedule: `campaign.get` names no schedule,
so which one a campaign sends on is read at Reply.

Through Reply, a reply on a thread, `conversation.reply`, is not held to the rule on a `<` that starts a tag yet, and
its text goes as written; whether Reply cuts a reply as it cuts a direct email has not been seen, so write an address
in a reply without its angle brackets.

The responder's skill ships marked `draft`: no agent host has read its text in the state it ships in. Its commands parse
against this build, as every skill's do, but how an agent follows its loop on a real reply is not yet seen.

A fault of the runtime's own that stops a subscribed kind's turn is logged, and the kind is offered again at the next
scan with no pause, from the same line, so a fault that lasts repeats at every scan until it is gone.
