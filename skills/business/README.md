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

## The skills

| Skill | What it is for |
|---|---|
| [`sdr-operations`](sdr-operations/SKILL.md) | The contract the rest of the pack argues from: 325 named operations in 21 families, each fixed by what it reaches, whether it can be undone, what approval it needs, what to read before repeating it, and what it costs; and where the runtime's own names, its `audience` family among them, are not the catalogue's. |
| [`campaign-planning`](campaign-planning/SKILL.md) | Turning a vague outbound goal into an executable plan: clarifying the goal until success is observable, and with Jason what the campaign's `goals` are, surfacing the constraints, and deciding the checkpoints. |
| [`audience-building`](audience-building/SKILL.md) | Turning raw prospect data into a deliberately shaped audience: who belongs, how two records become one person, what is actually reachable; and with Jason, an outward search read from what the provider declares, narrowed before it is widened, previewed and probed, never pulled to the maximum. |
| [`campaign-launch`](campaign-launch/SKILL.md) | Preparing and launching a campaign with every precondition checked and the user's explicit approval of what will be sent. |
| [`inbox-triage`](inbox-triage/SKILL.md) | Working the replies: what matters first, compact thread context, drafting together, sending only what was approved, and with Jason starting from what the responder left and never drafting a second answer beside one. |
| [`performance-analysis`](performance-analysis/SKILL.md) | An honest read on performance: the figures over a stated window, and a diagnosis that separates deliverability from copy from targeting. |
| [`approval-boundaries`](approval-boundaries/SKILL.md) | Where an agent must stop and ask: how an operation's approval class follows from its reversibility and its reach, and what a valid confirmation is; with Jason, a standing approval is a person's, per campaign and category of reply, or the campaign's allowance for searches, capped in people. |
| [`sending-guardrails`](sending-guardrails/SKILL.md) | Protecting sender reputation and inbox placement: authentication, warm-up, pacing, bounce interpretation and recovery. |
| [`linkedin-guardrails`](linkedin-guardrails/SKILL.md) | Keeping social accounts safe under automation: invitation pacing, messaging windows, daily limits, and recovery when a platform restricts an account. |

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
- **The boundary above was written into the skills** that name an operation this runtime enforces.
- **This catalog** replaced the pack's own.

Rewriting the knowledge itself is deliberately not part of that list. Where these texts and this runtime
disagree about the profession, the disagreement is recorded rather than silently resolved.

## Installing them

```sh
jason skills install --dry-run --pack jason-business-skills
jason skills install --pack jason-business-skills
jason skills update --pack jason-business-skills
```

The first prints what would be written where and deploys nothing — a source not on this machine yet is fetched
into Jason's staging directory first, so there is something to show; all three print the target roots before
writing, because writing into somebody's home directory is not a silent act. `update` re-runs the deployment
against the source its record names, at the ref this build carries unless the record chose another — this pack
is refreshed on its own cadence, so it is the verb this pack is meant to be kept current with.
[`skills/README.md`](../README.md) is the contract the verb keeps.

A person without this build installs a skill by copying its directory into wherever their own agent host looks
for skills: for Claude Code that is `~/.claude/skills/<name>/` for themselves, or `.claude/skills/<name>/`
inside a project; the [host matrix](../../docs/execution-profiles.md#12-the-hosts) lists the others. The directory
name is the skill's name and the two must match — a skill whose front matter names anything else is loaded by
nobody and reported by nobody.

This repository also offers both packs to a harness through the **skills marketplace** at
[`.claude-plugin/marketplace.json`](../../.claude-plugin/marketplace.json), which is a different thing from
the **plugin marketplace** under [`plugins/`](../../plugins/README.md): that one holds JavaScript packages
that execute provider operations in the plugin host.
