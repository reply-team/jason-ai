# Changelog

This package versions independently of the Jason runtime, under SemVer. A change at Reply's end is
absorbed by a release of this package alone.

## 0.2.2

- A call to Reply is given what is left of the invocation's budget less five seconds (never more than half of
  what is left), where it used to be given all of it. A call given everything runs out at the moment the
  host's own engine ends the invocation, and on a busy machine the host's work after a cut-off call — ending
  the CLI's process tree, waiting for its pipes — could outlast what the engine had left. The host then
  answered `plugin_timeout` in place of this package's `provider_unavailable` or `provider_answer_lost`, and
  the identifiers the attempt had learned did not travel with it. A call that runs out of time is now cut off
  with time left for the package to answer it.
- Nothing else changes: the same calls, codes, classes and messages.

## 0.2.1

- The messages of `campaign_not_pausable`, `campaign_not_activatable` and `population_changed`, and of
  four `provider_call_failed` rows — a refusal in a word this package does not know, an enrollment Reply
  rejects as invalid, an enrollment that could not be built, and a read filtering by an identifier that
  names nothing — no longer send their reader to the failure's details, which never reach a work item's
  last error. Each says what happened and what could have caused it, and that the attempt's trace says
  which, with Reply's own code or both numbers. The README's tables say the same, and two tests hold
  every message in the error table and every remedy in those tables to it.

## 0.2.0

Adds the four reads — `enrollment.list`, `engagement.summarize`, `conversation.list` and
`conversation.get` — and four writes: `campaign.pause`, `campaign.activate`, `optout.record` and
`enrollment.stop`. Built against Reply API v3 through the same CLI, under the same rules as the
first three operations: eleven in all, every operation this build publishes.

- `campaign.pause` pauses a sequence, and answers `already_paused` for one that is; it runs while its
  Jason campaign is paused. `campaign.activate` starts or resumes one only when Reply's count is the
  population a person approved, and fails `population_changed` with both numbers when it is not.
- `optout.record` sets Reply's opt-out flag, and `enrollment.stop` takes a person out of a sequence
  without marking them finished. Both find a person without a pin by Reply's free-text search, read to
  its end and compared with the address exactly; neither creates a second contact for an address Reply
  holds, and a stop creates nobody. What they said, and why they were taken out, never reach Reply.
- A contact Reply answers under an identifier this package cannot work by — from the search, the import
  or the create path — is reported in the failure's details by that identifier alone, never with the
  rest of the person's record.
- A read whose answer is cut off at `Plugins:Exec:OutputBytes` now fails `provider_answer_too_large`,
  permanently, instead of being retried as an answer that went missing. A write's cut answer is still
  a lost write.
- The manifest asks for 256 MiB of memory: a read of a thread reads two answers that can each be as
  large as the exec capture.
- Upgrading: `Plugins:Limits:MaxMemoryMb` must be at least 256 (it is 512 by default). A lower ceiling
  refuses the whole reload that loads this version, and every package stays on the snapshot before it.

## 0.1.0

First release. Implements `campaign.get`, `list_membership.add` and `campaign.enroll` against Reply
API v3 through the Reply CLI, which owns the account's credential. What it does not do yet, and why,
is in [README.md](README.md).
