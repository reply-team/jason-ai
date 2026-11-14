<div align="center">

# Jason AI GTM

### Your go-to-market, run by your AI.

**It works with the Claude Code or Codex plan you already pay for. No model API bill, no seats.**

Describe your idea. Jason finds the contacts, plans a line for every person, writes, sends, reads the replies<br>
and keeps going after the chat ends. Nine specialists, and nothing sent without your yes.

[![Release](https://img.shields.io/github/v/release/reply-team/jason-ai)](https://github.com/reply-team/jason-ai/releases/latest)
[![Runs on](https://img.shields.io/badge/runs%20on-Claude%20Code%20%2B%205%20more-D97757)](#quick-start)
[![Platforms](https://img.shields.io/badge/macOS%20%C2%B7%20Linux%20%C2%B7%20Windows-local--first-2F6FEB)](#quick-start)
[![License](https://img.shields.io/badge/license-MIT%2C%20runtime%20included-3DA639)](#license)

[At a glance](#at-a-glance) · [Quick start](#quick-start) · [What you get](#what-you-get) · [The team](#the-team) · [Your stack](#your-outbound-stack) · [Pricing](#what-it-costs) · [Compare](#how-it-compares) · [Docs](#documentation)

</div>

---

Going to market takes a team: someone to find the right people, someone to write, someone to follow up, someone to read
every reply. Your AI can already do each of those jobs. But when the chat closes, it forgets who it wrote to, who
answered and what comes next.

**Jason is the missing layer.** Tell it what you sell and who should buy it, in plain words. Jason finds the contacts,
plans each person's line of contact, writes, sends through your provider, reads the replies and keeps the work going
while you're in meetings. Your agent does the thinking, Jason keeps the memory, and every message waits for your
approval. Whoever installs it, you or your AI, starts from the three lines under Quick start.

**You use Jason from a release.** Nothing in this repository is built, cloned or tested to use it; the source is here
for changing Jason.

> **Plugins are plain JavaScript.** A plugin is a small JavaScript package the runtime runs in its own embedded engine,
> with no Node and no build step, so one package runs the same on macOS, Linux and Windows. The runtime is C#, chosen
> for cross-platform builds, stability and speed; its code is fully open. [Why](#plugins-are-javascript).

<!-- Demo GIF: describe the idea → contacts found → approve → send through Reply → handle a reply -->

---

## Quick start

Paste this into your AI (Claude Code, Codex or another agent). It installs Jason and takes you to your first campaign.

```text
Install Jason by following https://get.jasonharness.ai/install
When it reports Ready, start its quick-start skill and take me to my first campaign.
If anything stops you, ask me directly and tell me why.
```

Prefer a window? [Jason Desktop](#jason-desktop). Everything about installing: [docs/INSTALL.md](docs/INSTALL.md).

---

## What the install does

**Installing Jason should be the easy part: one prompt, one program, nothing to configure by hand.** Here is what that
means today.

- **What you do:** paste one prompt into your agent. It guides you through the rest and asks only when it needs you.
- **What it installs:** one self-contained executable, under your own account. The install script checks the download's
  SHA-256 before unpacking it, and ends by deploying the role skills from the release's `jason-skills.zip`. No admin
  rights, and no Docker, Python or Node for Jason itself.
- **How long it takes:** 16 seconds from nothing to `Ready` for the executable, the role skills, the browser UI and the
  start, measured on an Apple-silicon Mac with Jason 0.24.0. Your agent working through the guide takes longer, and so
  does connecting your provider.
- **How you know it worked:** `jason status --human` checks everything, and each failed check names the command that fixes it.
- **What you don't need:** `git`, a build, a server, a mock provider, a hosted account or an onboarding call.
- **How you take it off:** `jason uninstall`. It removes only the skills it recorded installing, so anything you copied in
  by hand stays. Your data stays too unless you add `--purge-data`, and your `~/Jason` folder is never touched.
- **If you would rather have a window:** [Jason Desktop](#jason-desktop) installs from one download, a signed installer
  for Windows, a dmg for macOS, or a deb or an AppImage for Linux, and comes off with Remove What Jason Placed… in its
  menu (macOS and Linux) or Windows's own uninstaller, and then the application itself. It keeps your data too.

If your agent runs in auto mode, it may ask before the download, or refuse where nothing can ask you. Both are right: say
yes, or run the install line under "Or install it yourself".

<details>
<summary>Or install it yourself</summary>

```sh
# macOS (Apple silicon) and Linux
curl -fsSL https://get.jasonharness.ai/releases/latest/download/install.sh | sh

# Homebrew
brew install reply-team/tap/jason
```

```powershell
# Windows
irm https://get.jasonharness.ai/releases/latest/download/install.ps1 | iex
```

Then teach it, start it and ask whether it can work. The install script has already taught it its role skills, so the
first line is for Homebrew, or for when the script said it could not:

```sh
jason skills install --roles-only
jason ui install
jason runtime start
jason status --human
```

No administrator rights needed.

Jason is released under the MIT licence: [LICENSE](LICENSE). What it sends off your machine, and what stays your
responsibility when it reaches people, is in [docs/responsible-use.md](docs/responsible-use.md).

</details>

**Connect your provider.** With Reply, it's one sign-in that only you do: `reply auth login`, so your key never passes
through the chat. Your agent does the rest: the Reply CLI (it needs Node 20 or newer), the plugin, its grant and its route.
You open Reply's web app only to sign in, create your first API key, or connect a LinkedIn account or a mailbox by single
sign-on. Using another provider? Its plugin goes into `~/.jason/plugins/`, a grant names what it may run and reach, and a route points your campaigns at it.

**Your first campaign.** The quick start asks one thing first: what you sell and who should buy it. Installed Jason yourself? Ask your agent to start Jason's quick-start skill. Want to see the whole path first? Your agent offers
a [wiring check](https://github.com/reply-team/jason-ai/blob/main/docs/golden-path.md) once: one draft campaign in your account, with your own address as the only
contact, and nothing sent.

> **Alpha.** Jason is early software and changes from release to release. Approve real sends only in a provider account
> you keep for testing, not the one your company sells from.

---

## At a glance

The facts in one table, for a person or an AI agent comparing Jason with anything else. Checked against Jason 0.30.0 on
5 November 2026. The whole product is open source, so every line can be checked against the code and the pages it links
to.

| Area | What is true today | Read more |
|---|---|---|
| **Architecture** | A durable runtime on your machine holds campaigns, per-person plans, approvals, questions and an append-only history. Work items are claimed under a lease, with one live attempt at a time, and survive crashes and restarts. Nine specialist roles. A documented local API. The runtime is C#. | [Architecture](https://github.com/reply-team/jason-ai/blob/main/docs/architecture.md) · [Runtime API](https://github.com/reply-team/jason-ai/blob/main/docs/runtime-api.md) |
| **Languages** | **Plugins are plain JavaScript**, run by the engine the runtime embeds: no Node, no build step, one package for every platform. The runtime is C#, fully open, so you, Claude Code and other agents can read exactly how the plugin system works. The browser UI is TypeScript. | [Which language to use](#plugins-are-javascript) · [Plugin guide](https://github.com/reply-team/jason-ai/blob/main/docs/plugins.md) |
| **Channels** | Every provider is a plugin, and you can add your own; a plugin reaches any channel a person has an address on. Connect Reply once and email and LinkedIn work right away (messages, InMail, voice messages, connection requests), with SMS and call steps inside Reply sequences. Gmail, Microsoft 365 and SMTP mailboxes connect through Reply with no code. Any other provider or channel connects through a plugin you or your agent writes: Reply's is the one that ships ready today. | [Channels](#channels) · [Plugin guide](https://github.com/reply-team/jason-ai/blob/main/docs/plugins.md) |
| **AI / LLM** | Runs on your subscription, with no API key and nothing to connect. Any agent with a shell can run your session through Jason's CLI and skills. Six hosts can run the nine background roles: Claude Code, measured on real runs; Codex, measured on Windows but not yet run on an account (its sandbox there needs an administrator once); Cursor CLI, GitHub Copilot CLI, Gemini CLI and Antigravity CLI, built from each host's documentation and not yet run on an account. Jason ships no model and holds no model credentials. | [Which agent does what](#runs-on-the-plan-you-already-pay-for) · [Execution profiles](https://github.com/reply-team/jason-ai/blob/main/docs/execution-profiles.md) |
| **Search and personalization** | Find leads through Reply's Live Data API (1B+ B2B contacts, with a preview of the audience size before you spend credits), or through your own tools: anything your agent can reach, a CSV, your CRM. A Researcher and a Personalizer role work per person. Jason has no contact database of its own. | [Finding leads](#finding-leads) |
| **Deliverability and warm-up** | Deliverability modules ship with Jason: sending and LinkedIn guardrail skills (authentication, warm-up, pacing, bounces, recovery) and a Deliverability role that reads bounces, opt-outs and failures and can pause a campaign at once. Warm-up itself runs in the provider you connect: Reply, or TrulyInbox, Lemwarm, Warmy and others. Jason has no warm-up engine of its own. | [Deliverability](#deliverability-and-warm-up) |
| **Human in the loop** | Every operation that reaches a real person waits for a person's approval, in the chat or the browser UI. It is written into the operation's contract, not left to a setting, and there is no autopilot switch. Two things to know: enrolment into a provider's sequence is approved once per person, and the provider then sends that sequence's steps; and a named person can pre-approve drafted replies to one category of reply in one campaign, with an optional expiry and ceiling. Every such release is logged under that person's name. | [Nothing reaches a person without you](#nothing-reaches-a-person-without-you) |
| **Integrations and CRM** | Any integration you can connect to Claude Code works: MCP server, CLI or API. Your agent connects it and uses it, with no Jason connector needed. HubSpot, Salesforce and Pipedrive sync two ways through Reply, or connect through the CRM's own MCP. Phone pushes go through ntfy. | [Any other integration](#any-other-integration) |
| **Installation** | One prompt pasted into your agent, or one line in a shell. One self-contained executable under your own account, with no admin rights and no Docker, Python or Node for Jason itself. Measured on a Mac: 16 seconds from nothing to `Ready`. | [Quick start](#quick-start) · [Install guide](https://github.com/reply-team/jason-ai/blob/main/docs/INSTALL.md) |
| **Documentation** | 20 guides, 30 operation contracts in Markdown and JSON with 942 test fixtures, a catalogue of 325 SDR operations, a page per skill, release notes, a self-check and a troubleshooting skill. | [Documentation](#documentation) |
| **Extensibility** | Skills and roles are plain Markdown you can edit. Plugins are small JavaScript packages against published contracts, with no private path: Reply's uses the same door as yours. You can register new roles and build on the Runtime API. A plugin can also subscribe to the runtime's journal: it is handed the lines of the kinds you granted it, after they are written, and acts back through the Runtime API only as far as you granted. | [Make it yours](#make-it-yours) |
| **Maturity** | Alpha. The runtime's source and the public history opened with 0.24.0 on 1 November 2026. The repository carries 1105 C# test files and a UI test suite, with CI on macOS, Linux and Windows. It ships 9 business skills, 8 runtime skills and 9 role skills: campaign planning, audience building, launch, inbox triage and the rest of the profession's practice, the skills that teach an agent to operate the runtime, and one skill per role. | [Releases](https://github.com/reply-team/jason-ai/releases) · [Skills](https://github.com/reply-team/jason-ai/blob/main/skills/README.md) |
| **Security and compliance** | No telemetry. Campaign data stays on your machine, apart from what the plugins you install send to their services. Install scripts verify the download's SHA-256 before unpacking. Plugins run in their own short-lived process, only with the programs and hosts you granted, and are digest-checked. The provider's CLI holds the credential, so Jason never sees it. The do-not-contact list never relaxes. Staying within the law where you send stays your job. | [What leaves your machine](#under-the-hood) · [Responsible use](https://github.com/reply-team/jason-ai/blob/main/docs/responsible-use.md) |

---

## Contents

- [Quick start](#quick-start)
- [At a glance](#at-a-glance)
- [Runs on the plan you already pay for](#runs-on-the-plan-you-already-pay-for)
- [What you get](#what-you-get)
- [How it works](#how-it-works)
- [Just ask](#just-ask)
- [Jason Desktop](#jason-desktop)
- [The team](#the-team)
- [Nothing reaches a person without you](#nothing-reaches-a-person-without-you)
- [Your outbound stack](#your-outbound-stack)
- [Make it yours](#make-it-yours)
  - [Plugins are JavaScript](#plugins-are-javascript)
- [What it costs](#what-it-costs)
- [How it compares](#how-it-compares)
- [Who it's for](#who-its-for)
- [Under the hood](#under-the-hood)
- [Documentation](#documentation)
- [FAQ](#faq)
- [Philosophy](#philosophy)
- [Getting help](#getting-help)
- [License](#license)

---

## Runs on the plan you already pay for

Most AI sales tools resell you a model. The tokens are baked into the price of every seat, and to protect that margin they
ration the thinking: shorter prompts, cheaper models, fewer passes over each draft.

Jason ships no model at all. It runs in **your agent, under the login you already have,** on a Claude Code or Codex
subscription, within its usage limits. There's no API key to manage, nothing to connect, and no per-token bill that grows
every time a role thinks twice.

| Your agent | Your session: plan, approve, drive Jason | The nine background roles | What you pay |
|---|---|---|---|
| **Claude Code** | ✅ Yes, and the one we verify against | ✅ Yes, measured on real runs | A Claude Pro, Max or Ultra plan. No API key |
| **Codex** | ✅ Through Jason's CLI and skills | ✅ Measured on Windows, not yet run on an account (its sandbox there needs an administrator once) | Your Codex plan. No API key |
| **Cursor CLI · GitHub Copilot CLI · Gemini CLI · Antigravity CLI** | ✅ Through Jason's CLI and skills | ✅ Built in, not yet run on an account | That agent's own plan (Gemini CLI on a Code Assist sign-in) |
| **Any other agent with a shell** | ✅ Through Jason's CLI and skills | Not in this release | That agent's own plan |

Jason is a plain command-line program and its skills are plain Markdown, so any agent that can run a command can operate
it. You choose which agent runs the background work, and if none is available, that work waits while everything else
keeps running.

**Why it matters**

- **You don't pay for AI twice.** The thinking is covered by a plan you already have. Jason itself is free. What you
  pay for is what actually goes out: the sends, through your provider.
- **Better messages, not cheaper ones.** Every draft is written with the model you choose for that role, and the
  planner can have a second role review it before you see it. No vendor's budget model in between.
- **Model calls only where judgment is needed.** The runtime does the bookkeeping in plain code: what's due, who's
  next, what's waiting, what failed. The model is called for research, writing, reviewing and reading replies.
- **It grows with your agent.** When your agent gets a better model or a new feature, Jason gets it the same day.
- **Your context is already there.** The same agent that knows your files, your notes and your connected tools does
  the selling.
- **Your keys stay yours.** Jason holds no model credentials. It has nowhere to put one.

---

## What you get

- **Your AI becomes your go-to-market team.** No new platform to learn, no onboarding calls. You talk to your agent in plain words,
  and it runs your outbound.
- **A memory that outlives the chat.** Every campaign, person, plan, decision and reply is kept on your machine. Close
  the laptop, restart, crash: Jason picks up exactly where it was. Each role also keeps its own notes for its later runs,
  for your business, a project and a campaign. Where a note and the campaign record disagree, the record wins.
- **One plan per person, not one sequence for everyone.** Each prospect gets their own line of contact, and it changes
  when they reply, when a group goes quiet, or when bounces and opt-outs climb.
- **Nine specialists instead of one generalist.** A planner, researcher, copywriter, personalizer, critic, responder,
  analyst, deliverability specialist and a manager who watches the whole campaign.
- **You keep the send button.** Nothing reaches a real person until you say yes, in the chat or in the browser. You can
  also say yes in advance, for one kind of reply in one campaign, and take it back at any time.
- **Never a double-send.** If a send is interrupted and the answer is lost, Jason doesn't guess. A reply comes back to you.
  A new message is repeated only under its own key, where the provider keeps those keys and answers with the first result
  instead of sending again. Otherwise it comes back to you too.
- **Every channel, and room for your own.** Connect Reply once and email and LinkedIn work right away, with SMS and call
  steps in the same sequences, and the inbox and warm-up handled. Or add any provider or channel of your own through a
  plugin.
- **Yours to change.** Every skill and role is plain text. Want a compliance check before anything goes to a bank? Ask
  your AI to add it.

---

## How it works

Jason is a **harness**: it wraps the AI you already use and gives it what one session can't hold.
Your agent is the brain. Jason is what it remembers, what it's allowed to do, and everything it has done.

| Part | Like a new hire's… | What it does |
|---|---|---|
| 🧠 **Your agent** | Intelligence | Claude Code, Codex or another agent plans and decides with you. Jason ships no model and holds no model keys. |
| 📘 **Skills** | Training | The trade: picking accounts, writing cold email, reading replies, staying out of spam. |
| 🗂 **The runtime** | Notebook and calendar | One small program on your machine that keeps campaigns, plans, approvals and history, and carries the work on between sessions. |
| ✉️ **Plugins** | Phone and inbox | Reach your channels to enrol people, send, and read replies. Reply ships ready; add your own the same way. |

Three owners, who never trade places: **your provider** owns the channels, the sending and the inbox. **Jason** owns
the line: who is contacted, about what, and what was decided for each person. **You** own the goals, the approvals and
the money.

Every message travels the same line, and you sit in the middle of it:

```text
plan → research → write → personalize → critique → ✋ you approve → send → read the reply → re-plan for that person
```

Day to day, it goes like this:

1. **Install with one prompt.** Paste it into Claude Code; your agent installs Jason and checks it's ready.
2. **Describe your campaign** in your own words, a link, a file or a voice note. Jason asks what's missing, one
   question at a time, like a new hire would.
3. **Bring contacts or let Jason find them:** Reply's Live Data API, your own search tools, a CSV, a Google Sheet, your CRM,
   or public research.
4. **Approve the first message.** Your feedback ("too formal", "always mention a customer result") becomes a rule.
5. **Start small.** A pilot of ten, every message approved. Jason reports what's working; you decide whether to grow it.
6. **It reacts.** Replies, objections, out-of-office and silence each change the plan for that one person.

---

## Just ask

No forms, no sequence builder. You say what you want the way you'd brief a great sales hire:

- *"Twenty CFOs at logistics companies in Chicago. LinkedIn first, email if they accept."*
- *"Re-engage everyone who went quiet last quarter, with a new angle."*
- *"Work our closed-lost deals from last year. Skip anyone who left over price."*
- *"Draft answers to today's replies. I'll approve them after lunch."*
- *"What's working in the agency campaign, and what should we stop?"*
- *"Before anything goes to a bank, check it against our compliance rules."*

---

## Jason Desktop

Jason Desktop is a window onto the same installation your agent and the `jason` command use, for the moments you want
to see a screen. It is one more client of the runtime, not a second Jason: it works on the data directory the CLI
uses, starts the runtime through `jason runtime start`, and changes the installation only through the CLI's verbs. It
adds a chat that starts your own agent (Claude Code, or Codex to read and advise) on your computer and asks you before
the agent acts, a tray that counts what waits for you, and one-step setup of Reply, your notification channels and your
AI provider's key. The full experience is still a session with your agent, by text or by voice, with the skills
loaded; the desktop never replaces it, and what a screen shows is never all there is.

It is published with every release, from the [releases page](https://github.com/reply-team/jason-ai/releases), as
`jason-desktop-windows-x64-setup.exe` for Windows on x64, `jason-desktop-macos-arm64.dmg` and
`jason-desktop-macos-x64.dmg` for macOS, and for Linux on x64 `jason-desktop-linux-x64.deb` (Debian and Ubuntu, and the
one to take on Ubuntu 24.04 and later) and `jason-desktop-linux-x64.AppImage`, or from the short addresses `get.jasonharness.ai/desktop/windows`, `/desktop/mac`,
`/desktop/mac-intel`, `/desktop/linux-deb` and `/desktop/linux-appimage`. The Windows and macOS packages are
signed; the Linux ones are not, and are built so, started once under a virtual display, and not yet seen on a real
machine. To take it off, choose Remove What Jason Placed… in its menu on macOS and Linux (Windows's uninstaller does the same) and then
remove the application; your data stays. [Jason Desktop](https://github.com/reply-team/jason-ai/blob/main/docs/desktop.md)
says what it does, what its chat may run, how it updates and how it comes off.

---

## The team

Instead of one AI doing everything, the work is split the way a good sales team splits it.

| Role | What they do | For example |
|---|---|---|
| **Manager** | Watches the whole campaign, on events and on a schedule | "Nothing has moved in a week. Pause, or change the angle?" |
| **Planner** | Plans the campaign and each person's line with you | "Ten SaaS companies in Austin. Email first, then LinkedIn." |
| **Researcher** | Finds the right people and the facts about them | A new office, a funding round, a fresh job title |
| **Copywriter** | Writes the core message and the follow-ups | The pitch, in your voice |
| **Personalizer** | Makes each message about the person reading it | "Saw you just opened in Denver…" |
| **Critic** | Reviews the drafts the planner hands it | Sends back a claim you can't prove |
| **Responder** | Reads replies and drafts the answer | Tells "not now" from "interested" from "talk to my colleague" |
| **Analyst** | Tells you what's working | "LinkedIn gets twice the replies of email for this group." |
| **Deliverability** | Keeps your mail out of spam | Pauses a campaign whose bounces climb |

The roles run in the background on the host you chose, on your plan. When one can't safely decide something, it asks you,
and the work waits for your answer in whichever session you give it.

The Manager wakes on what happens: a failure, a rejected approval, a reply, an answered question, a provider event or
block, or something you reported. If nothing happens, it looks in anyway, every five hours unless you set another pace
for that campaign.

---

## Nothing reaches a person without you

Jason does as much as it can on its own, and nothing that should be yours.

- **Approvals are the rule, not a setting.** Every operation that reaches a real person is parked with a record of
  exactly what would be done, until you decide. Approve in the chat or in the browser UI, where you see the person,
  the account it goes from and the full message.
- **A yes in advance, only if you give one.** You can pre-approve drafted replies to one category of reply in one
  campaign, such as `interested`, with an expiry and a ceiling if you want them. A search for people sends nothing, but
  it spends your provider's credits, so the first search for a new audience always waits for you; after it, you can let
  the searches that top up that audience go without asking, up to a number of people you set. Only a named person can
  set either, a role never can, and every message or search it lets go is logged under that person's name.
- **A do-not-contact list that never relaxes.** Work that would reach someone on it fails before it starts.
- **Blocks stop the line.** If your provider refuses an account (billing, a disconnected channel), Jason holds the work
  until you clear it or a periodic trial gets past the block.
- **Questions, not guesses.** A role that isn't sure raises a question with its reason.
- **A history that can't be rewritten.** Every action is appended to a journal, so you can always see what happened
  and why.
- **Notifications that point, not decide.** A desktop notification or a push to your phone tells you something waits,
  and where.

---

## Your outbound stack

A harness alone doesn't send a single email. Outbound also needs infrastructure: someone to find the people, mailboxes
to send from, a warm reputation, an inbox that catches replies. **In Jason, all of that is pluggable. Connect Reply once and
it's all there, or pick your own tool for any step.** Jason is built by the team at [Reply.io](https://reply.io), and no
provider is built into the runtime, Reply included.

### Channels

Every provider is a plugin, and you can add your own; a plugin reaches any channel a person has an address on. There
are three ways to connect one:

| Way | What you do | What you get |
|---|---|---|
| **Reply, in one step** | Sign in once with the Reply CLI. Your agent wires up the rest | Email and LinkedIn right away: messages, InMail, voice messages and connection requests. SMS and call steps inside Reply sequences. One unified inbox for email and LinkedIn. Your mailboxes connect into Reply: Gmail, Microsoft 365 or any SMTP/IMAP account, or buy a domain with mailboxes there |
| **Any provider, through a plugin** | Ask your agent to write a plugin, or write it yourself | Your own Gmail, Microsoft or SMTP sending, Instantly, HeyReach, a WhatsApp or Telegram tool, an in-house system. A plugin is a small JavaScript package, and it implements only the operations you need. Routes choose the plugin per campaign, or even per operation |
| **Any tool, through your agent** | Connect it to your agent as an MCP server, CLI or API | Your agent acts with it and records what happened in Jason with `jason report submit`, so the history stays true. Jason's approval gate covers plugin operations only, so this path relies on your agent asking you first |

**What ships ready today:** the Reply plugin, the journal forwarder, and ntfy, Slack and Telegram for notifications, each
one `jason plugin install` away. A direct Gmail, Microsoft or SMTP connection
without Reply is a plugin to write, not a switch to flip: the [author's guide](https://github.com/reply-team/jason-ai/blob/main/docs/plugins.md) and the
[contracts](https://github.com/reply-team/jason-ai/blob/main/docs/contracts/README.md) are public, one document per operation, and your agent can work from them.
Channel names are open: a provider's campaign can run on any channel a person has an address on. One-off direct messages
are defined for email and LinkedIn.

### Finding leads

Two routes, and you can mix them per campaign:

- **Reply's Live Data API.** Your agent searches 1B+ B2B contacts through the Reply API: preview the audience size first,
  then spend credits on the people you want. They land in your contacts, ready for a campaign.
- **Your own search.** Any data tool your agent can reach: the tools listed below through their MCP, CLI or API, your CRM, a
  CSV, a Google Sheet or public research. The people it finds go into a Jason campaign.

Personalization happens per person either way. The **Researcher** finds and checks the facts, saying where each was seen
and what it couldn't establish. The **Personalizer** makes each message about the person reading it, and the **Critic**
sends back a claim you can't prove. Jason has no contact database of its own.

### Deliverability and warm-up

Jason ships the judgment; warm-up itself runs in the provider you connect.

- **Sending guardrails.** A skill for domain authentication, warm-up, volume pacing, reading bounces and recovering from them.
- **LinkedIn guardrails.** A skill for invitation pacing, messaging windows, daily limits and recovery when a platform
  restricts an account.
- **A Deliverability role.** It reads bounces, opt-outs and failures, says what they mean and, when a campaign has to stop,
  pauses it at once without waiting for anyone. Performance analysis separates deliverability problems from copy and
  targeting.
- **Hard stops.** A do-not-contact list that never relaxes, and provider blocks that hold the work until you clear them or a periodic trial gets past the block.
- **Warm-up, through your provider.** Connect Reply for MailToaster warm-up, ramp-up and domain health checks, or another
  provider such as TrulyInbox, Lemwarm or Warmy. Jason has no warm-up engine of its own.

### Any other integration

If you can connect it to Claude Code, Jason can work with it. Hand your agent an MCP server, a CLI or an API, and it connects
and uses it in the same session that runs your outbound: a CRM, a calendar, Slack, a data warehouse, your own internal tools.
Nothing needs a Jason-specific connector. Programs of your own can also read and write the runtime through the documented
[Runtime API](https://github.com/reply-team/jason-ai/blob/main/docs/runtime-api.md).

### Every step, and who can do it

Reply covers nearly every step out of the box. Or pick your own tool for any of them.

| Step | In Reply | Or connect |
|---|---|---|
| **Find contacts** | ✅ Live Data: 1B+ B2B contacts · `API` | Apollo · ZoomInfo · Clay · Lusha · your CSV or CRM |
| **Find emails & phones** | ✅ Enrichment · *API coming soon, per Reply's docs late November 2026* | Hunter · Prospeo · Findymail |
| **Verify emails** | ✅ Email validation · `API` | ZeroBounce · Bouncer · MillionVerifier |
| **Domains & mailboxes** | ✅ Buy a domain with mailboxes, or connect Gmail, Microsoft 365, SMTP · `API` | Google Workspace · Mailforge · Zapmail |
| **Warm-up & deliverability** | ✅ MailToaster warm-up, ramp-up, domain health checks | Lemwarm · TrulyInbox · Warmy |
| **Email** | ✅ Sequences and direct email · `API` | Instantly · Smartlead · lemlist |
| **LinkedIn** | ✅ Messages, InMail, connection requests, voice and AI voice messages · `API` | HeyReach · Expandi · Dripify |
| **Calls, SMS, WhatsApp** | ✅ Cloud Calls, SMS and WhatsApp steps · `API` for SMS, call and WhatsApp steps | Aircall · JustCall · Orum |
| **Inbox & replies** | ✅ Unified inbox for email and LinkedIn · `API` | Your own Gmail or Outlook |
| **Reporting & webhooks** | ✅ Reports and webhooks · `API` | Jason's analyst |
| **CRM** | ✅ Two-way sync with HubSpot, Salesforce, Pipedrive | Your CRM's own MCP |
| **Meetings** | ✅ Booking links on Google Calendar or Calendly | Cal.com · Chili Piper |
| **Research** | ✅ Jason's researcher, on your agent's plan | Clay · BuiltWith |
| **The thinking** | ✅ Your Claude Code or Codex plan | Your Claude Code or Codex plan |

<sub>✅ in Reply today · `API` your agent can also drive it through Reply's API</sub>

**How each tool reaches Jason.** Data, verification and research tools are used by your agent through their own MCP,
CLI or API, and the people they find go into a Jason campaign. Sending, LinkedIn and the inbox go through a **provider
plugin**: [Reply's](https://github.com/reply-team/jason-ai/blob/main/plugins/reply/README.md) ships ready, and any other provider plugs into the same published
contracts, with no private path. One provider per campaign, or a different one per operation.

> **Or connect Reply once.** One Reply account covers almost every row above: data, validation, mailboxes, warm-up,
> email, LinkedIn, calls, the inbox, CRM sync and meetings. Your agent reaches it through Reply's CLI, API and MCP, and
> Jason's Reply plugin ships ready. One login, one bill, and no glue between six tools. Jason keeps the work
> alive; Reply is the infrastructure underneath it.

<details>
<summary><b>Bringing your own tools? Entry prices, free tiers and how your agent connects</b></summary>

<br>

| Step | Tool | Starts at | Free | Your agent connects via |
|---|---|---|---|---|
| Find people | Clay | $167/mo | 100 credits/mo | MCP · CLI |
| | Lusha | $49.90/mo | 40 credits/mo | MCP · API |
| | Apollo | paid plans per user | free plan | MCP |
| | ZoomInfo | not public | trial | MCP · API |
| Find emails | Hunter | $49/mo | 50 credits/mo | MCP · API |
| | Findymail | $99/mo | 10 credits | MCP · API |
| | Dropcontact | €79/mo | 50 credits | MCP · API |
| Verify emails | ZeroBounce | $39 per 2,000 | 100/mo | MCP · API |
| | Bouncer | $8 per 1,000 | 100 credits | MCP · API |
| | MillionVerifier | $89 per 50,000 | 100 credits | API |
| Mailboxes | Google Workspace | ~$7 per mailbox/mo | 14-day trial | API · CLI |
| | Mailforge | $3 per mailbox/mo, 10 minimum | — | API |
| | Zapmail | $39/mo for 10 mailboxes | — | API on Pro |
| Warm-up | TrulyInbox | $29/mo | 1 mailbox free | API |
| | Lemwarm | $29 per mailbox/mo | inside lemlist plans | — |
| | Warmy | $49/mo | 7-day trial | MCP · API |
| Email | Instantly | $47/mo | trial | MCP · CLI · API |
| | Smartlead | $39/mo | trial | MCP · API |
| | lemlist | $69/mo | 14-day trial | MCP · CLI · API |
| LinkedIn | HeyReach | $79 per sender/mo | 14-day trial | MCP · CLI · API |
| | Dripify | $79 per user/mo | 7-day trial | MCP · API |
| | Expandi | $99 per seat/mo | 7-day trial | MCP |
| Calls | JustCall | $29 per user/mo, 2 minimum | 14-day trial | MCP · API |
| | Aircall | $30 per licence/mo, 3 minimum | test access | API |
| Meetings | Calendly | $10 per seat/mo | free plan | MCP · API |
| | Cal.com | $12 per user/mo | free for 1 user | API |
| CRM | HubSpot | from $20 per seat/mo | free CRM | MCP · API |
| | Salesforce | $25 per user/mo | free suite | API · CLI |
| | Pipedrive | ~$14 per seat/mo | 14-day trial | MCP · API |

<sub>Entry prices as listed on each vendor's site, 31 October 2026, billed annually where that's the headline. They change
often, so check before you buy. Data and research tools reach Jason through your agent; another sending provider needs
a Jason plugin.</sub>

</details>

**Also works with:** desktop notifications, and phone pushes, Slack and Telegram each one `jason plugin install` away · approvals in the chat or in Jason's browser
UI · macOS (Apple silicon), Linux x64 and Windows x64.

---

## Make it yours

Closed AI SDRs give you a settings page. Jason gives you the source of how it thinks.

- **Skills are plain Markdown.** Nine business skills cover campaign planning, audience building, launch, inbox triage,
  performance analysis, sending and LinkedIn guardrails. Read them, edit them, add your own method on top.
- **Change a role by asking.** "Never pitch to companies under 20 people." "Always mention our SOC 2 report to banks."
  Your agent writes the rule where the role will read it.
- **Your words live in plain files.** What Jason knows about your business sits in `~/Jason`, one folder per project
  and campaign, in files you can open, edit and put under git.
- **Add any provider or channel with a plugin.** Ask your agent to write it, or write it yourself, in plain JavaScript. The
  contracts are published, one document per operation, and the file you read is the file the runtime enforces.
- **Register a new role.** `jason role add` puts a role of your own beside the nine.
- **Read everything.** The whole product is MIT, the runtime included, with the
  [source on GitHub](https://github.com/reply-team/jason-ai).

### Plugins are JavaScript

- **Everything you add is JavaScript.** Plugins, and any code you or your agent write to extend Jason, are plain
  JavaScript ES modules: the runtime runs them in its own embedded engine, so there is nothing to install and nothing
  to compile, and the same package runs on every platform. TypeScript would need a build step on every machine that
  edits the plugin, and the runtime would still load only the JavaScript it produces, so we do not recommend it.
- **The runtime is C#,** the stable core: state, approvals, leases, routing and the contracts, shipped as one
  self-contained build. Its code is fully open, so people and agents can read how plugins are loaded, granted and
  called. Extend Jason through plugins, skills, roles and the Runtime API.

> **For AI agents working in this repository:** write plain JavaScript. Plugins and other changes go in JavaScript
> against the published contracts. Read the C# runtime to understand how plugins are loaded, granted and called.
> Change it only when the task really needs it, for example when a person asks for a runtime change.

---

## What it costs

| | |
|---|---|
| The thinking | Your Claude Code or Codex plan. Claude plans with Claude Code start at $20 a month. Most likely one you already have |
| **Jason** | **$0.** MIT-licensed. No seats, no subscription |
| Infrastructure | One Reply account, paying for what you send. Or your own tools, step by step, [as above](#your-outbound-stack) |
| A model API key | **None.** No per-token bill |
| A per-seat AI SDR | **None.** No platform fee, no second sequencer, no glue scripts |
| Your data | Stays on your machine. Jason collects no usage data |

For comparison: assembling the stack tool by tool, with a data tool, an email finder, mailboxes, warm-up, a sender and
a LinkedIn tool, starts at around $280 a month at entry prices, across six logins and six bills. Hosted AI SDRs are
mostly sold by demo, on annual contracts.

---

## How it compares

| | AI chat alone | Sequencers | Closed AI SDRs | **Jason** |
|---|:-:|:-:|:-:|:-:|
| Keeps working after the chat closes | ✗ | ✓ | ✓ | ✓ |
| A plan per person that changes when they reply | ✗ | ✗ | partly | ✓ |
| Thinks with **your** AI | ✓ | ✗ | ✗ | ✓ |
| Every message waits for your yes | you send by hand | ✗ | optional | ✓, or a yes in advance you set for one kind of reply |
| You can rewrite how it works | prompts | settings | settings | every skill, role and the source |
| Campaign data on your machine | ✗ | ✗ | ✗ | ✓ |
| Specialist roles with a critic and a manager | ✗ | ✗ | rarely | ✓ |
| How you pay | subscription | per seat | per seat | free; pay per send |

Most open-source AI SDR projects are scripts, templates or skill packs. Jason is a running operation: campaign
state, specialist roles, approvals and a supported sending provider, all MIT, built and maintained by the team at
Reply.io.

---

## Who it's for

**Built for**

- **Founders** doing their own outbound, who want to test a market this week without hiring an SDR or buying a seat.
- **Solo GTM operators** who already live in Claude Code and are tired of prompts, spreadsheets and an AI that forgets.
- **Sales Agencies** running a separate campaign per client, each with its own approvals, history and provider account.
- **GTM engineers and RevOps** who want to shape every rule, role and data source, with no vendor roadmap to wait for.
- **Teams with strict rules,** in finance or healthcare, who need their own checklist on every message and a human yes
  on every send.

**Not built for**

- Anyone who wants messages sent with no person deciding.
- Anyone looking for a hosted platform where outreach is designed on screens.
- A team that needs one shared system today: each person runs their own Jason.
- Anyone who needs a promise of meetings or revenue. Jason improves how the work is done, not the offer behind it.

---

## Under the hood

For the technical reader, in one screen.

```text
   you ──── plain words ────►  your agent                ◄──── skills: the GTM trade
                                   │
                                   │  CLI · local Runtime API
                                   ▼
   ┌──────────────────────────── Jason runtime · your machine ────────────────────────────┐
   │  campaigns · people · per-person plans · approvals · questions · do-not-contact      │
   │  append-only history · dispatcher → nine roles, launched in your agent host          │
   └──────────────────────────────────────────┬───────────────────────────────────────────┘
                                              │  plugins: isolated, granted, digest-checked
                                              ▼
                    Reply · your own provider · ntfy  →  email · LinkedIn · SMS · your channels · inbox
```

- **One executable, three modes.** The runtime, which owns all state; the CLI, a client of its local API like the
  browser UI and your agent; and the plugin host, where plugin code runs. The source is public under MIT.
- **You choose the host.** A short execution profile names which agent runs the work, and different work can run on
  different profiles. You sign in to the host yourself, and the profile has nowhere to put a secret. The
  [host matrix](https://github.com/reply-team/jason-ai/blob/main/docs/execution-profiles.md#12-the-hosts) says what was
  measured on each host and what wasn't.
- **JavaScript for what you build, C# underneath.** Plugins and everything you are expected to change are plain
  JavaScript, run by the engine the runtime embeds. The runtime is C# on .NET, shipped self-contained, so you don't
  need .NET installed; its code is fully open. [More on languages](#plugins-are-javascript).
- **Durable by design.** Work items are claimed under a lease with one live attempt at a time. Migrations back up
  first. An update writes each step to a ledger before doing it, so one that dies halfway is finished by the next
  `jason update apply`, or put back with `jason update rollback`. Send the same create twice with one idempotency key, and you get the first result back, not a second copy.
- **Contracts, not glue.** Every provider operation is a published, machine-readable contract, checked when the work is
  created and again when it runs.
- **Nothing is routed until you route it.** A fresh runtime sends work nowhere. A route says which plugin does an operation
  for a campaign and from which account, and each attempt records the package, its digest and the account it ran under.
- **Plugins can't decide.** They run in their own short-lived process, only with the programs and hosts you granted.
  A provider plugin is handed work only after you approved it, and no plugin can approve, answer or clear anything.
- **It fixes itself through your agent.** Ask "is Jason ready?" and your agent runs the self-check: six required checks,
  from the runtime answering to `jason` being on your PATH, each naming its fix.
- **A support file you can read first.** `jason doctor --dry-run` lists what the file would hold. It leaves out prompts,
  request bodies, the database and your `~/Jason` folder, replaces your home directory and account names with marks, and
  never sends itself anywhere.

```sh
jason status --human            # read-only: can this installation work? each check names its fix
jason ui open                   # the browser UI: attention · campaigns · people · performance
jason approval list --human     # read-only: everything that waits for your yes
jason approval approve <id> ... # your yes for one item, under your name
jason update apply              # self-update, with skills and UI; `jason update rollback` undoes it
jason profile create local-codex --host codex --program codex  # run background roles on Codex
```

**What leaves your machine:** what the plugins you install send to their services, whether a provider, a notification
topic (ntfy publishes the pointer to a topic, on the public server unless you name your own) or a receiver you name
for journal lines; the Reply plugin's calls name Jason and its version as their client; what your agent sends to its
model; a check for a newer release five minutes after the runtime starts and then once a day (one request carrying
only the version, to an address Reply.io operates, which you can turn off); and installs and updates you start
yourself. Nothing else. No telemetry.

---

## Documentation

Everything below is public, and every page is written to be followed by a person or by an agent.

**In numbers:** 20 guides · 30 operation contracts, each in Markdown and machine-readable JSON · 942 contract fixtures and
430 API fixtures · a catalogue of 325 SDR operations in 21 families · 9 business skills, 8 runtime skills and 9 role
skills · release notes on every release.

| To… | Read |
|---|---|
| Understand why Jason exists, and who it isn't for | [Philosophy](https://github.com/reply-team/jason-ai/blob/main/docs/philosophy.md) |
| Install it, update it or take it off | [Install guide](https://github.com/reply-team/jason-ai/blob/main/docs/INSTALL.md) · [Release and update](https://github.com/reply-team/jason-ai/blob/main/docs/release-and-update.md) · [Jason Desktop](https://github.com/reply-team/jason-ai/blob/main/docs/desktop.md) |
| Run the installation day to day: the runtime, the CLI, the plugins, the ecosystem | [Handbook](https://github.com/reply-team/jason-ai/blob/main/docs/handbook.md) |
| Walk the whole path once, from empty to one approved operation | [The wiring check](https://github.com/reply-team/jason-ai/blob/main/docs/golden-path.md) |
| See how the pieces fit | [Architecture](https://github.com/reply-team/jason-ai/blob/main/docs/architecture.md) · [Work execution](https://github.com/reply-team/jason-ai/blob/main/docs/work-execution.md) · [Campaign manager](https://github.com/reply-team/jason-ai/blob/main/docs/campaign-manager.md) · [Projects](https://github.com/reply-team/jason-ai/blob/main/docs/projects.md) |
| Connect a provider or add a channel | [Plugin guide](https://github.com/reply-team/jason-ai/blob/main/docs/plugins.md) · [Contracts](https://github.com/reply-team/jason-ai/blob/main/docs/contracts/README.md) · [Routing](https://github.com/reply-team/jason-ai/blob/main/docs/routing.md) · [Reply plugin](https://github.com/reply-team/jason-ai/blob/main/plugins/reply/README.md) |
| Choose which agent runs the work | [Execution profiles](https://github.com/reply-team/jason-ai/blob/main/docs/execution-profiles.md) |
| Build on the runtime | [Runtime API](https://github.com/reply-team/jason-ai/blob/main/docs/runtime-api.md) · [Events](https://github.com/reply-team/jason-ai/blob/main/docs/events.md) · [Inbound sync](https://github.com/reply-team/jason-ai/blob/main/docs/inbound-sync.md) · [Reports](https://github.com/reply-team/jason-ai/blob/main/docs/reports.md) |
| Get told when something waits, and decide it | [Notifications](https://github.com/reply-team/jason-ai/blob/main/docs/notifications.md) · [Browser UI](https://github.com/reply-team/jason-ai/blob/main/docs/ui.md) |
| Read or change how the work is done | [Skills](https://github.com/reply-team/jason-ai/blob/main/skills/README.md) · [Business skills](https://github.com/reply-team/jason-ai/blob/main/skills/business/README.md) · [Runtime skills and roles](https://github.com/reply-team/jason-ai/blob/main/skills/runtime/README.md) |
| Know what leaves your machine, and what stays your job | [Responsible use](https://github.com/reply-team/jason-ai/blob/main/docs/responsible-use.md) · [Security policy](https://github.com/reply-team/jason-ai/blob/main/SECURITY.md) |
| Fix something | [When something is wrong](https://github.com/reply-team/jason-ai/blob/main/docs/handbook.md#when-something-is-wrong) · [Troubleshooting skill](https://github.com/reply-team/jason-ai/blob/main/skills/runtime/troubleshooting-jason/SKILL.md) · [Open an issue](https://github.com/reply-team/jason-ai/issues/new?template=problem.yml) |
| Follow what changed, or contribute | [Release notes](https://github.com/reply-team/jason-ai/blob/main/RELEASE-NOTES.md) · [Contributing](https://github.com/reply-team/jason-ai/blob/main/CONTRIBUTING.md) · [Development](https://github.com/reply-team/jason-ai/blob/main/DEVELOPMENT.md) |

---

## FAQ

**Does it ever send on its own?**
No. Every message to a real person waits for your approval. One nuance, said plainly: if a campaign enrols someone
into your provider's own sequence, you approve that enrolment once for that person, and the provider then sends the
sequence's steps. And if you choose to, you can pre-approve drafted replies to one category of reply in one campaign,
and let the searches that top up one of a campaign's audiences find more people up to a number you set: a search sends
nothing, though it spends your provider's credits. No role can turn either on.

**What if my laptop is closed?**
Jason pauses. When you're back, it picks up where it left off and checks for new replies. Nothing is lost, and nothing
is sent twice. `jason runtime autostart enable` starts it when you log in, with no admin rights.

**Which AI does it work with?**
Any agent with a shell can run your session through Jason's CLI and skills, on its own subscription. The nine background
roles run on Claude Code, measured on real runs; on Codex, measured on Windows but not yet run on an account (its
sandbox there needs an administrator once); or on Cursor CLI, GitHub Copilot CLI, Gemini CLI or Antigravity CLI, built
from each host's documentation and not yet run on an account. The skills follow the open Agent Skills
format.

**Do I need Reply?**
No. You need a provider for anything to reach a person. Reply's plugin ships ready, with data, mailboxes, email, LinkedIn
and the inbox in one account. Any other provider connects through a plugin, and data tools work through your agent.
Without a provider, Jason can still plan and keep records; it just sends nothing.

**Can I send from Gmail, Microsoft 365 or SMTP?**
Yes. Connect them to Reply and they work with no code. To send through them without Reply, you or your agent writes a
small plugin against the published contracts; no ready-made one ships today.

**Can I add my own channel?**
Yes, any of them. Providers are plugins, and channel names are open. Ask your agent to write the plugin from the
[author's guide](https://github.com/reply-team/jason-ai/blob/main/docs/plugins.md). Your agent can also use any tool it's connected to and record what it did in
Jason.

**Which language do I write plugins in?**
Plain JavaScript: ES modules the runtime runs in its own embedded engine, with no Node and no build step. The Reply
plugin that ships ready is written the same way. The runtime is C# and fully open. See
[Plugins are JavaScript](#plugins-are-javascript).

**Does Jason find leads?**
Yes, through Reply's Live Data API or through your own search tools. Jason has no contact database of its own.

**Does Jason warm up mailboxes?**
Through your provider: Reply's warm-up, or another such as TrulyInbox, Lemwarm or Warmy. Jason itself brings the
deliverability skills, the Deliverability role and the stop rules.

**How do I connect my CRM or another tool?**
Connect it to Claude Code, or your agent, as an MCP server, CLI or API, and it's connected. HubSpot, Salesforce and
Pipedrive also sync two ways through Reply.

**Do I need an API key for the model?**
No. Jason runs through your agent under your own login, so your subscription covers the thinking, within its usage
limits.

**I'm already a Reply customer. What changes?**
Nothing you have to move. Jason works on the Reply account you already have. Keep your sequences running and try Jason
on one new group of prospects.

**Is it open source?**
Yes. Since 0.24.0 the whole product is MIT, the runtime included, and its
[source and releases](https://github.com/reply-team/jason-ai) are public. The C# runtime's code is fully open, to read, to
check and, when needed, to change; the code you write day to day, a plugin, is plain JavaScript.

**Something's wrong. What do I do?**
Ask your agent "what's wrong with Jason?". It runs the self-check and the fix it names. Still stuck? `jason doctor`
writes a file made to be shared, which you can read first and which never sends itself, and prints the address of an
[issue form](https://github.com/reply-team/jason-ai/issues/new?template=problem.yml), or write to support@reply.io.

---

## Philosophy

**The line of contact is the work.** Outbound happens per person, over weeks: chosen, read about, written to, answered,
remembered when they say no. A tool that only sends messages doesn't hold that line. Jason does.

**Your AI thinks. Jason remembers. Your provider delivers.** Each part does one job, and none of them pretends to be the
others.

**Autonomous doesn't mean unsupervised.** Every operation that reaches a real person waits for a person's word, given on
the item or, for one kind of reply, in advance. That's written into the operation's contract, and no role can give it.

**Honest over smooth.** An interrupted send is never guessed at: it repeats only where the provider can't send it twice, and otherwise comes back to you. A role that isn't sure asks. The
history is append-only, so what happened stays what happened.

**Vendor-neutral by position, not by accident.** No provider is built into the runtime. Reply's plugin goes through the
same public door as anyone else's.

**General knowledge stays general.** Skills carry the profession for everyone. Your campaign's findings stay your
campaign's, and are never passed off as universal truth.

**No promises we can't keep.** Jason gives you continuity, control and a sales team's know-how. It won't rescue a weak
offer, bad data or a burnt domain, and it doesn't pretend to.

---

## Getting help

Something doesn't work? Open an [issue](https://github.com/reply-team/jason-ai/issues/new?template=problem.yml) with
the file `jason doctor` wrote attached. A question or an idea? Start a
[discussion](https://github.com/reply-team/jason-ai/discussions). Help with your own data, or a file made with `jason doctor --include-journal`,
goes to **support@reply.io** and never to an issue: an issue is public the moment it's submitted. A vulnerability goes
the way [SECURITY.md](https://github.com/reply-team/jason-ai/blob/main/SECURITY.md) says.

---

## License

**MIT.** Everything in [reply-team/jason-ai](https://github.com/reply-team/jason-ai) is under the
[MIT licence](https://github.com/reply-team/jason-ai/blob/main/LICENSE) from 0.24.0, the runtime included, along with the skills, plugins, contracts, docs, install
scripts and UI. ReplyApp Inc. releases this repository, its history included, under the MIT licence from 0.24.0, and
the licence files in older trees are superseded.

"Jason", "Jason AI", "Reply" and "Reply.io" are trademarks of ReplyApp Inc.

<div align="center">
<br>

**Your AI does the work. You keep the send button.**

Built by the team at [Reply.io](https://reply.io) · Questions: support@reply.io

</div>
