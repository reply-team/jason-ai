# Changelog

This package versions independently of the Jason runtime, under SemVer. A change at Reply's end is
absorbed by a release of this package alone.

## 0.2.0 (in progress)

Adds the four reads: `enrollment.list`, `engagement.summarize`, `conversation.list` and
`conversation.get`. Built against Reply API v3 through the same CLI, under the same rules as the
first three operations.

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
