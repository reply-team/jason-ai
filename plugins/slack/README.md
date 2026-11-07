# The Slack plugin

Jason's notifications, delivered to a [Slack](https://slack.com) channel: when Jason starts waiting for you — an approval
parked at a claim, or a question a role raised — a pointer is posted to the channel you chose.

## What it does

One thing: the delivery contract [`notification.send`](../../docs/contracts/notifications/notification.send.md). It
posts the pointer the runtime composes —

> 3 approvals and 1 question are waiting in Jason. Ask your agent what Jason is waiting for, or run jason approval
> list and jason decision list.

— as plain text to one channel, with Slack's `chat.postMessage`. **Nothing else about the work travels**: the runtime makes
the pointer from counts and, when protective actions failed, at most three campaign ids, and the delivery contract,
[`notification.send`](../../docs/contracts/notifications/notification.send.md), says exactly what a pointer may carry.

What leaves the machine is one request to `slack.com`: the channel, the pointer's text and the bot token in the
`Authorization` header. The text is sent with Slack's `mrkdwn` formatting turned off, so an underscore in a campaign id
stays an underscore. The token is never part of the binding, and the plugin never logs it or returns it.

## What you need

- **A Slack app with a bot token.** Create an app at [api.slack.com/apps](https://api.slack.com/apps), give its bot the
  `chat:write` scope, install it to your workspace and copy the *Bot User OAuth Token* (it starts with `xoxb-`).
- **The bot in the channel.** Invite it from the channel itself (`/invite @your-bot`); a bot that is not a member cannot
  post to a channel, except one that has `chat:write.public`.
- **Jason 0.32.0 or newer**, which installs the package with `jason plugin install slack`.

## Installing it

**1. Place the package:**

```sh
jason plugin install slack
```

**2. Name it, grant it and give it the token**, in `~/.jason/config/settings.json`:

```json
{
  "Notifications": {
    "Plugins": {
      "slack": { "Binding": { "channel": "C0123ABCDEF" } }
    }
  },
  "Plugins": {
    "Grants": {
      "slack": { "Http": ["slack.com"] }
    },
    "Config": {
      "slack": { "token": "xoxb-…" }
    }
  }
}
```

Naming the plugin under `Notifications:Plugins` is what makes it a channel; a plugin not named there is never invoked.
The grant gives the package the one host its manifest asks for; declaring is not permission.

**The binding names the channel.** Prefer the channel's **id** (`C0123ABCDEF`; in Slack, the channel's details dialog
shows it at the bottom): an id survives a rename. A name, with or without the leading `#`, is sent as written. The
binding takes `channel` and nothing else.

**The token** is read from `Plugins:Config:slack:token`, and where that says nothing from the environment variable
`SLACK_BOT_TOKEN`. To keep it out of the settings file, grant the variable instead —
`"slack": { "Http": ["slack.com"], "Env": ["SLACK_BOT_TOKEN"] }` — and put `SLACK_BOT_TOKEN` in the environment of the
runtime, not of your shell. A runtime started at logon — by a logon task, a LaunchAgent or a `systemd --user` unit — has
the account's logon environment, not your shell's profile, so set it as a user environment variable there.

**3. Reload:**

```sh
jason plugin reload
jason plugin list --human
```

`slack` is listed `valid`, of kind `notification`. The next burst of waiting things is posted to your channel as well as
shown as a toast; `jason runtime status --human` says what each channel did last.

## What it reports

Every outcome is a line in the journal (`jason journal list --kind notification_sent`). A failed delivery carries
Slack's own error word in the line's `detail`.

| Outcome | When |
|---|---|
| `delivered` | Slack answered `ok`; the receipt is the message's `ts` |
| `failed` `notification_unreachable` (transient) | Slack did not answer, answered 429 or 5xx, answered something that is not its documented JSON, or said `ratelimited`, `fatal_error`, `internal_error`, `service_unavailable`, `request_timeout`, `team_added_to_org` or `org_login_required`. The runtime tries once more, thirty seconds later |
| `failed` `notification_unauthorized` (permanent) | no token was found, Slack answered 401 or 403, or said `invalid_auth`, `not_authed`, `token_revoked`, `token_expired`, `account_inactive`, `missing_scope`, `not_allowed_token_type`, `no_permission` or `team_access_not_granted` |
| `failed` `notification_refused` (permanent) | any other answer, such as `channel_not_found`, `not_in_channel` or `is_archived`, and any word this table does not list |
| `failed` `http_host_not_allowed` (permanent) | the grant does not name `slack.com` |
| `not_attempted` `binding_invalid` | the binding fails the package's schema: no `channel`, a name with other than lower-case letters, digits, `.`, `_` and `-`, or a second field |
| `not_attempted` `unavailable` | the package is not loaded |

A failure never fails the approval or the question the pointer was about: nothing waits on a notification.

## What it cannot do

- **It cannot tell whether you saw it.** `delivered` means Slack accepted the message; whether anyone read it is between
  Slack and the channel's members.
- **It sends the pointer and nothing else**: no title, no blocks, no thread, no mention of its own making — the delivery
  contract allows none.
- **One channel per installation.** The binding names one; a second channel would be a second package under another id.
