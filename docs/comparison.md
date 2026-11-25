# How Jason compares

Jason AI GTM Harness is an open-source AI SDR that runs inside the agent you already use and keeps the outbound work
going on your machine after the chat ends. This page sets it next to the projects and products people most often weigh
it against, and says plainly where each of them fits better.

Every line about another project is taken from that project's own README or site, read on 25 November 2026. Projects
change quickly: read their pages before you decide, and open an [issue](https://github.com/reply-team/jason-ai/issues)
if a line here has gone out of date. The names are their owners' trademarks; none of these projects is affiliated with
or endorsed by Reply.io.

## In one table

| | Jason | Sellbird (formerly Harvey) | Franco | OpenOutreach | AgentSDR | SalesGPT |
|---|---|---|---|---|---|---|
| **What it is** | A durable runtime, skills and plugins around your own agent | An outreach agent on your Claude or ChatGPT plan | An outbound sales agent on your Claude Code or Codex plan | An AI agent for B2B lead generation | A self-hosted outbound workspace with an AI CRM | A Python agent for sales conversations on an LLM |
| **Where it runs** | Your machine | Your machine | Your machine | Your machine, as a self-hosted CLI | Your server (Docker or Bun, PostgreSQL) | Your code |
| **The model** | Your agent's plan; no API key | Your Claude or ChatGPT plan | Your Claude Code or Codex plan | An LLM key you bring | An AI key you bring | An LLM API you bring |
| **Channels** | Email and LinkedIn through Reply, SMS and call steps in Reply sequences, any other through a plugin | Email | Email from your mailbox | Email from your mailbox, or a CSV | Email, LinkedIn, WhatsApp | Voice, email, SMS and messaging apps |
| **Who approves a send** | You, for every message, until you choose otherwise | You, by default | You, with review, preview and autopilot modes | Not stated in its README | You approve each drafted reply; it sends on its own only if you turn on auto-send for a sequence | Your application; it offers a human-in-the-loop step |
| **Specialist roles** | Nine, with a critic and a campaign manager | Sub-agents | Five agents: Scout, Writer, Sender, Handler, Analyst | Separate packages to find and to send | An AI CRM for replies | One agent |
| **Licence** | MIT, the runtime included | AGPL-3.0 | MIT | Apache-2.0 | MIT | MIT |

## When something else fits better

- **Sellbird (formerly Harvey)** if you want prospecting without a lead database and cold email in one Python project.
  It runs on the same kind of plan Jason does.
- **Franco** if you want a lean agent that works through a list of accounts with approved copy and a kill switch, from
  your own mailbox.
- **OpenOutreach** if you want to describe a product and get matched people with the reason each one fits, from a
  self-hosted CLI.
- **AgentSDR** if a team wants one shared web workspace, with email, LinkedIn and WhatsApp and an AI CRM, on its own
  server. Each person runs their own Jason, so Jason is not one shared system today.
- **SalesGPT** if you are building a conversational sales agent into your own product, including voice.
- **A closed AI SDR** (11x, Artisan, AiSDR and others) if you want a hosted service that someone else runs, and its
  pricing suits you.
- **A sequencer** (Instantly, Smartlead, lemlist, Apollo and others) if one sequence for everyone is enough and you want
  it designed on screens.

## When Jason fits

- You already work in Claude Code, Codex or another agent, and want it to run outbound without a second AI bill.
- You want a plan per person that changes when they reply, go quiet, bounce or opt out, not one sequence for everyone.
- You want every message to wait for your yes until you decide otherwise, with an append-only history of who decided
  what.
- You want a supported provider for data, mailboxes, warm-up, email, LinkedIn and the inbox, in one account, and the
  freedom to add any other provider through a plugin.
- You want to read and change every skill, role and line of the runtime: [Make it yours](../README.md#make-it-yours).

Start under [Quick start](../README.md#quick-start), or ask your agent to follow https://get.jasonharness.ai/install.
The website's own comparisons are at [reply.io/harness](https://reply.io/harness/compare/other-ai-sdrs).
