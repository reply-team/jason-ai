# `sender.identity` — who messages are written as

**Version 1.** The machine-readable document is [`sender.identity.json`](sender.identity.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **A named preference, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/) and never among them: no work item names it, no route carries it, and no plugin is
> invoked for it. It is a fact about the client that a person states, kept once for the whole installation.

## What it is for

Every message Jason helps write is written as somebody. This preference says who: the people a message may be signed
by, how a recipient addresses them, and the grammatical forms a message uses for them in a language that marks them. It is kept once, so that a skill can check that it is set before it writes anything for a person to send, and a screen can show it.

Several senders are normal. **The first in the list writes whenever a campaign names no other**; a campaign that
should be written by somebody else names that sender by `name` or by one of their `aliases`.

## The value

```json
{
  "senders": [
    {
      "name": "Marta Lind",
      "addressed_as": "Marta",
      "gender_forms": "feminine",
      "title": "Head of Partnerships",
      "company": "Example Analytics",
      "aliases": ["Marta", "M. Lind"],
      "details": "Writes in the first person; signs with her first name only."
    },
    {
      "name": "Sam Ortega",
      "gender_forms": "neutral",
      "title": "Founder",
      "company": "Example Analytics"
    }
  ]
}
```

- `name` is how the sender signs. Required.
- `addressed_as` is the name a recipient uses for them, where it is not the whole name.
- `gender_forms` is `feminine`, `masculine` or `neutral`: the forms a message uses for the sender in a language that
  marks them, where "I was glad to hear from you" ends one way when a woman writes it and another when a man does.
  Required: it is why this preference exists.
- `title` and `company` are as the sender's signature gives them.
- `aliases` are other names a campaign may pick this sender by, each given once.
- `details` is anything else a writer has to know to write as them.

Which mailbox or account a message goes out from is not part of this value: that is the provider's account, chosen
where the send is. A value is at most 64 KiB in its canonical form, which is its compact JSON with the keys sorted, in
which every character outside ASCII is escaped at six bytes, and so are DEL and each of `<`, `>`, `&`, `'`, `"`, `+` and
the backtick, while a character outside the Basic Multilingual Plane takes twelve; the `value_bytes` a preference
reports is the number the cap compares. Two or three UTF-8 bytes become six: about ten thousand characters outside ASCII
in all.

## Setting it

A preference is the person's word, so only a person sets it, and the request names them as its actor. An agent working
for the person in their own session sets it on their answer, under their name; a role Jason launches is refused when it sets or unsets one (`preference_not_human`).

```sh
jason preference set sender.identity --value-file sender.json --actor human:Marta
jason preference list --human
jason preference unset sender.identity --actor human:Marta
```

Unsetting removes the value. The installation's record of what changed keeps that it was set and unset, by whom and
when, with the value's size and a hash of it — never the value itself, which is personal data a person must be able to
take back.

## The fields

<!-- BEGIN GENERATED properties -->
<!-- Rendered from sender.identity.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| key | `sender.identity` |
| version | 1 |

| Field | Required | What it holds |
|---|---|---|
| `senders` | yes | a list of 1 to 20, each an object whose fields follow |
| `senders[].name` | yes | text of 1 to 200 characters |
| `senders[].addressed_as` | no | text of at most 100 characters |
| `senders[].gender_forms` | yes | one of `feminine`, `masculine`, `neutral` |
| `senders[].title` | no | text of at most 200 characters |
| `senders[].company` | no | text of at most 200 characters |
| `senders[].aliases` | no | a list of at most 10, each different; each text of 1 to 100 characters |
| `senders[].details` | no | text of at most 2000 characters |

<!-- END GENERATED properties -->

The fixtures under [`../fixtures/preferences/sender.identity/`](../fixtures/preferences/sender.identity/) are run by
the repository's tests against this document, so an example on this page and the schema cannot drift apart.
