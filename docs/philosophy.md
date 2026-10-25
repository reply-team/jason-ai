# What Jason is, and who it is for

This page is for a person, or the AI agent working for them, who has found Jason and wants to know whether it is for
them. The [README](../README.md) says in a paragraph what Jason is and why it was built; this page gives the reasoning
behind that paragraph and says plainly who is well served by it and who is not. Everything it says Jason does, Jason
does in this release, on the page it links to. What a release changes is in its notes, not here.

## The job

Outbound sales development is a job done per person, over time. Somebody is chosen because they fit, read about,
written to, written to again or left alone, answered when they reply, and remembered when they say no. Each of those
people has a line of contact: what was sent, when, through which channel, what came back and what happens next. The
line is the work, and it lasts weeks or months.

An AI agent does much of the thinking in that job well: it can plan a campaign, research a company, write to a person
and call a tool. What it cannot do on its own is keep the line between conversations. When the session ends, nothing
holds what was sent, what waits for a decision, what failed and what comes next, and the next session starts from a
blank page. A tool that sends messages does not fix that either: sending is one step of the line, not the line.

Jason is built on that reading. The line belongs to the person who runs the outreach and to the agents working for
them, so it lives with them, on their machine, in a form an agent can read and a person can check.

## A harness

Jason calls itself a harness: a structure that lets the AI you already use do one job, outbound sales development, and
keep doing it. It has four parts, and each has one job.

- **The runtime keeps the state and performs the work.** It is one executable on your machine, with a data directory
  of your own. It keeps campaigns, the people in them, the work to be done about them, approvals, questions and an
  append-only history, and it carries the work on between sessions: a dispatcher claims work items and hands them to
  whatever performs them, and a campaign is reviewed when something happens and on a cadence when nothing does. You
  and your agents reach it through its command line and its API; the [browser UI](ui.md) is one more client of the
  same API.
- **Your agents think and decide, inside what you allowed.** Jason ships no AI model and holds no model credentials.
  Your own interactive session plans and decides with you; background work runs as roles, launched in an agent host
  you installed and signed in to yourself, and in this release that host is Claude Code. A role that cannot decide
  something alone asks you, and your answer carries the work on.
- **Plugins reach the providers.** A plugin performs a provider operation, such as enrolling a person or sending a
  message, against one provider's account, in a process of its own, and reports what happened in Jason's terms.
- **Skills teach the trade.** The [runtime skills](../skills/runtime/README.md) teach an agent to operate Jason; the
  [business skills](../skills/business/README.md) carry the profession itself, from planning a campaign to reading a
  reply, independently of any runtime or provider. They are a starting point: a company's own method sits on top.

Jason stands on the conventions agent hosts already have, skills and sessions first. Most of the work happens in your
own session, and each step the hosts take forward is a step for Jason too.

Two things Jason is not follow from this. It is not a model or a chat: it uses the agent you already have. And it is
not a sequencer: it runs no sequence of its own, plans a line per person and reaches people one at a time, and every
operation that reaches a person waits for a person's approval. A direct message is approved as it will be sent. An
enrolment into a provider's sequence is approved once for that person, and the provider then sends that sequence's
steps on its own, without an approval for each: a provider's sequences stay the provider's, and a campaign may still
enrol people into one where that is the better tool.

## Providers and plugins

Jason is vendor-neutral by position, not by accident. No provider is built into the runtime: an integration is a plugin,
never runtime code, and a fresh installation sends work nowhere until somebody writes a route saying which plugin
performs which operation for which campaign, in which account. The operations are published as contracts, one document
per operation under [docs/contracts/](contracts/README.md), and the file a plugin author reads is the file the runtime
enforces. Jason is made at Reply.io, and the first provider plugin, [plugins/reply/](../plugins/reply/README.md), works
against a Reply account through the same public mechanism any other plugin uses. There is no private path.

The division of labour follows from the job, and it has three owners who never trade places. The provider owns the
channels: the mailboxes and accounts, the sending itself, deliverability, the inbox where replies arrive. Jason owns
the line: who is contacted, about what, and what was decided, asked, approved and done for each person. You own the
goals, the authority and the money. The line is kept on your machine, not inside any one provider.

What a plugin may do is narrow and visible. It runs outside the runtime, in a short-lived process of its own; its
package carries a content digest that is checked before any of its code runs; it may use only the programs and hosts
the installation granted it, and nothing is granted by default. What a plugin may not do is decide: it never chooses
who is contacted, and work that needs a person's approval does not reach it until a person approved it. A plugin is
still software you installed on your own machine, and [docs/plugins.md](plugins.md) says so in plain terms, with
every rule a package is held to.

## Four kinds of knowledge

Jason keeps four kinds of knowledge apart, because they come from different places and deserve different trust.

- **Skills** are general: the profession, written once for everyone, versioned and shipped.
- **Campaign context** is particular to one campaign: the brief in your own words and what the campaign has established.
  It is part of the runtime's state, read by every role working on the campaign, and what you tell Jason about your
  business lives beside it in plain files you can read and edit, one folder per project and campaign
  ([docs/INSTALL.md](INSTALL.md) says where).
- **Role notes** are a role's own working memory, written by that role for its later runs and kept for your business as
  a whole, a project or a campaign. They are deliberately not authoritative: where a note disagrees with the campaign's
  state, the state is what is true.
- **Learned practice** would be the installation's own knowledge, distilled from what happened across campaigns. This
  version does not have it. It is named here so that the other three are never stretched to play its part.

The separation is the point. What works for one team fails for another, so general advice must never pass for a
finding about your own campaigns, and one campaign's finding must never be taught to every campaign.

## What stays yours

Jason is built to do as much as it can on its own and nothing that should be yours.

- **Approvals.** An operation that reaches a real person is parked, with a record of exactly what would be done, until
  a person decides, and nothing is sent in the meantime; nothing runs but what they approved, and the approval is
  given under a person's name, through the command line or the browser UI. Whether an operation needs one is written
  in its published contract, not in a setting.
- **The do-not-contact register.** Work that would reach somebody on it fails before it starts. That floor never
  relaxes.
- **A provider's block.** When the provider refuses an account for a cause the work cannot change, such as billing or
  a disconnected channel, Jason keeps that as a block and holds the work it concerns. A person who has removed the
  cause clears it under their name; an agent cannot.
- **Questions.** A role that reaches a decision it cannot safely make raises a question with its reason, and the work
  waits for your answer in whichever session you give it, not in the one that asked.
- **Notifications are pointers.** When something waits for you, Jason tells you how many things wait and where to
  look. The decision is made where the record is, never in the notification.
- **Your data and your machine.** The runtime keeps its data in a directory you own, and its history is append-only:
  what happened is not rewritten. What leaves your machine is what the plugins you install send to their providers, what
  the agents you use send to theirs, the runtime's check for a newer release, and the requests you start yourself, such
  as an install or an update; nothing else, and no telemetry. The exact list is in the [README](../README.md), right
  under its opening.

## Who it is for

- **You run outbound yourself, or with a small team,** you already work with an AI agent, and you have an account with
  a provider that owns your channels. The provider plugin that ships is Reply's; with any other provider, somebody
  writes a plugin first. You want the outreach to keep going between sessions, with the agent doing the thinking and
  you keeping the decisions.
- **A sales team whose people are at home with agents** and want them to run the line of contact for each person, each
  under their own approvals. That is one installation per person, under their own account, with their agent and
  their provider account.
- **A provider's customer who wants to try the harness.** Try it in a provider account you keep for testing, as the
  README asks. Jason acts there only through the published operations: some run without asking, such as creating a
  campaign, pausing one, updating a contact or filing a thread, each recorded in the history, and anything that
  reaches a person waits for your approval.
- **A plugin author with a provider to bring.** The contracts are published, the mechanism is the one every plugin
  uses, and [docs/plugins.md](plugins.md) is the guide.

## Who it is not for

- **Someone who wants a hosted product with a screen in front of the work.** Jason runs on your machine and is driven
  from your agent's session. Its browser UI shows what the runtime holds, approves, rejects and answers what waits for
  you, and pauses or starts a campaign; it is not a place where outreach is designed.
- **Someone with no provider, or no agent.** Jason performs no sending of its own: without a provider account and its
  plugin, it can plan and keep records, and nothing reaches anyone. Without an agent host, the runtime still keeps its
  state and performs the provider operations already decided, but the value is in the agent, and launched roles run in
  Claude Code in this release.
- **Someone who wants messages sent without a person deciding.** Every operation that reaches a real person asks for an
  approval, and its contract, not a setting, says so.
- **A team that needs one shared system.** An installation is one person's, with its own register and its own history; a
  team works as several.
- **A machine the builds do not cover.** Jason ships for Windows x64, Linux x64 and Apple-silicon macOS; there is no
  build for an Intel Mac ([docs/INSTALL.md](INSTALL.md)).
- **Someone who wants to run it as a service for other companies.** The runtime is a free executable whose source is not
  published, under its own licence, [LICENSE-RUNTIME.md](../LICENSE-RUNTIME.md): for your own business use, on as many
  of your own machines as you like, including rented cloud machines; running it for other companies, as a hosted or
  outsourced service, needs a written agreement, and redistributing or modifying it is not allowed. The skills,
  contracts, plugins, documentation and install scripts are MIT. Your agent host and your provider account stay yours to
  pay for, as you do already.
- **Someone who needs a promise of results.** Jason improves continuity, control and the use of professional knowledge.
  It does not guarantee replies, meetings or revenue when the offer, the data, the market or the sender's reputation are
  weak, and it does not turn an agent's judgment into truth.

## How it grows

Jason is in alpha. It does what its pages say, and what it does changes from one release to the next. Each release is
one self-contained step: a capability added, a contract published or tightened, a skill rewritten, and its pages
change with it. There are no dates and no roadmap promises here, and nothing on this page should be read as one. Any
new provider or kind of integration arrives the way the first one did, as a plugin against published contracts, never
as code inside the runtime. A skill is marked verified only after a named agent host has read that exact text while
driving this runtime; the rest are marked drafts, which means not yet read that way, not unusable.

To try it, start with [docs/INSTALL.md](INSTALL.md). The [wiring check](golden-path.md) shows the whole path once,
from an empty installation to one operation approved by you and performed against a provider. The real work begins
when you tell your agent about your first campaign, in your own words.
