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

# Jason 0.34.0

## What is new since 0.33.0

A campaign can be filled from a description of who it is for. The description is written in the provider's own terms;
the provider says, for free, how many people it matches and shows a few of them; a search for some of them waits for a
person, then runs at the provider; and Jason follows the search by itself and makes the people it finds the campaign's
members as they arrive. A threshold says when the campaign is running low on people nobody has started on, and an
allowance a person gives lets the searches that top up an audience go without asking, up to a number of people. The
Reply package performs all of it through Reply's Live Data.

- **An audience is described in the provider's terms.** Six operations make a new family: `audience.preview`,
  `audience.search`, `audience.search_status`, `audience.search_cancel`, `audience.filter_values` and
  `audience.availability`, thirty operations in all. An audience's definition is the provider's own shape (which
  industries, which titles, which company sizes, as the provider names them), and a plugin that searches declares it as
  a schema. `jason operation describe audience.search --campaign <campaign-id>` prints what the plugin a campaign is
  routed to takes, each filter with its description. See
  [the audience family](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/contracts/README.md#the-audience-family).
- **A preview before anything is spent.** `jason audience preview --campaign <campaign-id> --definition '<json>'`
  answers how many people and companies a definition matches and a sample of at most 25 of them, with no way to reach
  them; `--audience <n>` previews an audience the campaign already searched under. `jason audience filter-values
  --campaign <campaign-id> --filter <name> --query <text>` spells a filter's values as the provider does, and
  `jason audience availability --campaign <campaign-id>` says whether the account may search now. The three are the
  provider's reads, asked through the campaign's route when a person or an agent asks: nothing is recorded, journaled or
  pinned, a preview's definition is held to the plugin's schema first, and a preview costs nothing by its contract.
- **A search is work that waits for a person.** `audience.search` is a work item whose contract asks a person once:
  `jason workitem create <campaign-id> --kind provider_op --operation audience.search --input
  '{"definition":{…},"count":25}'` parks it for approval. It asks for 1 to 1000 people, one per company unless
  `people_per_company` says more, and names either a definition or, for a top-up, the number of an audience the campaign
  holds: `{"audience":1,"count":25}`. The claim holds the definition to the routed plugin's schema
  (`definition_invalid`), refuses a number the campaign holds no audience under (`audience_unknown`), and checks the
  search a status read or a stop names (`search_unknown`, `search_reference_absent`, `search_route_mismatch`): the claim
  now makes twenty-five checks, in one published order
  ([routing](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/routing.md)).
- **Audiences, numbered.** The first search the claim lets through under a definition, to run or to wait for a person,
  makes the campaign an audience: an `aud_` id, a number 1, 2, 3 within the campaign, and the definition as the claim
  held it. The same definition with its members in another order is the same audience, and a definition searched before
  finds its old number. The claim records the audience on the search under the context key `context.audience`, which no
  one else may write on a search. See
  [the audiences](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/campaign-manager.md#10-the-audiences).
- **The people a search finds become the campaign's members, page by page.** A search the provider takes ends its work
  item `accepted`. From then on a new step of the dispatcher's scan, the poll, reads it with `audience.search_status`
  every `Dispatcher:SearchCadenceSeconds` (300 by default), at once while the provider says more people are readable,
  and less often after reads that failed. Each page lands in the change set that ends its read: a person is found by the
  provider's identifier pinned at the plugin, then by each address, then by each LinkedIn profile; a person found nowhere
  becomes a contact, with what a contact has no field for under `custom`; and each becomes a member, except a person the
  campaign already holds, who stays the one member they are, and one removed from it, who stays removed. Membership is
  local: nothing is enrolled at the provider. In `jason campaign list-contacts`, a member a search brought in carries a
  `source`: the audience, the search and when they landed. A search whose reads land nobody for
  `Dispatcher:SearchStallSeconds` (3600 by default) ends `stalled` and is read no more. The scan now runs eleven steps.
  See [following a search](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/campaign-manager.md#following-a-search-and-the-people-it-lands).
- **A search can be stopped.** `audience.search_cancel` stops a search at the provider, and the people it already
  landed stay the campaign's. Like a pause or an opt-out it runs in a paused campaign, and like taking back a send it
  runs through a provider block; a stop that fails is notified as a protective action that failed.
- **The chronicle says how a search went.** Four new kinds, written by the dispatcher, facts only:
  `audience_people_landed`, for each page that carried anybody; `audience_search_ended`, once per search, as `finished`,
  `exhausted`, `stalled` or `cancelled`; and `audience_reserve_low` and `audience_reserve_recovered`, below.
  `audience_search_ended` and `audience_reserve_low` are new default triggers of the campaign manager.
- **The reserve.** `jason campaign update <campaign-id> --audience-threshold 50` says how many people nobody has started
  on the campaign wants to keep, from 1 to 100000, and `--clear audience_threshold` takes it away. A step of the scan
  counts them whenever something that can change the number was written, and says once per crossing that the reserve
  fell under the threshold or recovered, so the manager is woken when the campaign runs low; it creates no work itself.
  The count is `jason campaign summary`'s `new`, which now leaves out the work the runtime made about a person by itself,
  the read of a thread that moved and the responder it woke: a member whose only work is that is now counted as `new`.
  See [the reserve](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/campaign-manager.md#9-the-reserve).
- **The allowance: a yes in advance for top-ups.** A search spends the provider's credits, so a new audience's first
  search, its probe, always waits for a person. After it, a named person can let the searches under that audience go
  without asking until the people they ask for reach a ceiling, in the act of approving,
  `jason approval approve <approval-id> --allow 250 --allow-per-search 25 --actor human:<name>`, or on its own,
  `jason approval set-standing <campaign-id> --operation audience.search --audience 1 --max-people 250 --max-per-search 25
  --actor human:<name>`; `--allow-expires-at` and `--expires-at` set an end, and `jason approval unset-standing
  <campaign-id> --operation audience.search` takes it back. A campaign holds one allowance, and approving a search under
  another audience retires it in the same act. A search it does not cover parks, and its preview says why:
  `no_standing_approval`, `another_audience`, `expired`, `over_per_search` or `ceiling_reached`. A search it lets go
  runs under an approval decided by the person who set it, with an `approval_standing_applied` line, and
  `jason approval list-standing` shows the searches it let go and the people they asked for. See
  [standing approvals](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/work-execution.md#standing-approvals).
- **Where a campaign's audience stands.** `jason campaign audience <campaign-id>` answers the reserve's count and
  threshold, the allowance and how much of it is used, and the campaign's audiences, the newest first, each with its
  definition, how many people its searches landed, and its latest searches (ten unless `--search-limit` says up to a
  hundred); `--human` lays each definition out by the plugin's schema. `--audience <n>` on `jason campaign summary`,
  `activity`, `activity-by-period` and `list-contacts` reads only the people a search landed under that audience, so one
  audience is judged on its own. See
  [reading the audiences](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/campaign-manager.md#reading-the-audiences).
- **The roles learn the work.** The planner forms an audience: it reads the definition the route's plugin takes, spells
  values with the typeahead, previews and reads the sample, and creates the probe, sized by the preview's count and never
  the maximum, with its proposal for a threshold and an allowance, neither of which it sets. The manager runs the pulse:
  it reads how a search ended, has the analyst judge a probe's people once the probe's window is over, and tops up an
  audience that works by its number, under the allowance. The analyst judges one audience's own people and answers
  `working`, `replace` or `stop`. `approvals-and-questions`, `managed-campaign-work`, `approval-boundaries` and
  `inbox-triage` teach the allowance beside the standing approvals for replies; in the business pack,
  `audience-building` gains outward search with no provider named, and `sdr-operations` maps the family onto its
  catalogue. Each of these ships as a draft (below).
- **For plugins.** Six contract documents under `docs/contracts/operations/`, with their fixtures under
  `docs/contracts/fixtures/`. A provider that performs `audience.search` or `audience.preview` declares
  `audience_definition` in its manifest: a `schema` in the contracts' dialect, every property described, and an optional
  `keys`, which gives a property a `side` and a `typeahead`. One that performs
  `audience.search` performs `audience.search_status` too, or the load refuses it with the new problem code
  `audience_family_incomplete`. The runtime composes `definition` at the input's root for a search and a preview, and
  `search` (the search's work item, your reference and, for a status read, your cursor) for a status read and a stop;
  `audience.search_status` is the first operation paged by a cursor, which is yours and never parsed. The `read` family
  a plugin may be granted now covers `operation.describe` and `campaign.audience`. See
  [plugins](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/plugins.md).
- **The Reply package performs the family over Reply's Live Data**, every one of the thirty operations, and needs
  reply-cli 0.20.0 or newer. A search makes a private contact list of its own, starts Reply's search into it and answers
  with a reference; a status read pages through that list, and the read that returns the last person of a search that
  ended deletes it; a stop deletes the search and its list. Reply charges for each person a search finds; a preview is
  neither saved nor charged. `audience.availability` answers that the account may search and nothing more, since Reply
  has no read of an account's credits, and a search for people without an email (`include_without_email`) is refused.
  Read [what it cannot do](https://github.com/reply-team/jason-ai/blob/v0.34.0/plugins/reply/README.md#what-it-cannot-do)
  before relying on a search; the known issues below carry the two that matter most.
- **The journal forwarder 1.4.0** declares the four new kinds, so a person can grant them.
- **Not yet.** Nothing stops a campaign from running two searches at once; a provider may refuse the second itself
  (`search_in_progress`). The browser UI approves or rejects a search, and shows neither the planner's proposal nor why a
  search parked; allowing more people is the CLI's, and so are the audiences, the threshold and the allowance, which no
  screen shows yet. A probe still waiting for a person has no row in `jason campaign audience`: `jason approval list`
  shows it.

## Upgrading from 0.33.0

Nothing stops: no setting is renamed, and no start refuses a setting 0.33.0 took.

- **reply-cli first, if you use Reply.** The Reply package 0.15.2 needs reply-cli 0.20.0 or newer, and
  `jason update apply` replaces an installed Reply package you did not edit. With an older reply-cli the plugin is
  `unavailable` (`executable_incompatible`), and every work item routed to it, a send included, fails at the claim with
  `plugin_unavailable` until reply-cli is updated and the plugins are reloaded (`jason plugin reload`).
- **The executable:** `jason update apply`, or run the install line again. Installed with Homebrew, it is the three
  lines `jason update apply` prints instead: `jason runtime stop`, `brew upgrade jason`, `jason runtime start`. After the
  install line or Homebrew, `jason skills update` and `jason ui install` follow, and `jason plugin install <id> --replace`
  for each package you installed and did not edit.
- **The packages:** reply 0.15.2 (0.14.6 in 0.33.0) and journal-forwarder 1.4.0 (1.2.0); ntfy 1.0.3, slack 1.0.0 and
  telegram 1.0.0 as in 0.33.0. A release is fourteen files, as in 0.33.0.
- **The data directory:** one migration, applied at the first start after the backup every migration takes: two columns
  on `campaigns`, the reserve's threshold and the reserve step's place in the chronicle; three new tables, `audiences`,
  `audience_searches` and `audience_landings`; and three indexes on the journal for the new kinds. Nothing already
  there is rewritten.
- **A narrowed `Manager:Triggers`** keeps your choice and does not gain `audience_reserve_low` or
  `audience_search_ended`; add them if the audience should wake the manager.
- **`jason approval set-standing` and `unset-standing` without `--category`** are now the runtime's refusal, exit code
  1, where 0.33.0 refused them as a usage error, exit code 2: with `--operation audience.search` they set and take back
  the allowance, which takes no category.
- **Skills:** the update redeploys them wherever a deployment was recorded. Eleven skills changed their text, and each
  is `draft`: no agent host has read it against 0.34.0. Six of them were `verified` in 0.33.0: `approvals-and-questions`,
  `managed-campaign-work`, `planner-in-session` and the `planner`, `manager` and `analyst` roles. The other five were
  drafts already: `troubleshooting-jason`, which now names a stop of a search among the work a paused campaign lets
  through, and `approval-boundaries`, `audience-building`, `inbox-triage` and `sdr-operations` of the business pack. The
  skills marked `draft` are those eleven, `operating-the-installation`, `researcher-in-session`, the `responder` role and
  the other five skills of the business pack; `reporting-outside-effects` and the `researcher`, `copywriter`,
  `personalizer`, `critic` and `deliverability-specialist` roles stay `verified`.
- **Going back:** an apply of 0.33.0 leaves a newer package where it is, and 0.33.0 refuses both packages this release
  changed: Reply 0.15.2, whose `audience_definition` it does not read, and the forwarder 1.4.0, whose kinds it does not
  write. A package problem rejects the whole load, so the runtime starts with no plugin loaded at all until each
  is put back with `jason plugin install <id> --replace`, run just after the apply. The older executable opens the
  database as this one migrated it, which it may refuse to do. See
  [Applying an older release](https://github.com/reply-team/jason-ai/blob/v0.34.0/docs/release-and-update.md#applying-an-older-release).
- **From 0.32.x or earlier,** the notes of each release in between apply as well. From 0.18.x or earlier, Jason's own
  installation updates through 0.19.0: `jason update apply --version 0.19.0`, then `jason update apply`; from a build
  older than 0.18.0, the first of the two can stop at `started`, as the known issue about that update below says.

## Known issues

Through Reply, a person the account already holds is never found by `audience.search`, however well they match, while
`audience.preview` counts and samples them, so a search can find fewer people than its preview said. A person the
campaign holds and Reply does not, one brought from a file say, can be found by a search again and is charged for; Jason
keeps them the one member they are.

A person the campaign holds from elsewhere, found by a search under another address, with no LinkedIn profile the two
share and no identifier of that provider pinned, becomes a second contact: a name never matches anybody.

Two campaigns landing the same new person at the same moment can each make a contact for them.

A search of a campaign archived while the search ran is never ended: the poll reads no archived campaign's searches, so
it stays without an ending.

Through Reply, the read that returns the last person of a search that ended deletes the search's list, so if that read's
answer is lost before its people land, they cannot be read again. A stop deletes the list too: the people Reply found
since the last read are charged, stay Reply's contacts and never land in the campaign.

A search a person resumes at the provider after it ended `stalled` is not followed by the poll again; a read anybody
makes still lands its page: `jason workitem create <campaign-id> --kind provider_op --operation audience.search_status
--input '{"search":"<the search's work item>","limit":100}'`.

A threshold set while nobody is on the campaign says at once that the reserve is low, since a count of nobody is under
any threshold, and wakes the manager for nothing. Set it once a search's people have landed.

`jason approval get --human` does not show why a search parked; the preview's `standing_approval` is in the JSON.

An import on the same machine, into a second data directory, "needs the source harness root moved aside first: the
records name its folders, and project.link refuses a folder present elsewhere (harness_folder_present)". The import
names each refused link, and its `next` carries `jason project link <id> --path <folder>` for each.

`jason status` calls autostart `ok` from the registration alone: a registration whose executable is not there any more
reads as registered. `jason runtime autostart status` reads the executable it names, and says "The executable it runs
is not there any more" with its path.

On Windows, a campaign's move to another project can be refused for the instant the background import-file sweep writes
inside that folder, soon after a start; ask again and it succeeds.

`reply skills install --project` does not see an old `reply-adapter` plugin from the `reply-skills` marketplace
installed at user scope on
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

Through Reply, a contact's later replies by email can be lost: a lost reply reaches none of `conversation.list`,
`conversation.get` and the change feed, and Jason's inbound sync says nothing of it. A direct email's replies are all
detected, any number, a reply after an answer sent from Reply's inbox included. A second reply to a sequence email is
not detected: Reply takes the first reply to each email it sent. After an answer sent from Reply's inbox on a sequence
thread, a reply is detected only when its own headers reference that answer, and a reply to the original sequence email
is dropped. No auto-reply or out-of-office is stored on a direct thread; a reply whose `In-Reply-To` and `References`
are both empty is dropped; and a reply to a send older than 40 days is not matched unless a newer send to that address
exists. A LinkedIn thread keeps later messages, so they are read. That a direct email's replies are all detected was
observed on an account; the rest, the LinkedIn statement among them, is read from how Reply is built and has not been
observed.

A send or an invitation through Reply carries no sending time in its own result: `sent_at` is null, because Reply's
answer to a LinkedIn send gives a local time marked as UTC and its answer to an email gives none. `message.status`
reports when a send went, and a message's own moment is read from its thread, where it is true UTC.

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
