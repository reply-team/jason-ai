---
name: quick-start
description: Use when a person has just had Jason installed and wants to be taken to their first campaign, when an install prompt says to start the quick-start skill, or when somebody with no campaign yet asks where to begin. It asks one question, reads which channels are connected instead of asking, and says the plan in one message.
metadata:
  status: draft
---

# Quick start

You take a person from a ready installation to a first campaign in a few minutes. Do not explain Jason first, and
do not ask what comes next: begin with step 1.

**Status: draft.** No agent host has read this text in the state it ships in. Its commands parse against this build.

## 1. One question

Send this, with nothing before it, as free text and not through a question tool:

> What do you sell, and who should buy it? One sentence is enough, and a link to your site works too.
>
> If you like, add a line about yourself and how you write. You can also do that later.

A no, or silence about the second part, is an answer. Do not ask for it again while sends are manual.

## 2. Read the channels, never ask for them

When the answer comes, make the campaign in the default project, with the offer and the audience in their own words as
its context (`managed-campaign-work` says how a campaign is made; leave `--project` out):

```
jason campaign create --name "First campaign" --context '{"offer":"payroll for startups hiring in Latin America","audience":"founders of seed-stage startups in Mexico and Colombia"}' --idempotency-key first-campaign
```

Then ask the runtime which channels a send would go through, for each of `email` and `linkedin`:

```
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation message.send --channel email
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation message.send --channel linkedin
```

A channel with no route is not connected. For each channel that is routed, read how many accounts it has with one
`sender.list` item, and read its result with `jason workitem get`:

```
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation sender.list --input '{"owners":"own","sender_channel":"email","limit":50,"offset":0}' --idempotency-key first-campaign-senders-email
```

Count the accounts whose `status` is `active`; count the others apart. The count never holds the person up: if the
read fails or is slow, say which channels are connected without numbers and go on. Never ask which channels they have.

The channels decide the form of the first message: email only; LinkedIn only, where it is an invitation and not a
letter; or both, and then several channels together. Draft nothing before they are known.

## 3. One message, one stop

Send one message that holds the plan: the goal, who Jason will look for, the channel or channels, and how it will
write ("short, plain, in your name"), and the state of the channels. End it with exactly: "Say what to change, or say
go." It carries no list of questions and uses no question tool. Only when the person asks to change something do you
use the question tool, with the recommended choice first.

- **Channels are connected.** One line of fact: "You have N mailboxes and M LinkedIn accounts connected; you can add
  more at any time." Not a question. Say the accounts that are not active in the same line.
- **None is connected.** Say it firmly in this same message: no channel is connected, connecting one now is
  recommended and is one step, and until it is done not one message leaves. Name the step for where the person is:
  in a terminal, `operating-the-installation` and the Reply CLI section of `docs/golden-path.md` say how; in the
  window, the Accounts page. It can be done later. Close with one sentence offering to show what a first message would
  look like.

## 4. A sample only on request

Show a sample message only to a person who asked for one, and call it a sample. Nobody who did not ask sees one.

## 5. Then the work goes on

From "go" the work goes on through the skills that own it: `planner-in-session` for the plan and
`managed-campaign-work` for the people and the work. Every first message still waits for the person's yes
(`approval-boundaries`).

## Rules

- Never ask for a preference. A preference is set only on the person's own word, never written from what you assume
  or already know of them; you may say "I take it you are ..., tell me if not". Do not remind them of preferences
  while sends are manual.
- End your last message of the quick start with one line: "There is a guided check of the wiring; say so if you want it."
