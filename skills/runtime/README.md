# Runtime skills

Skills that teach an AI agent how to operate Jason itself: driving the CLI, the role entry points
the runtime invokes, handling approvals, troubleshooting, maintenance and updates.

They move in lockstep with the runtime's API and ship with it. Content lands here as the runtime
gains the operations these skills describe.

- [`managed-campaign-work`](managed-campaign-work/SKILL.md) — **draft.** Turning an objective into durable
  campaign work: creating the campaign, adding the people it is about, submitting managed provider operations,
  and handling the approvals they wait on. Every command it prints is parsed by the CLI's own parser in a test,
  so it cannot drift from the build it ships with.
