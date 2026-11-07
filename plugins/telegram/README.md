# The Telegram plugin

Jason's notifications, delivered to a [Telegram](https://telegram.org) chat by a bot you make: when Jason starts waiting
for you — an approval parked at a claim, or a question a role raised — a pointer arrives on whatever device follows the
chat.

## What it does

One thing: the delivery contract [`notification.send`](../../docs/contracts/notifications/notification.send.md). It sends
the pointer the runtime composes —

> 3 approvals and 1 question are waiting in Jason. Ask your agent what Jason is waiting for, or run jason approval
> list and jason decision list.

— as plain text to one chat, with the Bot API's `sendMessage`. No formatting mode is asked for, so an `_` in a campaign id
stays an underscore. **Nothing else about the work travels**: the runtime makes the pointer from counts and, when
protective actions failed, at most three campaign ids, and the delivery contract says exactly what a pointer may carry.
The title and the counts the contract hands a plugin are not sent.

What leaves the machine is that text, the chat it goes to and the bot's token, to `api.telegram.org` and nowhere else.

## What you need

- **A bot and its token.** In Telegram, talk to [@BotFather](https://t.me/BotFather): `/newbot`, a name, a username. It
  answers with a token that looks like `123456789:` followed by letters, digits and hyphens. Whoever has the token can
  act as the bot, so keep it as you would a password.
- **A chat the bot can write to.** A bot cannot start a conversation: send it a message first (in a private chat, open
  the bot and press Start; in a group or channel, add the bot, and make it an administrator of a channel).
- **The chat's id** (below).
- **Jason 0.32.0 or newer**, which publishes this package for the install verb.

## Finding the chat id

1. Send your bot any message, in the chat you want the pointer in.
2. Open `https://api.telegram.org/bot<your token>/getUpdates` in a browser, or fetch it with `curl`. It lists only the
   updates of the last 24 hours, so send the message just before, and it fails while a webhook is set on the bot.
3. Find `"chat":{"id":...}` in the answer. That number is the id. A group's id is negative; a supergroup's or a
   channel's begins `-100`.

A third-party bot that tells you ids on request is a choice, and it sees the message you send it. The way above asks no
one but Telegram.

## The id form

`chat` in the binding is one of:

- **A number**, as the answer above gives it: `-1001234567890`. The Bot API documents an integer, and the plugin sends one.
- **`@name`**, the public name of a channel or a supergroup: `@jason_alerts`, five to thirty-two characters, a letter first
  and then letters, digits and underscores. It cannot name a private chat or a group without a public name.

Anything else fails the binding's schema, and nothing is sent.

## Installing it

**1. Install the package.** Every release of Jason from 0.32.0 on publishes it as `jason-plugin-telegram.zip`, listed in
that release's `checksums.txt`, and the Jason you run installs the one its own release published:

```sh
jason plugin install telegram
```

**2. Name it, grant it and give it the token**, in `~/.jason/config/settings.json`:

```json
{
  "Notifications": {
    "Plugins": {
      "telegram": { "Binding": { "chat": "-1001234567890" } }
    }
  },
  "Plugins": {
    "Grants": {
      "telegram": { "Http": ["api.telegram.org"] }
    },
    "Config": {
      "telegram": { "token": "123456789:the-token-botfather-gave-you" }
    }
  }
}
```

Three entries do three things. `Notifications:Plugins:telegram` is what makes it a channel; a plugin not named there is
never invoked. `Plugins:Grants:telegram` gives the package the one host its manifest asks for; declaring is not permission.
`Plugins:Config:telegram` holds the token, which the plugin reads with `host.config`.

**The token can instead come from the environment.** Grant the variable as well — `"Env": ["TELEGRAM_BOT_TOKEN"]` — and put
`TELEGRAM_BOT_TOKEN` in the environment of the runtime, not of your shell. A runtime started at logon, by a logon task, a
LaunchAgent or a `systemd --user` unit, has the account's logon environment, not your shell's profile, so set it as a user
environment variable there. When both are present the setting wins.

**3. Reload:**

```sh
jason plugin reload
jason plugin list --human
```

`telegram` is listed `valid`, of kind `notification`. The next burst of waiting things is sent to your chat as well as shown
as a toast; `jason runtime status --human` says what each channel did last.

## The token travels in the path

The Bot API takes the token in the path of every request, `https://api.telegram.org/bot<token>/sendMessage`; that is
Telegram's design. Jason's plugin host masks every value of `Plugins:Config` and every granted variable wherever it would
write one, so the line it writes about the call, a log line and a failure all show `[redacted]` in the token's place. The
token is never in the binding, the journal or a result.

## What it reports

Every outcome is a line in the journal (`jason journal list --kind notification_sent`). The HTTP status decides, never the
`error_code` in the body, which Telegram's page says may change.

| Outcome | When |
|---|---|
| `delivered` | Telegram answered `ok`; the receipt is the id of the message |
| `failed` `notification_unreachable` (transient) | no answer, a 429 or a 5xx, or a 2xx that is not a Bot API answer. The runtime tries once more, thirty seconds later |
| `failed` `notification_unauthorized` (permanent) | 401 or 404, or no token at all |
| `failed` `notification_refused` (permanent) | any other 4xx (400 and 403 included), a redirect, or a 2xx that says `ok: false` |
| `failed` `http_host_not_allowed` (permanent) | the host is not both in the manifest and in the grant |
| `not_attempted` `binding_invalid` | the binding has no `chat`, a `chat` in neither form above, or another field |
| `not_attempted` `unavailable` | the package is not loaded |

A wrong or revoked token is answered 401 by some paths and 404 by others: both are the Bot API's own behaviour, not
something its documentation states, and both are read as a token that does not work. A 403 means the bot may not write
there (it was blocked, removed or never started), and a 400 `chat not found` means the id is wrong or the bot never saw
the chat. A 400 that carries `migrate_to_chat_id` means a group became a supergroup and has a new id. The plugin passes
on only Telegram's `description`, which does not carry it, so look the id up again as in "Finding the chat id".
Telegram's own words for a refusal are in the failure's message.

A failure never fails the approval or the question the pointer was about: nothing waits on a notification.

## What it cannot do

- **It cannot tell whether you saw it.** `delivered` means Telegram accepted the message.
- **It sends the pointer and nothing else**: no formatting, no buttons, no attachment — the delivery contract allows none.
- **One chat per installation.** The binding names one; a second chat would be a second package under another id.
