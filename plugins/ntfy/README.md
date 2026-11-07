# The ntfy plugin

Jason's notifications, delivered to an [ntfy](https://ntfy.sh) topic: when Jason starts waiting for you — an approval
parked at a claim, or a question a role raised — a pointer arrives on whatever device follows the topic.

## What it does

One thing: the delivery contract [`notification.send`](../../docs/contracts/notifications/notification.send.md). It
publishes the pointer the runtime composes —

> 3 approvals and 1 question are waiting in Jason. Ask your agent what Jason is waiting for, or run jason approval
> list and jason decision list.

— with the title `Jason`, to one topic, as JSON to the server's root. **Nothing else about the work travels**: the
runtime makes the pointer from counts and, when protective actions failed, at most three campaign ids, and
the delivery contract, [`notification.send`](../../docs/contracts/notifications/notification.send.md), says
exactly what a pointer may carry.

The topic travels in the body rather than in the path, so the line the plugin host writes about the call in
`~/.jason/work/plugins/pin_…/stderr.log` names the server's root and not your topic.

## What you need

- **A topic.** On the public server, **a topic is readable by anyone who knows its name**: ntfy topics have no owner.
  Choose a name nobody would guess — `jason-` followed by a long random string, not your name — or run your own server
  with an access token.
- **The ntfy app, or anything else that follows the topic**, on the device you want the pointer on.
- **Jason 0.31.0 or newer for this version**; an older runtime takes the package from its own release's tree, and every
  release carries the copy that works with it. **Jason 0.32.0 or newer for the install verb.**

## Installing it

**1. Place the package.** Every release of Jason from 0.32.0 on publishes it as `jason-plugin-ntfy.zip`. If you edited
`plugin.yaml` in a copy of an earlier version to reach your own server, that copy reads as behind and `jason status`
advises `--replace`. `--replace` drops the edit, and the edit is no longer needed: grant the host and keep the binding,
as [Your own server](#your-own-server) says. A copy whose grant was `"*"` loses its server after `--replace`, since the
star means `ntfy.sh`, so grant the host by name first.

```sh
jason plugin install ntfy
```

It checks the package against its line in `checksums.txt` and, unpacked, against the content digest the release's
manifest gives, and places it as `~/.jason/plugins/ntfy` only once both hold. A copy of the directory from the release
you run works too, on any release since 0.2.0; the directory name is the plugin id, so it has to be `ntfy`.

**2. Name it and grant it**, in `~/.jason/config/settings.json`:

```json
{
  "Notifications": {
    "Plugins": {
      "ntfy": { "Binding": { "topic": "jason-7c1f9a2e5b40d613" } }
    }
  },
  "Plugins": {
    "Grants": {
      "ntfy": { "Http": ["ntfy.sh"] }
    }
  }
}
```

Naming it under `Notifications:Plugins` is what makes it a channel; a plugin not named there is never invoked. The
binding takes `topic` and, for another server, `server` (`https://your.host`). The grant gives the package the host
its manifest asks for; declaring is not permission.

**3. Reload:**

```sh
jason plugin reload
jason plugin list --human
```

`ntfy` is listed `valid`, of kind `notification`. The next burst of waiting things is sent to your topic as well as
shown as a toast; `jason runtime status --human` says what each channel did last.

## A server that needs a token

An ntfy server with access control takes a bearer token. The package reads one variable, `NTFY_TOKEN`, with
`host.env`, and sends it as `Authorization: Bearer …`. It is never part of the binding, and the plugin never logs it or
returns it.

1. Grant it: `"ntfy": { "Http": ["your.host"], "Env": ["NTFY_TOKEN"] }`.
2. Put `NTFY_TOKEN` in the environment of the runtime — not of your shell. A runtime started at logon — by a
   logon task, a LaunchAgent or a `systemd --user` unit — has the account's logon environment, not your shell's
   profile, so set it as a user environment variable there.

## Your own server

The manifest names `ntfy.sh` and says `any_granted_host: true`: the host is yours to grant, and no file of the package
changes, so `jason update` moves the package on as it does the public server's.

1. Grant your server's host, with `:port` when it is not 443: `"ntfy": { "Http": ["your.host"] }` under
   `Plugins:Grants`. The star in a grant means `ntfy.sh` alone, so a server of your own is named.
2. Name the server in the binding: `"server": "https://your.host"`. The binding's schema accepts `http://` too, but the
   plugin host does not: a server other than on the loopback interface is reached over `https`, and an `http` one fails
   with `http_scheme_not_allowed`.
3. Reload.

## What it reports

Every outcome is a line in the journal (`jason journal list --kind notification_sent`):

| Outcome | When |
|---|---|
| `delivered` | the server answered 2xx; the receipt is the id it gave the message |
| `failed` `notification_unreachable` (transient) | the server did not answer, or answered 429 or 5xx. The runtime tries once more, thirty seconds later |
| `failed` `notification_unauthorized` (permanent) | the server answered 401 or 403: the token is wrong, or one is needed and none was granted |
| `failed` `notification_refused` (permanent) | any other answer the server will not change its mind about, such as 400 for a topic it rejects |
| `failed` `http_host_not_allowed` (permanent) | the binding names a server the grant does not name; the star means `ntfy.sh` alone |
| `failed` `capability_not_granted` (permanent) | no host is granted to the package at all |
| `failed` `http_scheme_not_allowed` (permanent) | the binding names an `http://` server that is not `localhost`, `127.0.0.1` or `::1` |
| `not_attempted` `binding_invalid` | the binding fails the package's schema: no `topic`, a topic with other than letters, digits, `-` and `_`, or a `server` with a path |
| `not_attempted` `unavailable` | the package is not loaded |

A failure never fails the approval or the question the pointer was about: nothing waits on a notification.

A server that did not answer is named by its address in the line's `detail`: the binding's `server`, in the operating
system's wording. That is no secret, but the line reaches every subscriber of the kind `notification_sent`.

## What it cannot do

- **It cannot tell whether you saw it.** `delivered` means the server accepted the message; whether a phone showed it is
  between the server and the phone.
- **It sends the pointer and nothing else**: no priority, no tags, no click action, no attachment, no other field of
  its own making — the delivery contract allows none.
- **One topic per installation.** The binding names one; a second topic would be a second package under another id.
