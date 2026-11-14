---
name: quick-start
description: Use when a person has just had Jason installed and wants to be taken to their first campaign, when an install prompt says to start the quick-start skill, or when somebody with no campaign yet asks where to begin. It asks one question, reads which channels are routed instead of asking, and says the plan in one message.
metadata:
  status: draft
---

# Quick start

You take a person from a ready installation to a first campaign in a few minutes. Do not explain Jason first: begin
with step 1. **Status: draft.** Its commands parse against this build.

## 1. One question

Send this, with nothing before it, as free text and not through a question tool:

> What do you sell, and who should buy it? One sentence is enough, and a link to your site works too.
>
> If you like, add a line about yourself and how you write. You can also do that later.

A no, or silence about the second part, is an answer. Do not ask for it again while sends are manual.

## 2. Read the channels, never ask for them

Make a project named from what the person sells, or take the one that already holds it, and make the campaign in it,
under a name drawn from what the campaign is for, with the offer and the audience in their own words as its context
(`managed-campaign-work` says how). Neither is asked about. Then ask the runtime which provider each channel goes
through. Never ask for them: the person is not asked which channels they have. Make each idempotency key new per run.

```
jason project list --query payroll
jason project create --name "Payroll for Latin America"
jason campaign create --name "Seed-stage owners in Mexico and Colombia" --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --context '{"offer":"payroll for startups hiring in Latin America","audience":"owners of seed-stage startups in Mexico and Colombia"}' --idempotency-key payroll-latam-owners-20261114-1530
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation message.send --channel email
jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation message.send --channel linkedin
```

A channel with a route has a provider, which the answer names; one with no route is not connected. A route does not
prove that an account is connected, and a draft campaign cannot read accounts, so read no accounts and count none
before "go". The channels decide the form of the first message: email only; LinkedIn only, where it is an invitation
and not a letter; or both, and then several channels together. Draft nothing before they are known.

## 3. One message, one stop

Send one message that holds the plan: the goal, who Jason will look for, the channel or channels, how it will write
("short, plain, in your name"), and the state of the channels. Say in passing what was made, as a draft: the project
and the campaign, and that both names are the person's to change. End it with exactly: "Say what to change, or say go." It
carries no list of questions and uses no question tool; only when the person asks to change something do you use the
question tool, with the recommended choice first.

- **A channel has a route.** Say: "Email goes out through {provider}." and "LinkedIn goes out through {provider}.", or
  one sentence when both go through the same provider. Then: "Which mailboxes and LinkedIn accounts are connected
  there I read as soon as you say go."
- **A channel has no route.** Say: "{Channel} is not connected."
- **Neither has a route.** Say it firmly: no channel is connected, connecting your provider now is recommended and is
  one step, and until it is done not one message leaves. That step is the person's one act, "connect your provider".
  With Reply it is one sign-in only they can do: in a terminal `reply auth login` (on Windows PowerShell, `reply.cmd auth login`), and you do the rest (the CLI, the
  plugin, its grant and its route: the Reply CLI section and steps 3 to 5 of `docs/golden-path.md`); in the window,
  the Accounts page. Then say: "It can be done later."

## 4. A sample only on request

If the person puts the channels off, answer in a reply of its own and offer, in one sentence, to show what a first
message would look like. Never put the offer in the plan message. Show a sample only to a person who accepts, and call
it a sample. Nobody who did not ask sees one.

## 5. After "go"

Nothing is started before the person's go. From "go" the work goes on through `planner-in-session` for the plan and
`managed-campaign-work`, which owns starting the campaign, the people and the work. `planner-in-session` takes the plan
from the campaign that exists: the brief and the goals go into its context, and no second campaign is made. Then you
start the campaign and submit `sender.list` for each routed channel (`sender_channel` is `email` or `linkedin`), since
a draft cannot run it:

```
jason campaign update-context cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --set '{"goals":[{"id":"demo","kind":"demo","title":"A demo with the operations lead","reached_when":"a demo is on their calendar","order":1}]}' --reason "the plan agreed with the person"
jason campaign start cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation sender.list --input '{"owners":"own","sender_channel":"email","limit":50,"offset":0}' --idempotency-key first-campaign-20261114-1530-senders-email
```

Say its answer in one line before anything else: "{N} mailboxes and {M} LinkedIn accounts are connected; you can add
more at any time." With one channel routed, name only that channel's accounts. Count the accounts whose `status` is
`active`, and the others apart. A routed channel that holds no account is said firmly: not one message leaves on it
until an account is connected, and name the one step as in step 3. Every first message still waits for the person's
yes (`approval-boundaries`). The message after "go" that hands over to the planning ends with one line: "There is a
guided check of the wiring; say so if you want it."

## Rules

- Never ask for a preference. A preference is set only on the person's own word, never written from what you assume
  or already know of them; you may say "I take it you are ..., tell me if not". Do not remind them of preferences
  while sends are manual.
