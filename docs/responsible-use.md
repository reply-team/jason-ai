# Running Jason responsibly

This page is for a person who runs Jason, and for the AI agent working for them. It says what the runtime sends off
your machine and what stays your responsibility when you use it to reach people. Jason is released under the MIT
licence ([LICENSE](../LICENSE)); these are statements about the product, not terms of that licence, and nothing here
asks you to accept anything. They are the statements the runtime's earlier licence made, in its words.

## Data

The runtime runs on your machines and stores its data there. ReplyApp Inc. does not receive your content, and apart
from the requests described below the runtime sends no telemetry, usage data or crash reports.

### Update check and downloads

Unless you switch it off (by setting `Update:CheckEnabled` to `false`), the running runtime asks whether a newer
version exists five minutes after it starts and then once a day; both intervals can be configured, and a check you or
your agent ask for (`jason update check`) is the same request. That check, and the downloads you request to install or
update the runtime, go to an address operated by ReplyApp Inc., which redirects them to GitHub. The update check
identifies only the runtime's version, including its build, and a download only the file requested, and the `ref` of
the link you followed where it carries one; like any web server, that address also receives your IP address, the time
of the request and the standard headers of the program that makes it. ReplyApp Inc. uses these records only to operate
and secure the service and to count downloads in aggregate, keeps them for no longer than 31 days, and does not
combine them with any other data. ReplyApp Inc.'s privacy policy (<https://reply.io/privacy-policy/>) applies to them.
That address counts the downloads and install-page reads it answers by day, file, the `ref` the followed link names
and the kind of program that asked (a browser, a command-line or agent fetcher, or a crawler), and keeps nothing of
the person in those counts. After the redirect, and when you install skills, which are fetched from GitHub directly,
GitHub's terms and privacy statement apply.

### Your providers and AI agents

When you configure the runtime to act at third-party providers through plugins, to start an AI agent, or to send
notifications, the runtime does so with your accounts and credentials, and the data involved is exchanged between you
and those third parties under your agreements with them. ReplyApp Inc. is not a party to those exchanges, even when the
provider is ReplyApp Inc.; ReplyApp Inc.'s services are governed by their own terms.

### Personal data

For personal data that you process with the runtime, you are the controller. Apart from the records described above,
ReplyApp Inc. does not process personal data through the runtime.

## Your responsibilities

The runtime is a tool you operate. You decide which campaigns to run, which contacts to process, which messages to
send, which actions require your approval and which AI agents and providers to use.

You are solely responsible for:

(a) complying with the laws that apply to your outreach and data, including laws on electronic marketing and
unsolicited communications, data protection and privacy, and consumer protection;

(b) having a lawful basis for processing the personal data you use, and honouring opt-outs and data subject requests;

(c) complying with the terms of the providers and platforms you use through the runtime, including any restrictions
on automation (such as LinkedIn's);

(d) configuring and supervising approval rules, plugins and AI agents, and reviewing what they produce and do.

Features that help protect people, such as recording opt-outs or pausing campaigns, are aids, not guarantees, and do
not shift your responsibilities to ReplyApp Inc. Nothing the runtime, its skills or its documentation say is legal
advice.
