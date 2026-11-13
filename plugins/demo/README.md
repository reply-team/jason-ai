# The demo provider

A provider plugin that reaches nobody. Jason's demo installation, a second installation of Jason on a data directory of
its own, routes every operation to it, so the sample story runs through the real runtime, its approvals, its work items
and its journal, while nothing goes to anyone.

## Why nothing can leave it

The manifest requests no capability at all: no program, no host, no variable, no file and no Runtime API family. The
plugin host hands a package only what its manifest requests and a person grants ([docs/plugins.md](../../docs/plugins.md)
§9), so whatever the settings say, this package can call nothing outside its own process. A send made to it is answered
and goes nowhere.

## What it performs

| Operation | What it answers |
|---|---|
| `message.send` | `sent`, at once, with a reference made from the work item's key; it sends at once and schedules nothing |
| `social_invitation.send` | `sent`, the same way |
| `conversation.reply` | `sent`, into a thread of the story, from the person whose thread it is |
| `sender.list` | two invented accounts of the story's operator: one mailbox, and one LinkedIn account with no address |
| `conversation.list`, `conversation.get` | the story's written threads, once the story has started |
| `events.read` | the story's written replies, by its own cursor |

It takes no `send_after`, so nothing is ever held for later, and it performs neither `message.status` nor
`message.cancel`. A person the story does not hold is given a contact made from the address, the same one every time,
and never a reply.

## The story

[`modules/story.js`](modules/story.js) is the sample story, and it is data alone: one exported literal of strict JSON,
with an invented seller, its operator, four campaigns, eighteen companies and fifty-four people at `.example` addresses
and 555-01xx numbers, the messages written for them, and the replies written to some of those messages. The desktop's
seed reads the same file to create the campaigns and the people, so the people a send reaches here and the people the
seed creates are one set.

## When the replies come

A plugin run keeps nothing between runs, so the replies are written on a schedule. Each one lands at its own offset
after the moment named under `Plugins:Config:demo:story_start` in the demo installation's settings, which the seed
writes once the story's approved sends have gone. Without that moment the story has not started: no thread is listed
and every events read answers an empty page.

Every reply names the person by the contact this package pins when a send reaches them, and names no provider campaign,
so the runtime brings a reply to a campaign only where a send to that person was made ([docs/events.md](../../docs/events.md)
§3). The runtime reads each account on `Dispatcher:EventsCadenceSeconds`, so the demo installation sets it to its least,
sixty seconds, for the replies to arrive in the first minutes.
