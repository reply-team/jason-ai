# `send_mode.warm` — how what Jason sends to a contact who has answered goes

**Version 1.** The machine-readable document is [`send_mode.warm.json`](send_mode.warm.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **A named preference, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/) and never among them: no work item names it, no route carries it, and no plugin is
> invoked for it. It is a fact about the client that a person states, kept once for the whole installation.

## What it is for

A message, a reply or an invitation that reaches a real person waits for a person's approval, one at a time. This key
is the person's standing choice for the sends to a contact who has answered in the campaign: `automatic`, and they go
without being asked, or `every_message`, and each one waits for a person as it always has.
[`send_mode.cold`](send_mode.cold.md) is the same choice for a contact who has not answered, and each key is decided
on its own: setting one leaves the other as it is.

`automatic` does not mean that nothing waits. A send still goes to a person, as under `every_message`, where it was
written or edited in the desktop's chat; where its writer or a reviewer doubted it; where its contact has a reply
waiting to be judged, or one judged before Jason recorded what kind it was; where it answers an automatic reply, a
bounce or a request to stop, or a conversation marked for the person; where neither the critic Jason made for it nor a
person passed the text it carries, or it was edited after the pass; and where its campaign has already released the
day's ceiling of automatic sends, `Sending:AutomaticMaxPerDay`. Where a standing approval a person set for a category
of reply covers the send, it still releases one held for its contact's waiting reply, for what it answers or for its
review, as it would under `every_message`; it never releases one written in the chat, doubted or past the ceiling. And
no send goes to a contact who asked not to be written to, in any mode: the claim refuses it.
[The send mode at the claim](../../work-execution.md#the-send-mode-at-the-claim) gives the order these are asked in.

## The value

```json
{ "mode": "every_message" }
```

- `mode` is `automatic` or `every_message`. Required.

**Unset means `every_message`.** The catalog keeps no defaults, so an unset key answers null, and nothing goes without
a person's approval until a person sets it. While either key is unset, every approval names the keys still unset in its
`send_modes_unset`, so the question is put where a person is already deciding.

A campaign may hold a mode of its own for its warm contacts, set by a named person with `jason campaign set-send-mode`;
where it does, the campaign's mode is the one in force there, and this key is the one everywhere else. The mode is read
when a send is claimed, never frozen when the work is made: a change applies to the next send.

## Setting it

A preference is the person's word, so only a person sets it, and the request names them as its actor. An agent working
for the person in their own session sets it on their answer, under their name; a request from inside a run is refused
whatever actor it names (`decided_inside_a_run`).

```sh
jason preference set send_mode.warm --value-file mode.json --actor human:Marta
jason preference unset send_mode.warm --actor human:Marta
```

The file `mode.json` holds the value shown above, so the inner quotes reach the runtime on every shell. Unsetting
removes the value, and every message waits for a person again. The installation's record of what changed keeps that it
was set and unset, by whom and when, with the value's size and a hash of it — never the value itself.

## The fields

<!-- BEGIN GENERATED properties -->
<!-- Rendered from send_mode.warm.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| key | `send_mode.warm` |
| version | 1 |

| Field | Required | What it holds |
|---|---|---|
| `mode` | yes | one of `automatic`, `every_message` |

<!-- END GENERATED properties -->

The fixtures under [`../fixtures/preferences/send_mode.warm/`](../fixtures/preferences/send_mode.warm/) are run by the
repository's tests against this document, so an example on this page and the schema cannot drift apart.
