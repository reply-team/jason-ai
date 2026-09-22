# Business skills

Vendor-neutral knowledge of the SDR profession: campaign planning, ICP and audience building, research,
personalization, cadence, reply handling, deliverability and the guardrails that keep an outbound operation
safe. These skills teach an AI agent the **job**; the skills under [`skills/runtime`](../runtime/README.md)
teach it this **runtime**. They are two different kinds of knowledge and they are refreshed on different
cadences — this pack changes when the profession's practice does, not when Jason releases.

> **Knowledge, not the enforced contract.** The catalogue in [`sdr-operations`](sdr-operations/SKILL.md)
> fixes 325 named operations by five properties each, for any provider and any runtime. What *this build*
> validates and executes is the far smaller set of JSON documents under
> [`docs/contracts/operations/`](../../docs/contracts/operations), embedded into the runtime so that the file
> a plugin author reads is the file the runtime checks against. Where the two disagree, the contract decides,
> and an operation named here that the contract does not carry is knowledge this build cannot yet execute.

## Status

Every skill here is **`draft`**, and that is a statement about evidence rather than about quality. In this
repository `verified` means a named agent host read that exact body while driving this runtime, and the
digest of what it read is recorded. No host has done that with these texts yet. The knowledge itself was
written and reviewed elsewhere over a year; what has not been tested is how it reads beside a runtime that
owns campaign state, and that is what a reading would establish.

## The skills

| Skill | Status | What it is for |
|---|---|---|
| [`sdr-operations`](sdr-operations/SKILL.md) | draft | The contract the rest of the pack argues from: 325 named operations in 21 families, each fixed by what it reaches, whether it can be undone, what approval it needs, what to read before repeating it, and what it costs. |
| [`campaign-planning`](campaign-planning/SKILL.md) | draft | Turning a vague outbound goal into an executable plan: clarifying the objective until success is observable, surfacing the constraints, and deciding the checkpoints. |
| [`audience-building`](audience-building/SKILL.md) | draft | Turning raw prospect data into a deliberately shaped audience: who belongs, how two records become one person, what is actually reachable. |
| [`campaign-launch`](campaign-launch/SKILL.md) | draft | Preparing and launching a campaign with every precondition checked and the user's explicit approval of what will be sent. |
| [`inbox-triage`](inbox-triage/SKILL.md) | draft | Working the replies: what matters first, compact thread context, drafting together, sending only what was approved. |
| [`performance-analysis`](performance-analysis/SKILL.md) | draft | An honest read on performance: the figures over a stated window, and a diagnosis that separates deliverability from copy from targeting. |
| [`approval-boundaries`](approval-boundaries/SKILL.md) | draft | Where an agent must stop and ask: how an operation's approval class follows from its reversibility and its reach, and what a valid confirmation is. |
| [`sending-guardrails`](sending-guardrails/SKILL.md) | draft | Protecting sender reputation and inbox placement: authentication, warm-up, pacing, bounce interpretation and recovery. |
| [`linkedin-guardrails`](linkedin-guardrails/SKILL.md) | draft | Keeping social accounts safe under automation: invitation pacing, messaging windows, daily limits, and recovery when a platform restricts an account. |

## Where this came from, and what changed on the way in

This pack was published as an open skill pack in `reply-team/reply-skills`, **formerly** the home of both
this knowledge and the runtime guidance. It moved here at commit `cce5812`, byte for byte — every file's
SHA-256 matched its source — and four things were then changed in the open, each in its own commit:

- **The front matter is normalised on import.** A skill here declares `name`, a one-line `description`, and
  `metadata.status`, and **nothing else** — that is the rule, and it is what the guards enforce, so read it as
  the rule rather than as the list below. What the source carried under `metadata` was ten keys, all ten on all
  nine skills: `version`, `pack`, `category`, `maturity`, `status` (whose value there was `active`, a third
  meaning for a key that has two here), `owner`, `tags`, `tools`, `api` and `relations`. Only `status` survives,
  as `draft`. Folded descriptions are unfolded onto one line as well: three readers parse this front matter and
  none of them is a YAML parser. *If you re-sync this pack from upstream, normalise it again* — otherwise nine
  keys come back and the pack's guards fail for a reason that is hard to reconstruct from the failure. The
  removed values stay readable at the source commit named above.
- **Pointers to packs this repository does not ship were re-pointed**, not deleted: sentences that explained
  where plans and work items live now say that this runtime owns them, and that where no runtime is installed
  the plan lives wherever the user keeps it.
- **The boundary above was written into the six skills** that name an operation this runtime enforces.
- **This catalog** replaced the pack's own.

Rewriting the knowledge itself is deliberately not part of that list. Where these texts and this runtime
disagree about the profession, the disagreement is recorded rather than silently resolved.

## Installing them

```sh
jason skills install --dry-run --pack jason-business-skills
jason skills install --pack jason-business-skills
jason skills update --pack jason-business-skills
```

The first prints what would be written where and changes nothing; all three print the target roots before
writing, because writing into somebody's home directory is not a silent act. `update` re-runs the deployment
against the source and ref its record names — this pack is refreshed on its own cadence, so it is the verb this
pack is meant to be kept current with.
[`skills/README.md`](../README.md) is the contract the verb keeps.

A person without this build installs a skill by copying its directory into wherever their own agent host looks
for skills: for Claude Code that is `~/.claude/skills/<name>/` for themselves, or `.claude/skills/<name>/`
inside a project. The directory name is the skill's name and the two must match — a skill whose front matter
names anything else is loaded by nobody and reported by nobody.

This repository also offers both packs to a harness through the **skills marketplace** at
[`.claude-plugin/marketplace.json`](../../.claude-plugin/marketplace.json), which is a different thing from
the **plugin marketplace** under [`plugins/`](../../plugins/README.md): that one holds JavaScript packages
that execute provider operations in the plugin host.
