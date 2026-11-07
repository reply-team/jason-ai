# Plugins

Plugins are JavaScript packages that execute Jason's canonical provider operations against a real
vendor tool — and deliver notifications — inside a short-lived plugin-host process that the
runtime starts from its own executable. Official and community plugins use the same mechanism.

**A provider plugin here is reached because a route sends work to it.** A `provider_op` work item is
routed at the claim to the plugin an operator named for that campaign and operation, and to the
account their binding names; without a route it fails at the claim with `no_route`, and nothing falls
back to whichever other plugin happens to implement the operation. An operator writes routes with
`jason route set`; [docs/plugins.md](../docs/plugins.md) §13 lists what is still missing. A
notification plugin is reached because the `Notifications` settings name it, never by a route (its
contract is [`notification.send`](../docs/contracts/notifications/notification.send.md)). A subscriber is reached because the person granted
it journal kinds, never by a route or by the `Notifications` settings; the runtime hands it each line of those kinds after
it wrote it (its contract is [`subscription.handle`](../docs/contracts/subscriptions/subscription.handle.md)).

This directory is the plugin marketplace of the repository. Each plugin lives in its own folder:

```text
<plugin-id>/
├── plugin.yaml   # identity, version, supported contract versions, operations, requested capabilities
├── main.js       # invoke(operation, input, context)
└── modules/      # optional local JavaScript modules bundled with the plugin
```

The index so far:

| Plugin | What it implements |
|---|---|
| [`reply/`](reply/README.md) | every operation this build publishes — `campaign.get`, `campaign.create`, `list_membership.add`, `campaign.enroll`, `campaign.pause`, `campaign.activate`, `optout.record`, `enrollment.stop`, `enrollment.list`, `engagement.summarize`, `conversation.list`, `conversation.get`, `conversation.reply`, `conversation.classify`, `conversation.close`, `conversation.reopen`, `contact.update`, `contact.delete`, `sender.list`, `schedule.list`, `social_invitation.send`, `message.send`, `message.status` and `message.cancel` — against a Reply account, performed through the Reply CLI, which owns the credential. Its README is the operator's page: what it needs, how it is installed today, the account a route names, every failure it reports, and what it cannot do. |
| [`ntfy/`](ntfy/README.md) | the delivery contract `notification.send`: Jason's pointer — the counts the runtime composes, and where to look — published to an ntfy topic. Its README says how it is installed from the release, what a topic on the public server means, the settings that name it, the token, a self-hosted server, and every outcome it reports. |
| [`journal-forwarder/`](journal-forwarder/README.md) | the subscription contract `subscription.handle`: each journal line of a kind you grant it, POSTed as JSON to one URL you name, with the envelope answered unchanged. Its README says how it is installed from the release, the settings that grant it, what the receiver gets and that delivery is at least once, a receiver on another host, and every outcome it reports. |
| [`slack/`](slack/README.md) | the delivery contract `notification.send`: Jason's pointer — the counts the runtime composes, and where to look — posted to a Slack channel. Its README says how it is installed from the release, how to make the bot, the settings that name the channel, the token, and every outcome it reports. |
| [`telegram/`](telegram/README.md) | the delivery contract `notification.send`: Jason's pointer — the counts the runtime composes, and where to look — sent to a Telegram chat by a bot you make. Its README says how it is installed from the release, how to make the bot, the settings that name the chat, the token, and every outcome it reports. |

Each of these five is published with every release, Reply's from 0.11.0, the journal forwarder's from 0.30.0, and ntfy's,
Slack's and Telegram's from 0.32.0, as `jason-plugin-<id>.zip`, and `jason plugin install <id>` places it.

[`reply.digests`](reply.digests) lists the digest of each version the Reply package has had from 0.9.0 on, and of the
earlier versions a release published — the digest `jason plugin list` reports for an installed copy, whole in its JSON
answer; `--human` shows the first twelve hexadecimal characters after `sha256:` — so a person can check an installed
package against its version's line, and a copy whose digest is not its version's line is not that version.
[`journal-forwarder.digests`](journal-forwarder.digests) does the same for the journal forwarder's package, from 1.0.0,
the package 0.29.0 shipped in its tree; [`ntfy.digests`](ntfy.digests) for ntfy's, from the 1.0.0 that 0.2.0 shipped in
its tree; and [`slack.digests`](slack.digests) and [`telegram.digests`](telegram.digests) for Slack's and Telegram's.

**Writing one?** [docs/plugins.md](../docs/plugins.md) is the author's guide — package layout, every
manifest rule and its problem code, the Host SDK with every limit, capabilities and grants, the
invocation protocol, and how to test a package locally.

**What to implement** is §7 of that guide, "Implementing a canonical operation": the operations
themselves are published under [docs/contracts/](../docs/contracts/README.md), one document each,
and every one of them ends with the `conformance` list a conforming plugin has to demonstrate. The
four rules that hold whatever the operation — the recovery read on a repeated attempt, where a
failure's identifiers go, answering in the operation's own vocabulary, and working by a pin — are
under "What the runtime holds you to" in that same section.

[`ntfy/`](ntfy/README.md) is the smallest complete package to copy the shape of for a notification plugin,
[`journal-forwarder/`](journal-forwarder/README.md) for a subscriber, and
[`reply/`](reply/README.md) the one that implements every published operation. The stand-in package the
runtime's own tests drive is not published.
