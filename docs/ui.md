# The browser UI

Jason serves a small web UI from the runtime itself: what needs you (approvals and questions) and whether everything is
running. It is one more client of the Runtime API, with the same rights as the command line, and nothing in the runtime
exists for it alone: everything the page does is a verb you could type.

## Opening it

```
jason ui open
```

The runtime answers with a link into the page and the command opens it in your default browser (`--no-open` prints it
instead, for an agent to hand to you). The link carries a ticket that pairs one browser, once, within 60 seconds;
after that the page is paired and a bookmark of `http://127.0.0.1:52766/ui/` keeps working, across runtime restarts.
`jason ui open --approval <id>` and `--question <id>` open on one item.

If nothing is installed, `jason ui open` says so with the code `ui_not_installed` and names the two directories below.

## Where the page comes from

The runtime serves `<data>/ui/custom/` when it holds a build (an `index.html` and a `manifest.json`), else
`<data>/ui/default/` when that holds a build; no release installs one. It serves those files and nothing else, under
`Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; base-uri 'none'`.

To try a build that is not the installed one — a CI run's `jason-ui.zip`, or your own — unzip it into `~/.jason/ui/custom/`
and run `jason ui open`; `jason status` and `jason runtime status --human` say which build is served, with its version
and digest. Delete `custom/` to go back to the default.

## Paired browsers

```
jason ui tokens
jason ui revoke <token-id>
jason ui revoke --all
```

Each pairing is a token the runtime keeps as a hash, with who paired and when, and when it was last used. A token never
expires on its own; revoke it when a browser should no longer have it. A browser that `jason ui open` opens again ends
the token it held, so the list stays one row per browser. The System screen shows the same list.

## The port

The runtime listens on `127.0.0.1:52766` by default, so the bookmark is stable. When that port is taken it takes the
next free one above it and `jason runtime status --human` says so; `Runtime:Port` in `settings.json` picks another, and
`0` lets the OS choose, as before.

## What the page may do

The page holds its token in the browser's storage for the runtime's origin and sends it on every call, as the command
line sends the run token. A request from any other origin is refused by the runtime, and the browser never sends the
token to another site. The token has the same rights as the run token: there is one level of trust, the machine's user.
