# `agent.interactive_host` — the agent host a person's own session is started with

**Version 1.** The machine-readable document is [`agent.interactive_host.json`](agent.interactive_host.json); this page
explains it. Where the two seem to differ, the document is right and this page is a bug.

> **A named preference, not a canonical operation.** It is published beside the operations under
> [`../operations/`](../operations/) and never among them: no work item names it, no route carries it, and no plugin is
> invoked for it. It is a fact about the client that a person states, kept once for the whole installation.

## What it is for

`jason session open` starts the person's own agent host in a new terminal window: on one approval or question, at the
campaign's folder, with the item's brief as its first input, which the UI's Open an AI session button does on Windows
too; or at a folder, a campaign's, a project's or the harness root, with no first input, when no `--host` names
another. This key says which host that is. It is the host the person types and works in, not the host a role runs on:
a role's host is its execution profile's, and this key changes nothing a role does.

## The value

```json
{ "host": "codex" }
```

- `host` is one of the host words: `claude_code`, `codex`, `copilot_cli`, `cursor_cli`, `gemini_cli`,
  `antigravity_cli`. Required.

**Unset means `claude_code`.** The catalog keeps no defaults, so an unset key answers null, and `jason session open`
reads that as Claude Code.

The list of words is the hosts this release knows. A host added in a later release adds its word here and raises the
key's version: a wider list never makes a value already stored invalid. A word a newer runtime accepted and this CLI
does not know is refused by `jason session open` with exit 1, naming the word and the ones it knows, and nothing is
started.

The host's program is looked up on the search path the CLI runs with, by its command word (`claude`, `codex`, `copilot`,
`agent`, `gemini`, `agy`), as `jason status` finds a program: an `.exe`, npm's shim read for what it starts (the
interpreter and entry script it names, or the executable its package ships), or, for Cursor CLI, its launcher script read for the program it starts, as the runtime reads it; never a script
through a shell. A host that is not there is refused with exit 1, saying which program was not found and how to choose
another host, and a host found only as a file Jason refuses is refused naming that file and the rule it broke; Jason
never falls back to another host, since the person named one.

## Setting it

A preference is the person's word, so only a person sets it, and the request names them as its actor; a role Jason
launches is refused when it sets or unsets one (`preference_not_human`).

```sh
jason preference set agent.interactive_host --value-file host.json --actor human:Marta
jason preference unset agent.interactive_host --actor human:Marta
```

The file `host.json` holds the value shown above, so the inner quotes reach the runtime on every shell. Unsetting
removes the value, and the host is Claude Code again. The installation's record of what changed keeps that it
was set and unset, by whom and when, with the value's size and a hash of it — never the value itself.

## The fields

<!-- BEGIN GENERATED properties -->
<!-- Rendered from agent.interactive_host.json. Edit the document; this block follows it. -->

| What it is | Value |
|---|---|
| key | `agent.interactive_host` |
| version | 1 |

| Field | Required | What it holds |
|---|---|---|
| `host` | yes | one of `claude_code`, `codex`, `copilot_cli`, `cursor_cli`, `gemini_cli`, `antigravity_cli` |

<!-- END GENERATED properties -->

The fixtures under [`../fixtures/preferences/agent.interactive_host/`](../fixtures/preferences/agent.interactive_host/)
are run by the repository's tests against this document, so an example on this page and the schema cannot drift apart.
