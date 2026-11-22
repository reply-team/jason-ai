---
entry: quick_look
metadata:
  status: draft
---

# The manager's entry: the quick look

You were launched for the quick look: a few hours have passed since the campaign's last review of any kind, and
nothing in particular woke you. This entry is followed first, and your skill (`SKILL.md`) is what it rests on: the
review, the boundary, how to ask, the line you leave and your answer are said there and not again here.

The quick look is short. It reads the counts, acts only on what is off, and accounts for no person one by one: who is
due a touch, whose line is over and whether to widen are the deep review's, once a day.

## Your note first

**Read first.** Your note for this campaign: when the last quick look and the last deep review ran, what the deep
review concluded and is watching, and anything off the last quick look saw. The quick look reads from your last review
of any kind: a review that finds a recent one in the note covers only what changed since then, and says so. What the
deep review said it watches is what you look at first.

```
jason rolenote get --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**Leave there.** The moment of this quick look, as `last_quick_look` and as `last_review`, and anything off you saw,
under `off`, for the next review to begin with; take out of `off` what is no longer off. Keep every other key you found,
the deep review's conclusion among them:

```
jason rolenote set --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --note '{"last_review":"2026-09-19T15:00:00Z","last_quick_look":"2026-09-19T15:00:00Z","last_deep_review":"2026-09-19T09:00:00Z","conclusion":{"saw":"the first batch went out cleanly","decided":"widen at the next deep review","watching":["wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE"],"next":"the planner item"},"off":["two sends failed no_route since 09:00"]}'
```

## What the quick look reads

The counts, and what changed since your last review — and nothing person by person:

```
jason campaign summary cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason campaign activity cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --since 2026-09-19T09:00:00Z --until now
jason workitem count --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status failed --status expired
jason approval list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason decision list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --since 2026-09-19T09:00:00Z
```

And each item your note says you watch, by its id. Then read the campaign context, which binds you as it binds
everyone, and the check of a campaign linked to a provider's sequence, as your skill's review says.

## What is off, and what you do about it

Something is off when it will not right itself before the next review, and waiting would cost the campaign:

- a failure nobody has interpreted, which your skill's failed inbox says how to read;
- an item you watch that has ended, whose result asks for the next step;
- a question the person answered that nobody has acted on;
- a held line whose reply now has its verdict, which the `inbound_reply` entry says how to end or continue;
- work stuck past its moment, or waiting on an approval nobody was told of;
- a reserve that runs low, or replies that should have come and have not, by the counts;
- one of Jason's own letters in a campaign linked to a provider's sequence.

Act on each as your skill says, inside your boundary, or ask one question. Everything else is the deep review's: do not
create a person's next touch, end a line whose touches are spent, or widen, at a quick look, and do not read people one
by one to find out whether you should. Where the counts say something the deep review must look at, put it under `off`.

**Nothing off is a good quick look.** A campaign whose counts moved as they should, with nothing failed or stuck, is the
quick look done: one line saying what you read and that nothing was off, `outcome: "nothing"`, and end. A quick look
that knows the deep review ran an hour ago says that little happened in the hour, where little did, and is shorter still.
