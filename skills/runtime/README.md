# Runtime skills

Skills that teach an AI agent how to operate Jason itself: driving the CLI, the role entry points
the runtime invokes, handling approvals, troubleshooting, maintenance and updates.

They move in lockstep with the runtime's API and ship with it. Content lands here as the runtime
gains the operations these skills describe.

- [`managed-campaign-work`](managed-campaign-work/SKILL.md) — **draft.** Turning an objective into durable
  campaign work: creating the campaign, adding the people it is about, submitting managed provider operations,
  and handling the approvals they wait on. Every command it prints is parsed by the CLI's own parser in a test,
  so it cannot drift from the build it ships with.
- [`roles/researcher`](roles/researcher/SKILL.md) — **draft.** The brief a role is given when the runtime
  launches it: how to call home in the one command form that is permitted, how to read and keep its campaign
  note, and how to answer in the shape the work item asked for.

## Role skills

`roles/<role>/` holds one skill per role, named after its directory because that is how a host looks one
up — a skill whose front matter names anything else is loaded by nobody and reported by nobody, so the
runtime refuses the attempt instead of launching an untaught role.

These are not read from here at run time. An operator composes the directory the runtime hands out,
`<data>/skills/roles/<role>/`, from this pack and — later — from the business pack, and the runtime copies
it into each attempt's own work directory. Composing rather than reading from two places keeps one
question answerable: what exactly was this role taught on this attempt.
