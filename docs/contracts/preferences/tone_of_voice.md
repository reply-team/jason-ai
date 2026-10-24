# `tone_of_voice` — how the company sounds

**Version 1.** The machine-readable document is [`tone_of_voice.json`](tone_of_voice.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **A named preference, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/) and never among them: no work item names it, no route carries it, and no plugin is
> invoked for it. It is a fact about the client that a person states, kept once for the whole installation.

## What it is for

How the company sounds in writing, as agreed with the person: described in their own words, or read by their agent
from a message they like and then confirmed by them. It is kept once, so that a skill can check that it is set before it writes anything for a person to send, and a screen can show it.

## The value

```json
{
  "summary": "Plain and warm; short sentences; one concrete detail about the reader's company; no exclamation marks.",
  "formality": "informal",
  "greeting": "Hi and the first name, never Dear",
  "sign_off": "Best, and the sender's first name",
  "examples": [
    "Hi Alex, saw that Example Retail opened a second warehouse in May. We help teams that size cut picking errors by a third. Worth a short call next week? Best, Marta"
  ]
}
```

- `summary` is the tone in words. Required.
- `formality` is `informal`, `neutral` or `formal`.
- `greeting` and `sign_off` say how a first message opens and how a message closes. They are guidance for whoever
  writes, in plain words, not a template with anything to fill in.
- `examples` are up to three messages the person likes, with the recipient's details taken out before they are kept.

A standing rule about what every message says — "always include one proof point" — is an instruction for the work
rather than how the company sounds, and has no field here. A value is at most 64 KiB of UTF-8 in its canonical form,
which is its compact JSON with the keys sorted, counted with nothing escaped: a character costs what UTF-8 spells it
in, one byte in ASCII, two in Cyrillic, three in most other scripts and four outside the Basic Multilingual Plane; the
`value_bytes` a preference reports is the number the cap compares, once the value has been changed by this version. Every field at its longest comes to about 14.5 KB in
English and at most about 29 KB in Cyrillic.

## Setting it

A preference is the person's word, so only a person sets it, and the request names them as its actor. An agent working
for the person in their own session sets it on their answer, under their name; a role Jason launches is refused when it sets or unsets one (`preference_not_human`).

```sh
jason preference set tone_of_voice --value '{"summary":"Plain and warm; no exclamation marks.","formality":"informal"}' --actor human:Marta
jason preference unset tone_of_voice --actor human:Marta
```

Unsetting removes the value. The installation's record of what changed keeps that it was set and unset, by whom and
when, with the value's size and a hash of it — never the value itself.

## The fields

<!-- BEGIN GENERATED properties -->
<!-- Rendered from tone_of_voice.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| key | `tone_of_voice` |
| version | 1 |

| Field | Required | What it holds |
|---|---|---|
| `summary` | yes | text of 1 to 2000 characters |
| `formality` | no | one of `informal`, `neutral`, `formal` |
| `greeting` | no | text of at most 200 characters |
| `sign_off` | no | text of at most 200 characters |
| `examples` | no | a list of at most 3; each text of 1 to 4000 characters |

<!-- END GENERATED properties -->

The whole value is also bounded by the store's cap of 64 KiB, measured in UTF-8 as above, and a value past it is
refused as `too_large` with its size named; at the maxima above no value reaches it, since even four bytes for every
character come to about 58 KB.

The fixtures under [`../fixtures/preferences/tone_of_voice/`](../fixtures/preferences/tone_of_voice/) are run by the
repository's tests against this document, so an example on this page and the schema cannot drift apart.
