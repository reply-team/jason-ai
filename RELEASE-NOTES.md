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

# Jason 0.21.0

## What is new since 0.20.0

- **`jason update apply` now brings the Reply plugin up to date with the release.** Until now an update left every
  plugin package where it was, so the new runtime could start with the old plugin. The update has a new step,
  `plugins`, after the new executable is in place and before any runtime starts, so the first load reads the new
  package. Only a package an earlier release placed, and that is behind this one, is replaced; a package edited or
  built on this machine is left alone. The package it replaces is kept under the data directory's
  `update/previous/plugins/<id>`, and `jason update rollback` puts it back where the place still holds the package the
  update put there. The step never fails the update: a package it cannot replace stays as it was, with a line that
  says why. `jason update check` reads the same plan, and under a package manager it names
  `jason plugin install <id> --replace` for after the upgrade.
- **`jason plugin install reply` places the Reply plugin in one command.** It downloads the package this release
  publishes, checks it against its line in `checksums.txt`, unpacks it aside, checks the result against the content
  digest the release's `manifest.json` gives, and only then moves it in whole into `~/.jason/plugins/reply`; a runtime
  that answers is asked to reload. A package that fails a check is never placed, and the verb exits 1 saying why; a
  runtime that answers and refuses the reload leaves the package placed, and the verb exits 1 as well. A different
  package already there is kept unless you add `--replace`. It never starts a runtime. Step 3 of the
  [golden path](https://github.com/reply-team/jason-ai/blob/v0.21.0/docs/golden-path.md) is now this command, in place
  of the shell lines a person used to type.
- **Homebrew follows a release on its own.** The tap is asked to move as soon as a release is published; its six-hour
  schedule stays as the fallback.
- **An email may be a template the provider fills in for each person.** A `message.send` work item takes `compose`:
  `text`, or no `compose` at all, keeps the message final, sent exactly as approved; `template` makes the subject and
  the text templates, for an email only. Every plugin reads a template by one standard, set out once in
  [the template standard](https://github.com/reply-team/jason-ai/blob/v0.21.0/docs/contracts/README.md#the-template-standard):
  a value, a value with a quoted fallback, and `#if`, `else` and `/if`, on a value or on a comparison with quoted text.
  The runtime checks a template when the work item is written, so one it refuses never waits for an approval, and the
  refusal names the construct and where it starts without quoting the template. The approval carries one example
  rendered for the person the message goes to, and `jason approval get --human` prints it under `Example:`. You
  approve the template as written; the example is Jason's rendering, and the provider's may differ. A plugin that
  cannot write a template in its provider's syntax refuses it with `template_unsupported`, and a template the provider
  could not fill in for a person is `template_unrendered`; both are final, and nothing is sent.
- **Reply 0.10.0 sends an email template in Reply's own syntax.** This release publishes the Reply plugin 0.10.0. It
  writes each construct and name of the standard as Reply writes it, and refuses before anything is called what Reply
  would read otherwise: a custom field under a key Reply keeps for a value of its own, a template whose text could
  spell a prompt to a model, a comparison with a character Reply's comparison does not read, and a template on
  LinkedIn. Reply drops a tag it does not know with everything after it, in the subject and the body alike, so a
  direct email whose subject or body holds a `<` that starts a tag, such as `<name@example.com>`, is refused before
  anything is called, in a final text and in a template; write an address without its angle brackets. A value Reply
  puts in is not escaped, so markup in a contact's field becomes markup in the mail; the
  [plugin's page](https://github.com/reply-team/jason-ai/blob/v0.21.0/plugins/reply/README.md) says how Reply reads a
  template.
- **An execution profile can grant its agents web search and web fetch, and denies them otherwise.**
  `jason profile create` and `jason profile update` take `--tool web_search` and `--tool web_fetch`; a profile's
  revisions report `tools`, and each agent attempt's provenance records the grant it was launched with. A web tool the
  profile does not grant is denied to the agent it launches. A grant of `web_fetch` reaches every address the host will
  fetch. A grant is withdrawn by an update that sends an empty `tools` list through `--file`.
- **The CLI says plainly what it will not do, and writes in your name.**
  - An option the verb does not take is refused, `'<option>' is not an option of 'jason <verb>'`, instead of being read
    as a value; a value that really starts with a dash is given after `--`.
  - An option with a fixed list of values, such as a status, an order or a kind, refuses any other value before a
    request is made, naming the values it takes.
  - A write you make without `--actor` is recorded as `human:<this account's name>`, the name the paired browser writes
    as, where it used to name nobody; the eight verbs that are a person's own word take no default, as listed under
    upgrading below.
  - `jason campaign summary --human` and `jason campaign activity --human` name each campaign.
  - `jason campaign list` run inside a project's folder lists that project's campaigns, and now says on stderr that
    `--all-projects` lists every project's.
- **A page for programs written against the Runtime API.**
  [The Runtime API](https://github.com/reply-team/jason-ai/blob/v0.21.0/docs/runtime-api.md) describes finding the
  runtime and authorising, the form of a request, every operation by noun, reading the journal and reporting an effect,
  serving pages of your own beside the UI, and which parts are stable.
- **A work item's arguments may carry the provider's own object.** Beside the arguments an operation declares, they
  may hold one reserved key, `provider`, for what one provider can do beyond the shared arguments. The runtime never
  reads it: it holds it to being a JSON object and hands it to the plugin exactly as written. A plugin that does not
  read it ignores it, and the Reply plugin does. The rule is under
  [the input a plugin receives](https://github.com/reply-team/jason-ai/blob/v0.21.0/docs/contracts/README.md#the-input-a-plugin-receives).
- **The skills say more of what a campaign needs.** An answer to a role's question is recorded as the person's, and the
  campaign manager's next check-in creates the work it implies; a campaign is never split only to give some of its
  people another tone of voice; a draft greets a person by the name in Jason's own contact record; `enrolled` in
  `jason campaign list-contacts` is membership in the Jason campaign, not an enrolment at the provider; the copywriter
  may answer with a template when one message serves a whole audience; the roles that write messages say which
  operations reach a person, `message.send`, `social_invitation.send` and `conversation.reply`, each approved with a
  preview; and the performance analysis dates each failure against where the sends are routed now, so an old failure
  is not read as a current one.
- **The release page says what this release brings,** in notes written for it, where earlier releases carried only
  the common text.

## Fixed since 0.20.0

- **A new terminal finds `jason` after `install.sh`.** The installer wrote the PATH line to the login profile only, so
  under bash or zsh a terminal that is not a login shell, such as GNOME's, did not find the program just installed. It
  now also writes it to `~/.bashrc` or `~/.zshrc`, and any other shell is told to sign out and in again. The repair
  `jason status` names and `jason uninstall` follow the same rule, and both installers end by naming `jason status`.
  On Linux, a runtime started at sign-in reads no shell profile; the
  [install guide](https://github.com/reply-team/jason-ai/blob/v0.21.0/docs/INSTALL.md) says how a directory reaches its
  PATH.
- **An update stopped part-way is said, and nothing runs around it.** Before the update moves the installed build
  aside, nothing has been replaced: `jason runtime start` starts the installed build and says that an update waits and
  that `jason update apply` resumes it, and `jason status` names the waiting update. From that step on,
  `jason runtime start` and `jason runtime restart` start nothing and say why, `jason status` fails its runtime check
  with `jason update apply` as the repair, and a runtime registered to start at sign-in starts nothing either. Before,
  each of them could start the new build outside the update and leave its last steps undone without a word. An update
  ledger that cannot be read is refused the same way, naming the file.
- **The update's own start of the runtime ends at once when that runtime exits,** where it used to wait out the whole
  minute, and says what the runtime said; `jason update apply` then resumes at that start. An apply killed during its
  health check resumes from the start of the runtime, where it used to fail every time with nothing running.
- **A role of your own with no skill is reported by `jason status`, not failed.** The check requires skills only for
  the roles Jason ships. A skill of another role that the runtime refuses still fails the check, and the message names
  the directory to correct it in.
- **`jason skills install` and `jason skills update` write around a file you edited,** file by file, instead of
  refusing the whole deployment; `--force` overwrites it. A file a later release dropped goes with its skill.
- **`jason status` reads the files a skills record names,** so a pack whose files are gone fails `harness_skills`, with
  `jason skills update` as the repair, where it used to be reported as deployed.
- **`jason uninstall` on Windows removes what other builds of Jason unpacked** beside this one, keeping and naming any
  directory a running copy holds.
- **`jason update apply --version <version>` installs that version or refuses.** A feed that offered another release
  could make it refuse for a floor that was not the one asked about, or install the version the feed named; it now
  refuses with `update_artifact_unexpected`, naming both versions.
- **A profile's program is never a script a shell would run.** A `.cmd`, `.bat` or `.ps1` could only fail later; it is
  now refused when the profile is written, and a profile stored with one is refused when its work is claimed, before
  anything is started, with a message that says how to fix the profile.
- **A move refused for want of permission fails at once.** `jason plugin install`, `jason update apply` and
  `jason update rollback` wait up to ten seconds for a file another program holds open; a move this account is not
  permitted to make now fails without that wait, and says so.
- **Pages that said what the code no longer does now say what it does:** who may clear a provider block, and what
  `jason update check` answers on 0.18.x or earlier. The `THIRD-PARTY-NOTICES` file names each runtime pack by its
  major and minor version only, since the patch the executable carries is the publishing machine's.

## Upgrading from 0.20.0

- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`; then
  `jason skills update`, `jason ui install` and `jason plugin install reply --replace`. The update from 0.20.0 is
  finished by this release's own executable, so the new `plugins` step is taken whichever build began it.
- **The data directory:** this release adds one database migration, which lets a profile revision hold the tools it
  grants. The first start on 0.21.0 runs it after taking a backup, as every migration does; revisions written before it
  grant no tools. Your projects, campaigns, work items and chronicle stay as they were.
- **The Reply plugin:** with 0.9.0, the package 0.20.0 published, installed as it came, `jason update apply` replaces
  it with 0.10.0 and keeps 0.9.0 under the data directory's `update/previous/plugins/reply`. A package edited on this
  machine is left as it is; `jason plugin install reply --replace` swaps it for 0.10.0.
- **What is refused now that was not:**
  - An option the verb does not take, and a value outside an option's fixed list, exit 2 before any request.
  - The eight verbs that are a person's own word take no default actor: `jason approval approve`,
    `jason approval reject`, `jason decision answer`, `jason preference set`, `jason preference unset`,
    `jason block clear`, `jason workitem resume` and `jason report submit`. Without `--actor` they exit 2 before any
    request, naming the form to write, where the runtime used to refuse them with exit 1 (`actor_required`, or
    `validation_failed` for `jason workitem resume`).
  - A profile whose `program` is a `.cmd`, `.bat` or `.ps1` is refused with `shell_script`; one stored before is
    refused when its work is claimed, with `host_not_available`. A profile that passes `--disallowed-tools` among its
    arguments is refused when it is written; its `deny` list is the way to deny.
  - An agent launched under a profile that grants no web tool has web search and web fetch denied. Grant them with
    `jason profile update <name> --tool web_search --tool web_fetch`.
  - Through Reply, a final text or an invitation's note that holds `@prompt[[` is refused as `message_invalid`, as a
    double opening brace already is.
  - Through Reply, a direct email whose subject or body holds a `<` followed by an ASCII letter, `/`, `!` or `?` is
    refused before anything is called, as `message_invalid` in a final text and as `template_unsupported` in a
    template, and the message names the character without quoting the text. `a < b` and `<3` go as written.
  - `jason runtime start`, `jason runtime restart` and a start at sign-in refuse with `update_in_progress` while an
    update is stopped part-way from the moment it moves the installed build aside; `jason update apply` finishes it
    and `jason update rollback` undoes it.
- **Skills:** the update redeploys them wherever a deployment was recorded; after `brew upgrade jason`, run
  `jason skills update`.

From 0.19.x the same applies, and the update is one apply. From 0.18.x or earlier, Jason's own installation updates
through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build older than 0.18.0, the
first of the two can stop at `started`, as the first known issue below says.

## Known issues

On a build older than 0.18.0, the update through 0.19.0 can stop at `started`, saying that a library failed to load.
That update is still performed by the older build, and it happens when the two builds carry different .NET patches.
Type `jason update apply` again: it runs 0.19.0, which finishes the update, and one more `jason update apply` then
takes the newest release. From 0.18.x this does not happen: 0.19.0 is built on the same .NET patch as 0.18.0.

Through Reply, Jason sees only a contact's first reply. Reply marks a contact who replied as finished and stops
detecting that contact's later replies, so a later reply from the same contact never reaches Jason.

A send or an invitation through Reply carries no sending time: `sent_at` is null, because Reply's answer to a LinkedIn
send gives a local time marked as UTC and its answer to an email gives none. A message's own moment is read afterwards
from its thread, where it is true UTC.

Through Reply, `schedule.list` does not say which campaign runs on which schedule: `campaign.get` names no schedule,
so which one a campaign sends on is read at Reply.

Through Reply, a reply on a thread, `conversation.reply`, is not held to the rule on a `<` that starts a tag yet, and
its text goes as written; whether Reply cuts a reply as it cuts a direct email has not been seen, so write an address
in a reply without its angle brackets.
