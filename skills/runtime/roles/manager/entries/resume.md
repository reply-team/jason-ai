---
entry: resume
metadata:
  status: draft
---

# The manager's entry: a start after a pause

You were launched because a person started the campaign again after a pause. This entry is followed first, and your
skill (`SKILL.md`) is what it rests on: the review, the boundary, how to ask, the line you leave and your answer are
said there and not again here.

## Your note first

**Read first.** Your note for this campaign: the last deep review's conclusion, what you asked and were watching when
the campaign stopped, the batch in flight, the replies you were following, and when you last reviewed. That moment is
where this review reads from: everything since it is what the pause held. A review that finds a recent one in the note
— a pause of an hour, say — covers only what changed since then, and says so.

```
jason rolenote get --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD
```

**Leave there.** When the campaign resumed, as `resumed_at`, and the moment of this review as `last_review`; what you
now watch and ask, where it changed. Keep every other key you found, the last deep review's conclusion among them:

```
jason rolenote set --role manager --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --note '{"last_review":"2026-09-22T09:00:00Z","resumed_at":"2026-09-22T08:58:00Z","last_quick_look":"2026-09-19T15:00:00Z","last_deep_review":"2026-09-19T09:00:00Z","conclusion":{"saw":"three bounces from the new domain","decided":"paused the campaign and asked","watching":[],"next":"the person to start it again"},"watching":["wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRE"]}'
```

## A start after a pause is not a new campaign

Work the pause held runs again by itself, and the people in flight keep their lines: a line that was going on goes on,
a held one stays held for you to end or continue, and an ended one stays ended. Nothing is planned again from the
start. The line that woke you is the start, and the one before it is the pause, with its reason:

```
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind campaign_started --since 2026-09-18T09:00:00Z
jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind campaign_paused --since 2026-09-18T09:00:00Z
```

**Read what changed while it stood.** From the last review your note names, as your skill's review says: replies, and
the lines they held, read as the `inbound_reply` entry says (the file `entries/inbound_reply.md` beside your skill);
failures; questions answered while it stood; people added; lines ended or continued by somebody else. Where you paused
it yourself for an incident, read what the person answered and what they set before they started it again, and do not
undo it: their start is their word that what you asked is settled.

**What it then needs.** Where work stands ready to go again, say so in your line and leave it. Where people in flight
have touches that fell due while the campaign stood, they are the next deep review's, which comes one grid interval
after this resume, a day by default: the daily grid starts again from it, so the days the campaign stood are not deep
reviews it owes. Plan a first batch only where nobody in the campaign was ever touched, and then as the `start` entry
says (the file `entries/start.md` beside your skill). Widening to a next batch is the deep review's, never this
review's.

A campaign that stood is the one moment a person most wants to hear that it moved again: say in your line, in a
sentence they can read, what was held and what now goes on.
