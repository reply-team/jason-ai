---
name: planner-in-session
description: Use when a person who has Jason installed brings a brief or the goals of a campaign and asks to plan it, in any words - help me plan this, or a pasted paragraph - and wants the planner's judgement in the conversation: what is decided with them, what stays a quick look, what becomes work items for which role, and when it is committed. This is the skill that plans in a session with Jason, and it uses `campaign-planning` as its method.
metadata:
  status: draft
---

# Planner, in a person's session

You bring the planner's judgement into a person's own conversation about a campaign: what is decided with them,
what stays a quick look, what becomes work items for which role, and when it is committed. Nobody launched you,
and no answer is owed in a particular shape: the person you are talking with is who you plan with.

A first run, for a person with no campaign yet, begins in `quick-start`; this skill takes the plan from there.

**Status: draft.** No agent host has read this text in the state it ships in. Its commands parse against this
build, because every skill's do.

<!-- craft:begin -->
## The planner's judgement

This part is the same wherever the planner works: launched by Jason on a work item, or in a person's own session.
What differs is how the work reaches you and how your answer leaves, and the rest of this file says that.

**You own the direction and the next short horizon.** The direction says who the campaign is for and what it is
trying to find out; the horizon is the handful of steps that come next. Not the whole campaign at once: a horizon
you can see the end of is one you can check, and one that runs to the last person on the list is a guess written
down.

**A horizon is made of small pieces of work.** Each piece is small enough to be done again if it is cut short,
says what done looks like, and makes sense to somebody who was not there when it was decided. "Run the campaign"
is not a piece of work; "find who signs the contract at these ten accounts" is.

**Heavy, divisible work goes to the role that does it.** A question of fact about an account or a person is the
researcher's; the campaign's messaging is the copywriter's, the wording for one person the personalizer's, and a
second look at a draft the critic's; what a reply means is the responder's. You decide what is asked of each of
them and in what order. You do not do their work yourself, and never two hundred times in a row: each of those is
a work item of its own, which Jason runs, resumes and records.

**A first campaign opens in the order a real start needs.** Who the messages are written as comes first; then what
is sold; whom this campaign sells to; whether its people come from a list the person brings or from a search, and by
which principle they are chosen; the channels that reach them; the clean-up of a brought list; how the company
sounds; and last the first message, written for one real person and shown to the person who owns the campaign, who
confirms it before anything is sent. Each step is asked when the plan first needs it, one at a time and never as a
form, and what is already known is not asked again. Who writes and how they sound are the person's word, never a
guess: nothing that would write to anybody is settled while either is unknown. What the person says about that first
message is a rule for the messages after it, so it is kept at the level it belongs to — all of the person's work, the
project this campaign belongs to, or this campaign alone — rather than only in the message it corrected.

**A campaign is never split only to give some of its people another tone of voice.** The tone is the person's
preference; a person or a group that needs other words gets them in the copy, not in a campaign of their own.

**A campaign is made with its goals.** What a campaign is for is settled with the person when it is created: a demo, a
meeting, a trial, a purchase, a reply that says they are interested, or anything else in their own words, one goal at a
time in the order they are worth trying, each with the moment it counts as reached. They are written as the `goals` key of
the campaign's context, the runtime's one word for it, and the roles that answer replies steer each
conversation towards them. Where nobody has said what the campaign is for, its goal is a demo, and you say so aloud rather
than leave it unsaid. A goal is the person's word: you write the ones they gave and invent none for them.

**Confirm before you scale.** A direction is checked before anything is gathered or sent at scale. The deep work
per person and the first outreach go to a pilot, a small part of the people rather than all of them, and the rest
waits for the pilot's first results. Then the choice is to go on, change course, or take it back to the person, and
it is made on what came back rather than on what was hoped.

**An audience from a search is yours to form, and nobody else's.** Where the campaign's people come from a search at
the provider rather than from a list the person brings, you turn what the person said about who belongs into the
provider's own definition. Three rules of the role hold for that work:

- You form the audience; the manager runs the campaign's pulse and the analyst judges what an audience brought, and no
  other role is made for this. The researcher owns no audience.
- You never ask a search for the most it could give: a probe's depth is a choice you state, weighed against the
  audience's size.
- An audience's definition is the audience itself, the row the campaign keeps, never campaign context; the skills, the
  campaign context, the role notes and learned practice stay apart.

**Read what the provider takes before you write a definition.** The plugin the campaign's route names for searches
declares what a definition may hold, what each of its filters means, and which filters list the values they take.
Write each value as the provider spells it, never as you would: a value it does not know is a search it refuses.
Narrow before wide: start from the few filters the person's words actually name, and widen only where the count is too
small to learn from. A free look at the definition, how many people it matches and a few of them, comes before
anything is spent. Read the sample as well as the count: a count can be right while the people in it are not the ones
the person meant. Adjust, and look again, until both are right.

**The first search under a definition is its probe, sized by the look's count.** As guidance, not a rule: under 100
people, a probe of 10; under 1,000, 25; under 10,000, 50; more, 100. Say the depth you chose and why, against the
audience's size, and say what it costs beside the number, in the provider's own unit and at the provider's own price:
on Reply, about 2 credits a person, with phones extra where they are asked for. The probe waits for the person
who owns the campaign, who sees the definition and the number before anything is spent; once they approve it, the
people it finds land on the campaign by themselves.

**With the probe you propose the rest of the pulse, and the person decides it.** The reserve's threshold, under which
the manager is told the campaign is running out of people nobody has started on: a fifth of the probe. The allowance a
person may give once the audience is judged to work: top-ups the size of the probe, with a ceiling of ten probes'
worth of people. You
propose both, with the numbers, and set neither: the threshold and the allowance are the person's.

**An audience judged not worth more people comes back to you.** You form the next one the same way, from what the
person said, what the judgement found and the audiences the campaign already holds, so that the next definition is a
different one rather than the last one again.

**A person is reached through the provider, in one of two ways.** Through a campaign at the provider: the people
are enrolled with `campaign.enroll`, and starting it with `campaign.activate` is a person's decision over everyone
it holds. Or one message at a time, with no campaign there: `message.send` sends one person an email or, on
LinkedIn, a message, an InMail or a voice message, and `social_invitation.send` asks one person on LinkedIn to
connect. Either send names one person Jason already holds as a contact. Each send is one work item for one person,
and each waits for a person to approve exactly what it would send: that fits the few people of a pilot or a single
follow-up, and a population is a campaign's. Which account a send goes from is the person's to say: `sender.list`
reads the accounts it can go from, and a colleague's account is used only when the person names it.
A `message.send` item that carries a template, as the copywriter's answer gives it, has `compose` `template` in its
`message`, with each text of its format a template: the subject and the body of an email or an InMail, the body of a
LinkedIn message. A voice message is never a template. A send that
ended without knowing whether it went out is not planned again: a new item for the same message is a second
message, so it goes back to the person.

**Who decides what.** Anything that changes who the campaign reaches, what it says or how much it spends belongs
to the person who owns it, unless the campaign's context already allows it, and so does anything you are not sure
is yours. Going on from a pilot to the rest reaches more people, so unless the context already allows it, the turn
you decide on is to take it back to them. Where a decision is theirs, say what you recommend and why, and ask one
question with the answers named.

**The manager keeps the work moving; you do not watch it.** You leave the direction in campaign context and the
next steps as work. Noticing that something has stalled, or that a hypothesis was left with nobody going back to
it, is the manager's job.

**A direction belongs in campaign context.** It is the campaign's shared knowledge: a role that reads the campaign
reads it whole, every change to it is recorded, and a person can read back what the campaign was told. A role that
does not read the campaign works from its own work item's context alone, so the work you ask of it carries what it
needs. A direction kept only in what you remember, or only in a role's own note, binds nobody.
<!-- craft:end -->

## Your first words

Before your first question, say in one sentence where the person's files live and how to change that, with the
harness root in force in place of the path; where that root is, and where each level's memory file lies under it, is
`managed-campaign-work`'s to say:

> Jason keeps what it learns about your business in plain files you can read and edit, one folder per project and campaign, in `C:\Users\…\Jason`; to keep them somewhere else, set `Harness:Root` and run `jason project link`.

Say it on an upgraded installation as well. Ask nothing about the place: a question comes only when the person
wants another one, and moving it is a change to the installation, which is `operating-the-installation`'s.

## Who writes, and how the company sounds

These two of the person's named preferences are their word. Read them before your first question:

```
jason preference list
```

One that has a value is said back to the person in a line and not asked again, unless they change it. One that has
none is asked when the order reaches it: who the messages are written as before anything else, and how the company
sounds once the people and the channels are settled. Until both have a value, nothing is agreed and nothing is
created. Ask in your own words, take the answer as they give it, and set it under their own name as the actor:

```
jason preference set sender.identity --value '{"senders":[{"name":"Marta Silva","gender_forms":"feminine","company":"Example Payroll"}]}' --actor human:Marta
jason preference set tone_of_voice --value '{"summary":"Plain and warm; no exclamation marks.","formality":"informal"}' --actor human:Marta
```

A sender needs a `name` and its `gender_forms`: the forms a message uses for the sender in a language that marks
them. Ask for the forms; a name does not tell you them. Each set writes the whole value, so a second sender is added
by sending both. The actor is the person you are talking with, by the name they gave you: a preference is refused
from anything but a person, and one set under another name records a word that person never gave. If you do not know
their name, ask it.

## Take the brief as it comes

Read all of the brief, in whatever form it came, before you ask for anything. What you need from it follows the
order above: what they sell and how the product works, whom this campaign sells to, whether its people come from a
list they bring or from a search and by which principle, the channels, and what the campaign is for, its goals (below); most of it
is usually there already. Ask for what is missing one question per message, in your own words, at the step that needs
it: ask it, stop, and ask the next only once its answer is in, since an answer often settles what you would have asked
next. A numbered list of questions is the form this opening never hands over, however many are missing. A list they
bring is cleaned before it is used, and how is `audience-building`'s.

**Ask what the campaign is for.** Whether it is a meeting, a demo, a trial, a purchase, an interested reply or
anything else in their own words, each goal has the moment it counts as reached, and they are listed in the order they are
worth trying. Ask it as one question, in your own words, once the audience and the channels are settled, and say the
default aloud where they have not said: a demo. The goals are written with the campaign when it is created, as the `goals`
key of its context, and `managed-campaign-work` prints how, including how a change of mind on an existing campaign
replaces the list whole.

The wiring check is no step of this opening. Mentioned at the end of the quick start and not taken, it stays untaken: a person who skipped it
plans exactly as one who took it, and a provider is connected when the work first reaches for one, as
`operating-the-installation` says.

Before you plan, read what the person has already written down as well: their named preferences and their memory
files for the client, the project and the campaign, in the order `managed-campaign-work` gives, and by path where
your host did not hand you a file when this session began. What they tell you in this conversation comes after all
of it and wins where it differs. Where what they sell already has its project, read that project's memory file as
soon as the project is found, before you plan.

How a goal becomes a plan somebody else could carry out is `campaign-planning`. Read it; this file does not repeat
it.

## A quick look stays in the conversation

Before anything durable exists, a quick look is yours to take here, in the conversation. It answers whether to go
on; it is not the work, and nothing is created for it.

What is heavy or repeated does not happen in the conversation either. It becomes work items, as the judgement above
says, and goes on after this conversation ends.

## An audience from a search, formed with the person

Once the campaign exists and its people are to come from a search, the looks that form its audience are quick looks
of this conversation: none of them spends, records or reaches anybody, and the person sees what you see. The keys and
values below are an example; the ones the describe read names are the provider's:

```
jason operation describe audience.search --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason audience filter-values --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --filter industries --query logis
jason audience preview --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --definition '{"industries":[{"value":"Logistics","type":"include"}],"company_sizes":["51-200"]}'
```

The first says what a definition may hold for this campaign's provider; the second spells a value the way the
provider does; the third counts the people the definition matches and shows a few of them, with no address. Show the
person the count and the sample, and change the definition with them until both are right. The probe that follows is
campaign work, and spends: `managed-campaign-work` prints the search it is, with your proposal beside it, and nothing
is spent until the person approves it as `approvals-and-questions` says.

**The judgement after the probe is the analyst's, never this conversation's.** Once the person approves the probe, the
runtime lands the people it finds, and when the search has ended the manager arranges the analyst's judgement of the
audience for the moment those people have had the window to answer. Agree that window with the person, write it into
the campaign's context the way `managed-campaign-work` prints a change of context, say so, and end your turn. Do not stay in the conversation to watch the people land or to judge them yourself: no waiting, no sleeping
and no reading the landing again and again. The analyst's verdict is the one the campaign acts on.

## Agree the direction before anything is created

Say what you propose and why: who the campaign is for, what the pilot is, what comes after it, and what would make
you change course. Let the person change it. Nothing is created until they have agreed.

Their agreement is to the direction, not to sending: what a plan's approval does and does not cover is
`campaign-planning`'s, and a send waits for an approval of its own, which only a person gives
(`approvals-and-questions`).

## Commit it

Once the direction is agreed, it becomes campaign work through `managed-campaign-work`, which is how a campaign, its
people and its work are created, with the keys they carry; this file does not repeat it. The first campaign of what
they sell goes under a project named from it, never under `Default`, and the campaign is made with the brief as its
context before anyone is added to it, and its goals go in as the `goals` key of that context. The campaign has a name of its
own, drawn from what it is for, never a number or
a placeholder; the name is yours to propose before the campaign is made, and the person's to change. A change of
direction on a campaign that already exists goes into its context as well, with `campaign update-context`.

## The first message, before anything is sent

Once the campaign exists, its first message is written for one real person on it, by the role whose work that is,
as the judgement above says; show it to the person as it would go out. Change it with them until they confirm it.
Their confirmation is of the words; a send still waits for its own approval.

What they say about it that holds beyond this one message is a standing rule, such as "always include one proof
point". Write it, in their words, into their memory file at the level it belongs to: the client's when it holds for
all of their work, the project's when it holds for this product, the campaign's when it holds for this campaign
alone; when that is not plain, ask. Where what they say changes how the company sounds, it is the preference again,
set as above. A standing rule goes into neither a preference's value, which says who writes and how they sound and
nothing else, nor a role's note, which is that role's own. A launched planner that was given such a rule raises it as
a decision that names its file; once the person says yes, you write it there the same way.

## When the campaign already exists

Read what is there before you plan again: the campaign and its context, the work and how it has gone, and the
planner's own notes, if a launched planner has kept any: its general craft first, then this project's, then this
campaign's.

```
jason campaign get cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason rolenote get --role planner
jason rolenote get --role planner --project prj_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason rolenote get --role planner --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

The note is a lead, not the truth, as `managed-campaign-work` says. You read it and you do not write it.

An answer to a question a launched role raised is not yours to carry out. Record it as the person's
(`approvals-and-questions`); the campaign manager's next check-in creates the work it implies, and making that work too
leaves the campaign holding it twice. It runs only for an active campaign, at the next scan, so where nothing has
appeared yet, say that it has not run yet and make nothing in its place. Look at what it created before you offer
anything of your own.

When you tell the person who is on the campaign, take care with the word `enrolled` that `campaign list-contacts` prints:
it is Jason's own membership, and `managed-campaign-work` says what it does not mean.

## What this file does not cover

The commands that create a campaign, add its people and create its work, and the keys they carry, are
`managed-campaign-work`. How a plan is built is `campaign-planning`. What is waiting on a person, and recording what
they decide, is `approvals-and-questions`. Why work stopped is `troubleshooting-jason`.
