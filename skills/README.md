# Skills

Two packs live here, and they teach different things to the same agent.

| Pack | Teaches | Changes when |
|---|---|---|
| [`skills/runtime`](runtime/README.md) | How to operate **this runtime**: durable campaign work, what waits on a person, running the installation, why work stopped, reporting outside effects — and one skill per launched role | the Runtime API does |
| [`skills/business`](business/README.md) | The **SDR profession**, independent of any provider or runtime: the operation catalogue, planning, audiences, launch, replies, analysis, and the guardrails | the profession's practice does |

Keeping them apart is the point. Runtime skills move in lockstep with the API they print commands
against, and a test hands every command they print to this build's own argument router on every run.
Business skills are refreshed on their own cadence and are true whether or not Jason is installed at
all. A single pack would have to move at the faster of the two rates and would carry knowledge that
goes stale for a reason nobody can see.

Neither directory depends on the .NET code, has a build step, or is compiled into anything. They are
text and they are meant to stay that way.

## How skills get installed

```sh
jason skills install --dry-run
jason skills install
jason skills install --roles-only
jason skills update
```

The first prints exactly what would be written where and changes nothing; the second does it. **The
target roots are printed before anything is written, in every mode** — writing into somebody's home
directory is not a silent act.

`update` re-runs the deployment against the source and ref its own record names, which is why it takes
neither `--source` nor `--ref`: an update is the same act repeated, not a second decision about where
things come from. A verb that accepted both would let you "update" a deployment into one from somewhere
else and leave a record saying it had always been that way. With nothing to update it says so and names
`jason skills install` rather than quietly installing.

These are the rules both verbs keep:

- **Skills are pulled from git, never from the release bundle.** A release archive is frozen at a
  version; the business pack is refreshed on its own cadence, and a person who installed Jason in
  March should be able to take this month's SDR knowledge without updating the runtime. It also means
  installation works before any release exists, which today is the only way it can work at all.
- **From a pinned ref, not a moving branch.** A floating default would mean two people installing on
  the same day could get different texts, and nothing could then say whether a deployment is current.
  The pin is derived from the build rather than written down — `v` and its own release version, through
  the same rule that decides `0.1.0-dev` is older than `0.1.0` — so a development build points at the
  release it is on the way to and no literal goes stale. `--ref` overrides it, and a deployment made
  with an override says so in its record. **Until a release publishes that tag the default cannot
  resolve**, and the verb says so in those words rather than passing on a git error: install from a
  directory with `--source` until then.
- **The installed version is recorded where it was installed, and `jason status` reports it.** Each
  target directory keeps a record naming the pack, the source and ref it came from, the commit, a
  digest per file, and every path the installer wrote. That record is what says whether a deployment is
  current, and it is what an uninstall removes by — a verb that deleted by pattern would eventually
  delete somebody's own file. A root whose record cannot be read is reported `unknown` and nothing
  guesses at it. **Both roots are read**: the harness roots a person chose and the runtime's own, which
  is the half this document calls not optional.
- **Two destinations, because there are two readers.** The role halves go to the runtime's own data
  directory, where the runtime reads them at every launch to teach a role its job; the interactive
  and business packs go to the agent harnesses a person actually uses. The first is not optional: a
  runtime with no role skills launches every role untaught.
- **So the first can be deployed on its own.** `--roles-only` writes into the runtime's own directory and
  into no agent harness, and it is what `jason status` prints as the repair for `role_skills`. The harness
  half is somebody's own agent configuration, and a required check whose only repair also wrote there could
  not be repaired by anybody who would not allow that. `update` carries a harness only where its record
  says a deployment was made, so an installation taught with `--roles-only` stays that way.
- **Nothing is written until the whole tree is known to be deliverable.** The runtime refuses to launch
  a role whose skill names itself differently from its directory, or whose tree is over
  `Roles:MaxSkillBytes`. A deployment that wrote such a tree would turn a role with no skill — which
  runs untaught — into a role whose *every* launch is refused, which is strictly worse. So the install
  validates the whole source first and refuses the whole deployment, naming the skill and the reason.
  The cap comes from the running runtime, because it is a live setting; where no runtime answers, the
  documented default is used and the output says which was used, since a pass against the wrong cap is
  not a pass.
- **A role is deployed by rename.** Each skill is assembled beside its destination and moved into place,
  so a launch reading a role's tree sees the tree that was there or the tree that arrived and never a
  blend of the two. A launch that loses that race reads again; one that loses it twice is refused rather
  than run with no skill, because a role that was given a skill and did not receive it would do the job
  untaught at the price of a real launch.
- **And if a launch is reading a role's skill when the deployment reaches it, the deployment waits** — up
  to ten seconds — **and then fails for that role rather than forcing it**, leaving what was there in
  place and saying which role and why. Stop the runtime, or try again. What it will not do is write over
  a tree somebody is reading.
- **There is nothing to reload.** The runtime reads the role skills directory at every launch, so a
  deployment is live the moment it lands.
- **Installing twice costs nothing.** A skill already on disk exactly as the source has it is not written
  and not renamed, and the second run says it wrote nothing — down to the record, whose timestamp does not
  move either, because "it wrote nothing" has to mean the bytes are the bytes that were there.
- **A file you deleted is put back**, which is what a deployment being current means.
- **A file you edited is reported and kept, and nothing else is written either.** An installer that
  quietly reverts somebody's edit is a data-loss bug wearing a convenience label, and one that reverts
  half of them is worse: you would have to work out which half. `--force` is the word that says
  otherwise. A file that merely differs from the source while still matching the record is out of date
  rather than edited, and is simply written — which is what the digest per file is for.
- **And an uninstall removes by that record, or keeps what it cannot account for.** `jason uninstall`
  removes the paths the record names and nothing else, then the record, then each directory only if
  nothing is left in it — so a skill you put in the same root, and a file you added inside one of Jason's,
  both stay. A recorded file whose bytes are no longer the bytes the record names is your edit, and it is
  reported and kept unless `--force`; the record stays with it, because a receipt removed while the file
  it names is still there leaves a file nothing can account for again.
- **A deployment whose record never landed is completed rather than trusted**, and says so. The record is
  written last, so a crash half-way through reads as incomplete next time; a root that silently repaired
  itself would hide that something went wrong once.

### What a pack has to look like

- A directory of skills, each one `<name>/SKILL.md`, where the directory name and the front matter's
  `name` are the same string. A skill whose front matter names anything else is loaded by nobody and
  reported by nobody.
- Front matter of exactly `name`, a one-line `description`, and `metadata.status` — `draft` or
  `verified`. `verified` means a named agent host read that exact body while driving this runtime and
  the digest of what it read is recorded in the pack's catalog. It is a claim about evidence, and the
  guards refuse it without one.
- A `README.md` beside them that links every skill — the catalog. A guard holds both halves: every skill
  linked, and every link a skill.
- No build step and no dependency on the runtime's source.

## Installing by hand

Copying still works, and is what somebody without this build does: put a skill's directory wherever
your agent host looks for skills — for Claude Code, `~/.claude/skills/<name>/` for yourself or
`.claude/skills/<name>/` inside a project. Nothing records that you did, so `jason status` will not
report it and no uninstall will remove it.

This repository also offers both packs through the **skills marketplace** at
[`.claude-plugin/marketplace.json`](../.claude-plugin/marketplace.json), by relative path, so a clone
is installable with no network. That is a different thing from the **plugin marketplace** under
[`plugins/`](../plugins/README.md), which holds the JavaScript packages that execute provider
operations in the plugin host.
