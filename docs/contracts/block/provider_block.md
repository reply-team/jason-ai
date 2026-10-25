# `provider_block` — the provider refused, and did nothing

**Version 1.** The machine-readable document is [`provider_block.json`](provider_block.json); this page explains it.
Where the two seem to differ, the document is right and this page is a bug.

> **Not an operation.** It is published beside the operations under [`../operations/`](../operations/) and never among
> them: no work item names it and no plugin is invoked for it. It is what every operation's failure of class `blocked`
> carries beside its code.

## What it is for

Some failures have nothing to do with the work item. The account has run out of credits, a mailbox has reached the
allowance the provider counts for it, the key was revoked, the social account was disconnected. Repeating the call at
once meets the same refusal, and so does the next work item through the same account; what clears it is a person
topping up, reconnecting or waiting for an allowance to renew. A plugin says so with the fifth failure class,
`blocked`, the code `provider_blocked`, and the block below, so that what is blocked, why and where to go travel in
words the runtime reads rather than in a provider's own message.

## The rule a plugin follows

A plugin MUST report `blocked` only where the provider did nothing for this call: `blocked` asserts that the
provider did nothing for this call, so a repeat after the block clears is a first act, never a second one. A plugin
that cannot be sure the provider did nothing — the request left and the answer was lost, or the provider acted on
part of it — says `ambiguous`, never `blocked`, and the operation's own repetition rule decides what happens next.

Where the provider refused for a block and did nothing, a plugin says `provider_blocked` with the block, even where
the operation also declares an older code for the condition (`sender_limit_reached`, `insufficient_credits`,
`sender_unavailable`, `unauthorized`); those codes stand for an answer the plugin cannot read as a block.

Every canonical operation declares `provider_blocked` under the class `blocked`, the protective ones included; it is
the one code of the class. The delivery contract and the events contract do not declare it: a block a delivery
channel or an events read reports is an answer outside its contract.

## The block

```json
{
  "scope": "sender",
  "sender_external_id": "mbx_1042",
  "reason": "limit",
  "link": "https://app.example.com/mailboxes/1042",
  "clears_at": "2026-10-25T00:00:00Z"
}
```

- `scope` is what is blocked: `account`, every call through the binding the attempt ran under; `sender`, one sending
  account under it, such as a mailbox at its daily limit. Required.
- `sender_external_id` is the provider's identifier of that sending account: 1 to 256 characters, no control
  characters. Present exactly when `scope` is `sender`, and absent when it is `account`.
- `reason` is why, in the contract's words, never the provider's: `billing` (credits, a plan, a subscription or a
  payment), `limit` (an allowance the provider counts), `credential` (a key or a sign-in the provider no longer
  accepts), `channel` (a mailbox or a social account disconnected or paused), or `other` where none of these fits.
  Required.
- `link` is where a person goes to clear it, as the provider gave it: an `https` address, at most 2048 characters.
- `clears_at` is when the provider says the block clears by itself, such as an allowance that renews, as an ISO-8601
  timestamp with an offset. Absent where the provider did not say. It is a hint, never the only way the block is known
  to have cleared.

Nothing else is accepted. The binding is the one the attempt ran under, so the block never names an account or a
credential; the words a person reads are the failure's own `message`, so the block carries no detail of its own. The
whole block is at most 8 KiB, counted as the UTF-8 bytes of its JSON text.

## How it travels

The block is `error.block` on a failed outcome ([plugins.md §10](../../plugins.md#10-the-protocol)), and
`host.fail` takes it as `block` ([plugins.md §6](../../plugins.md#6-the-host-sdk)):

```json
{
  "class": "blocked",
  "code": "provider_blocked",
  "message": "The account has no credits left.",
  "block": { "scope": "account", "reason": "billing", "link": "https://app.example.com/billing" }
}
```

`error.block` is required exactly when the class is `blocked`, and refused under every other class: an outcome that
breaks either half is not an outcome, and ends as `plugin_malformed_outcome`.

## What the runtime does with it today

A blocked failure spends no retry: the attempt ends `blocked`, never counted, and the block is kept beside the error
the work item shows (`last_error.block`). The runtime keeps the block itself too, per plugin and binding identity —
the account's, or one sender's under it — and holds the work it concerns: `reach: act` work and the protective
operations wait on the block, as `waiting` with a `wait.blocked_by` naming it, until a call gets past it — one of
them let through as the block's trial at the moment the provider said it clears by itself where that is ahead, a day
at most; otherwise at the cadence. Every other operation's item ends final, and reads run through an open block. The
`link` stays on the block, which `block.get` and `block.list` answer, and on the error, where a person reads it: never
in a log, a journal line, a notification or a `wait` block. A person who has removed the cause clears the block with
`block.clear`, under their name, and the next scan lets what it held go; a provider that refuses again opens a new block.
A block that does not satisfy the schema is recorded `permanent`, with what is
wrong with it named beside the error, so a malformed block never stands for a real one. A read made on request outside
any work item, such as `contact.threads`, answers a blocked failure as an API error carrying the plugin's code with
`retryable` false; the block itself does not travel in an API error.

## Fixtures

[`../fixtures/block/`](../fixtures/block/) holds the published vectors: each `value-*.json` names the block, a case
and either `"expect": "valid"` or the pointers and reasons the check reports. The tests run every one of them through
the check the runtime runs.
