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


# Jason 0.24.0

## What is new since 0.23.0

- **This is the first release built and published by this repository.** Until now Jason was developed in a private
  repository, and each release published a copy of its open part here. From 0.24.0 this repository holds the whole
  product, the runtime's source included, with its history, and a tag pushed here builds, signs and publishes its
  release at once. Issues and Discussions are open. A commit made before 0.24.0 whose subject ends in "(#N)" names a pull
  request of the repository Jason was developed in until now, and the tags `v0.1.0` to `v0.23.0` point at snapshots of
  what was published here at those versions; [the development guide](https://github.com/reply-team/jason-ai/blob/v0.24.0/DEVELOPMENT.md) says both.
- **The runtime is released under the MIT licence, like everything else here.** The repository, its history
  included, is MIT from 0.24.0, and every archive carries that [`LICENSE`](https://github.com/reply-team/jason-ai/blob/v0.24.0/LICENSE) beside the executable and the
  third-party notices. The source of every earlier version is MIT as well, since the history is, and the licence files
  in older trees are superseded; the archives published before 0.24.0 carry the licence they were published with,
  which the install scripts still print for 0.6.0 to 0.23.x. What the earlier
  licence said about what the runtime sends off a machine and what stays your responsibility is kept, in its words and
  as statements about the product rather than terms to accept, on [a page of its own](https://github.com/reply-team/jason-ai/blob/v0.24.0/docs/responsible-use.md).
- **The C# runtime now lives under `dotnet/`,** so that the repository's root stays free for the port of the runtime to
  Rust, which will be developed here. The move changes no behaviour: the executable a release builds is the one it was.
  Until the port is complete, the C# runtime is the one released.
- **The product's address is `get.jasonharness.ai`.** The install lines, the update check and every download of this
  release use it. An installation from 0.7.0 to 0.23.x keeps updating through the earlier address, which still
  redirects the same paths; one older than 0.7.0 asks GitHub directly. [The release and update page](https://github.com/reply-team/jason-ai/blob/v0.24.0/docs/release-and-update.md) says which address each
  installation asks.
- **A problem reaches an issue with the file `jason doctor` wrote.** The repository has an issue form for a problem.
  After a plain `jason doctor` writes its file, it prints the form's address, with the version and the platform filled
  in: in its result as `problem_report_url`, on stderr, and with `--human` on a line of its own. Opening the issue and
  attaching the file is yours to do, and nothing is sent. Because that file may now go to a public issue, a default
  file names nothing you chose by its own name. A plugin, role or skill pack Jason does not ship travels as an alias,
  such as `<plugin-1>`, `<role-1>` or `<skill-pack-1>`, one per name. As the home directory is `~`, the harness root is
  written as `<harness_root>`, a data directory other than `~/.jason` as `<data>`, and the executable's folder outside
  the home directory as `<install_dir>` unless a package manager's prefix holds it. A skill source other than this
  repository is `<skill-source>`, and so is its ref unless that is a release tag. A file made with `--include-journal`,
  which carries what the journal's entries say, names no issue form: its `problem_report_url` is empty, and its warnings
  say it goes only to the support address under Getting help, never to an issue. [Getting help](https://github.com/reply-team/jason-ai/blob/v0.24.0/README.md#getting-help) and [the contributing page](https://github.com/reply-team/jason-ai/blob/v0.24.0/CONTRIBUTING.md)
  say where each kind of message goes; the troubleshooting skill offers a person the form's address and leaves opening
  the issue to them.

## Upgrading from 0.23.0

- **Nothing changes for an installation.** The update path, the feed and the commands are as they were, and this release
  adds no database migration: your projects, campaigns, work items and chronicle stay as they were. Run
  `jason update apply`, or the install line again; installed with Homebrew, `jason update apply` prints the three lines
  to run instead. The install line and Homebrew place the executable alone, so after either, `jason skills update`,
  `jason ui install` and `jason plugin install reply --replace` follow.
- **The licence beside the executable becomes MIT.** `jason update apply` and the install line replace the executable's
  notice files, `jason-LICENSE` and `jason-THIRD-PARTY-NOTICES`, with this release's. Each install script ends with the
  licence line of the version it installed: MIT for 0.24.0 and later, and for 0.6.0 to 0.23.x the earlier licence, with
  the link to that tag's file.
- **The Reply plugin** is published as 0.14.3, the package 0.23.0 published, and it needs reply-cli 0.18.0 or newer.
  From 0.22.0, `jason update apply` replaces the package as it came and keeps the one it replaced under the data
  directory's `update/previous/plugins/reply`; a package edited on this machine is left as it is, and
  `jason plugin install reply --replace` swaps it. Under reply-cli 0.17.2 or older the plugin is `unavailable` with
  `executable_incompatible`, and work routed to it fails at the claim with `plugin_unavailable`; update reply-cli with
  `npm install -g reply-cli`, then run `jason plugin reload`.
- **Should the old address ever stop redirecting,** one act moves an installation from 0.7.0 to 0.23.x:
  `jason update apply --feed https://get.jasonharness.ai/releases/latest/download/manifest.json`, or a reinstall with
  the current one-liner. The executable that arrives asks `get.jasonharness.ai` from then on. `Update:FeedUrl` alone
  moves only the running runtime's own check, not `jason update`, `jason ui install` or `jason plugin install`.
- **From 0.22.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the first known issue below says.

## Known issues

On a build older than 0.18.0, the update through 0.19.0 can stop at `started`, saying that a library failed to load.
That update is still performed by the older build, and it happens when the two builds carry different .NET patches.
Type `jason update apply` again: it runs 0.19.0, which finishes the update, and one more `jason update apply` then
takes the newest release. From 0.18.x this does not happen: 0.19.0 is built on the same .NET patch as 0.18.0.

Reply's routes behind 0.22.0's send features - a send's moment and its sending schedule, reading and cancelling a send
by its reference, and the inbox change feed - reach Reply's accounts on Reply's own schedule. Until an account's Reply
has a route, the Reply package answers `capability_not_offered` for that operation or argument, and the account's
replies are read thread by thread in place of the feed, as its README says; nothing in Jason needs updating when the
route arrives.

Through Reply, Jason sees only a contact's first reply. Reply marks a contact who replied as finished and stops
detecting that contact's later replies, so a later reply from the same contact never reaches Jason.

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
