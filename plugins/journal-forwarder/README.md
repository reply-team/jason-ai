# The journal-forwarder plugin

A reference subscriber. Jason writes everything it does to its journal; this plugin pushes each journal line of the
kinds you grant it to one URL of yours, as JSON, after Jason wrote it. It is the smallest complete package of
`kind: subscriber` — copy its shape to write your own (see [docs/plugins.md](../../docs/plugins.md), section 17).

## What it does

One thing: the subscription contract
[`subscription.handle`](../../docs/contracts/subscriptions/subscription.handle.md). For each line of a granted kind the
runtime hands it, the plugin POSTs

```json
{ "line": { "id": "jrn_…", "kind": "workitem_failed", "…": "the line as the journal lists it" },
  "envelope": {},
  "attempt": 1 }
```

to the one URL you name, and answers the envelope it was handed unchanged, so a forwarder placed anywhere in a chain passes
the envelope through. It declares every kind the catalog lets a plugin subscribe to; what it is actually handed is what
you grant (below).

The forwarder takes no `host.call`: it reads nothing of Jason and changes nothing in it, it only pushes what it is
handed. Its manifest asks for `http` and two environment variables, and for no Runtime API family.

## What the receiver gets

A `POST` with `Content-Type: application/json` and the line's public id in `Jason-Line-Id`, and with
`Authorization: Bearer …` when you set a token. The body is the object above: `line` is the journal line exactly as the
runtime handed it, `envelope` is what the handlers before this one returned (the empty object for the first), and
`attempt` counts how many times this plugin has been handed this line.

**Delivery is at least once.** A line is handed out again after the runtime restarts in the middle of a chain, and a
POST that timed out may have arrived. The receiver drops a line id it has already seen, and `Jason-Line-Id` is the key to
do it by. Any 2xx answer is delivery; its body is not read.

**What leaves the machine.** A line carries what the journal says: campaign and contact names, fields and contexts as they
changed, addresses put on the do-not-contact register, the reasons people typed. A receiver is therefore a place you
trust with that. The grant is the only filter, and `["*"]` sends every kind the package declares, which is every kind a
plugin may subscribe to.

## What you need

- **A receiver**: anything that takes a `POST` of JSON at a URL. A file is reached by a receiver of your own that writes
  one; the package has no way to write a file itself.
- **Jason 0.31.0 or newer** for this version of the package, whose manifest says a word an earlier runtime refuses.
  A release publishes the package its own runtime reads; 0.30.0 published 1.0.1. Subscriptions arrived in 0.29.0, which
  published none.

## Installing it

**1. Install the package.** Every release of Jason from 0.30.0 on publishes it as
`jason-plugin-journal-forwarder.zip`, listed in that release's `checksums.txt`, and the Jason you run installs the one
its own release published:

```sh
jason plugin install journal-forwarder
```

It checks the package against its line in `checksums.txt` and, unpacked, against the content digest the release's
manifest gives, and places it as `~/.jason/plugins/journal-forwarder` only once both hold. A different package already
there is kept unless `--replace` says otherwise. When the runtime answers, the verb asks it to reload; the reload of
step 4 is then the one that picks up the grant.

**2. Grant it**, in `~/.jason/config/settings.json`:

```json
{
  "Plugins": {
    "Grants": {
      "journal-forwarder": {
        "Http": ["localhost:8787"],
        "Env": ["JOURNAL_FORWARDER_URL", "JOURNAL_FORWARDER_TOKEN"],
        "Subscriptions": ["workitem_failed", "approval_requested"]
      }
    }
  }
}
```

`Subscriptions` is the list of journal kinds you admit it to; `["*"]` admits every kind it declares. A plugin is handed
nothing it was not granted, and declaring a kind is not permission to receive it. If other subscribers share a kind, the
order they run in is `Subscriptions:Order:<kind>` (section 16 of the plugin guide).

**3. Name the URL**, as an environment variable of the runtime — not of your shell. A runtime started at logon — by a
logon task, a LaunchAgent or a `systemd --user` unit — has the account's logon environment, not your shell's profile, so
set it as a user environment variable there:

| Variable | What it is |
|---|---|
| `JOURNAL_FORWARDER_URL` | the one URL every line is POSTed to: `http://localhost:8787/jason`, or an `https://` URL of your receiver |
| `JOURNAL_FORWARDER_TOKEN` | optional: a bearer token for a receiver that requires one. It is read with `host.env`, sent only as the `Authorization` header, and never logged or returned |

The settings file can say both instead, and what it says comes first: see
[The configuration](#the-configuration). Neither belongs in `plugin.yaml`.

Keep a secret in the URL's query string or in the token, never in its path: the plugin host's diagnostics name the host
and the path of every request, and never the query or the headers.

**4. Reload:**

```sh
jason plugin reload
jason plugin list --human
```

`journal-forwarder` is listed `valid`, of kind `subscriber`, and the lines of the granted kinds written from then on are
pushed. Lines written before the reload are not: a kind starts at the end of the journal.

## A receiver elsewhere than the loopback

The manifest names `localhost:8787` and says `any_granted_host: true`: the host is yours to grant, and no file of the
package changes. Two entries of `~/.jason/config/settings.json` say it, beside what step 2 wrote there.

### The grant

`Plugins:Grants:journal-forwarder:Http` names the receiver's host, with `:port` when it is not 443:

```json
{
  "Plugins": {
    "Grants": {
      "journal-forwarder": {
        "Http": ["receiver.example.com"],
        "Subscriptions": ["workitem_failed", "approval_requested"]
      }
    }
  }
}
```

`["*"]` does not say it: a star means the hosts the manifest declares, so `localhost:8787` alone. A package that says
`any_granted_host` asks for more than one that does not, and `jason plugin list --human` shows the word beside the
capabilities. The plugin does not check the URL's host itself: the plugin host does, against the grant. A URL whose host
the grant does not name ends the invocation as `http_host_not_allowed`, and nothing is sent.

### The configuration

`Plugins:Config:journal-forwarder` holds the URL and, where the receiver wants one, the token:

```json
{
  "Plugins": {
    "Config": {
      "journal-forwarder": { "url": "https://receiver.example.com/jason", "token": "a-token-of-yours" }
    }
  }
}
```

The plugin reads `url` and `token` first, and `JOURNAL_FORWARDER_URL` and `JOURNAL_FORWARDER_TOKEN` where these say
nothing, so with both keys set the `Env` grant is not needed. A value here is handed to the plugin on stdin at each
invocation; it is no part of the package, of the journal, of the logs, of `jason plugin list` or of a `jason doctor`
bundle. A receiver other than on the loopback interface is reached over `https`; an `http` one fails with
`http_scheme_not_allowed`.

Run `jason plugin reload` after changing either entry: a load freezes both, as it freezes every grant.

The package stays the one the release published, with the digest `jason plugin list` shows, so `jason update` moves it to
the next version and both entries stand, since neither is in the package.

## What it reports

A failure of a line is one `subscription_failed` journal line (`jason journal list --kind subscription_failed`), which
names the kind, the line, the plugin, the class, the code and the attempts. It never carries the URL, the token or
anything the receiver answered. A failure that is permanent ends the chain at this plugin for that line, and the kind
moves on to its next line; a transient one is tried again after a wait that doubles.

| Outcome | When |
|---|---|
| delivered | the receiver answered 2xx; the chain goes on with the envelope unchanged |
| `forwarder_unreachable` (transient) | the receiver did not answer within the twenty seconds the plugin allows, or answered 408, 429 or 5xx. The runtime tries again after a wait that doubles |
| `forwarder_unauthorized` (permanent) | the receiver answered 401 or 403: the token is wrong, or one is needed and none was set |
| `forwarder_refused` (permanent) | any other answer the receiver will not change its mind about, such as 400 or 404, or a redirect, which is never followed |
| `forwarder_url_missing` (permanent) | the key `url` under `Plugins:Config:journal-forwarder` says nothing, and `JOURNAL_FORWARDER_URL` is not set in the runtime's environment or the grant does not list it under `Env` |
| `forwarder_url_invalid` (permanent) | the URL is not an `http://` or `https://` URL |
| `http_host_not_allowed` (permanent) | the URL names a host the grant does not name |
| `http_scheme_not_allowed` (permanent) | the URL is `http://` to a host that is not `localhost`, `127.0.0.1` or `::1` |
| `unknown_operation` (validation) | the plugin was invoked under anything but the line's own kind, which the runtime does not do |

A timeout is reported as `forwarder_unreachable`, not as an ambiguous failure: delivery is at least once already, so a
repeat is expected and the receiver drops it.

## What it cannot do

- **It cannot tell what the receiver did with a line.** Delivered means the receiver answered 2xx.
- **It filters by kind and nothing else**: the grant is the only filter; a receiver that wants a subset of a kind drops
  the rest.
- **It sends to one URL.** A second receiver would be a second package under another id.
- **It cannot change anything in Jason**, and what it pushes is not handed to another handler as a new line.
