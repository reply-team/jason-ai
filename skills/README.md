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

**`jason skills install` does not ship in this build yet; it arrives with the first-run work.** This
section is the contract it implements, written before it exists so that the rule is set rather than
discovered:

- **Skills are pulled from git, never from the release bundle.** A release archive is frozen at a
  version; the business pack is refreshed on its own cadence, and a person who installed Jason in
  March should be able to take this month's SDR knowledge without updating the runtime. It also means
  installation works before any release exists, which today is the only way it can work at all.
- **From a pinned ref, not a moving branch.** A floating default would mean two people installing on
  the same day could get different texts, and nothing could then say whether a deployment is current.
  The pin is a constant this build carries and a flag overrides it.
- **The installed version is recorded where it was installed, and reported.** Each target directory
  keeps a record naming the pack, the source and ref it came from, the commit, a digest per file, and
  every path the installer wrote. That record is what says whether a deployment is current, and it is
  what an uninstall removes by — a verb that deleted by pattern would eventually delete somebody's
  own file.
- **Two destinations, because there are two readers.** The role halves go to the runtime's own data
  directory, where the runtime reads them at every launch to teach a role its job; the interactive
  and business packs go to the agent harnesses a person actually uses. The first is not optional: a
  runtime with no role skills launches every role untaught.

### What a pack has to look like

- A directory of skills, each one `<name>/SKILL.md`, where the directory name and the front matter's
  `name` are the same string. A skill whose front matter names anything else is loaded by nobody and
  reported by nobody.
- Front matter of exactly `name`, a one-line `description`, and `metadata.status` — `draft` or
  `verified`. `verified` means a named agent host read that exact body while driving this runtime and
  the digest of what it read is recorded in the pack's catalog. It is a claim about evidence, and the
  guards refuse it without one.
- A `README.md` beside them that links every skill and is linked by nothing else — the catalog.
- No build step and no dependency on the runtime's source.

## Installing without the verb

Copy a skill's directory into wherever your agent host looks for skills — for Claude Code,
`~/.claude/skills/<name>/` for yourself or `.claude/skills/<name>/` inside a project.

This repository also offers both packs through the **skills marketplace** at
[`.claude-plugin/marketplace.json`](../.claude-plugin/marketplace.json), by relative path, so a clone
is installable with no network. That is a different thing from the **plugin marketplace** under
[`plugins/`](../plugins/README.md), which holds the JavaScript packages that execute provider
operations in the plugin host.
