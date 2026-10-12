# Jason Runtime Licence

Version 1.0 — 10 October 2026

Copyright (c) 2026 ReplyApp Inc. All rights reserved.

> **Summary (not part of the licence).** The Jason runtime is free of charge and needs no Reply.io account. You may install and run it on as many of your own machines as you like, including cloud machines you rent, for your own business use. You may not redistribute it, host it for other companies, reverse-engineer it or modify it. Your plugins, skills, campaigns and data are yours. The skills, plugins, operation contracts, documentation and install scripts are separately licensed under MIT. The runtime comes with no warranty, and you are responsible for how you use it, including compliance with anti-spam, data-protection law and your providers' terms.

## 1. Acceptance

1.1. This licence is an agreement between you and **ReplyApp Inc.**, 6218 Georgia Ave. NW, Suite #1, Office 3048, Washington, DC 20011, USA ("**Reply.io**", "we", "us").

1.2. By downloading, installing, updating or using the Runtime, you accept this licence. This applies equally when the Runtime is installed or updated for you by a script, a deployment tool or an AI agent acting on your instructions: such a tool or agent acts on your behalf, and its acts bind you. If you do not accept this licence, do not download, install or use the Runtime.

1.3. If you accept this licence for a company or other organisation, you confirm that you have the authority to bind it, and "you" means that organisation.

1.4. The Runtime is licensed for business and professional use only, including by individuals acting in their trade or profession. By accepting this licence you confirm that you are not acting as a consumer.

## 2. Definitions

- "**Runtime**" means the Jason runtime executable in object-code form, as published by Reply.io in an official release archive, together with any update of it that Reply.io publishes. It does not include the Open Components.
- "**Official Source**" means the releases of the public repository `github.com/reply-team/jason-ai` or any other location Reply.io designates for publishing the Runtime.
- "**Open Components**" means the skills, plugins, operation contracts, documentation and install scripts that Reply.io publishes with Jason under the MIT licence, including copies of them embedded in the Runtime.
- "**Third-Party Components**" means the open-source software included in the Runtime and listed in the THIRD-PARTY-NOTICES file that accompanies the Runtime: in each official release archive, beside the executable after an official install or update, and in the public repository.
- "**Your Content**" means everything you or third parties create, configure or process with the Runtime: plugins, skills, settings, campaigns, contacts, approvals, notes, prompts, data and outputs.
- "**Your Machines**" means computers, servers and virtual machines that you own or control, including cloud machines you rent, wherever located.
- "**Internal Use**" means use by you, and by your employees and individual contractors acting on your behalf, for your own business or professional operations, including your own outbound sales.
- "**Managed Service**" means making the Runtime, or its functions, available to a third party for that third party's own operations, whether as a hosted service, a managed or outsourced service, software-as-a-service or in any similar way, whether or not for a fee.

## 3. Licence grant

3.1. Subject to this licence, Reply.io grants you a free-of-charge, worldwide, non-exclusive, non-transferable and non-sublicensable licence to:

(a) download the Runtime from the Official Source;

(b) install and run any number of copies of the Runtime on Your Machines for Internal Use; and

(c) make copies of the Runtime reasonably needed for backup, disaster recovery and the internal deployment of the Runtime to Your Machines (for example, in your own machine images or container images that are not made available outside your organisation).

3.2. **Contractors.** Your contractors may install and operate the Runtime on your behalf, for your Internal Use. You are responsible for their compliance with this licence.

3.3. Reply.io reserves all rights not expressly granted. The Runtime is licensed, not sold. No right to the Runtime's source code is granted.

## 4. Open Components, Third-Party Components and Your Content

4.1. The Open Components are licensed under the MIT licence, not under this licence. Nothing in this licence limits any right the MIT licence gives you, including for the copies embedded in the Runtime.

4.2. Third-Party Components are licensed under their own licences, reproduced in THIRD-PARTY-NOTICES. Where such a licence gives you rights that this licence would otherwise restrict, that licence prevails for that component.

4.3. Reply.io claims no right in Your Content. This licence does not extend to plugins, skills or other works that you or third parties create, even when the Runtime loads or runs them; you may license them on any terms you choose.

## 5. Restrictions

5.1. Except as this licence expressly permits, or as applicable law permits notwithstanding a contractual restriction, you must not:

(a) **redistribute** the Runtime: copy, publish, sell, rent, lease, lend, sublicense or otherwise make it available to any third party, including by mirroring it or bundling it into software, images or installers distributed outside your organisation. Third parties obtain the Runtime from the Official Source;

(b) **operate a Managed Service.** Running the Runtime on machines you rent from a cloud provider for your own Internal Use is not a Managed Service and is permitted. Hosting or operating the Runtime so that other companies use it for their own operations is not permitted without a separate written agreement with Reply.io (section 11);

(c) **reverse-engineer** the Runtime: decompile, disassemble, deobfuscate or otherwise attempt to derive its source code, structure or algorithms, except to the extent that applicable law expressly permits this despite this restriction (for example, decompilation for interoperability under Article 6 of Directive 2009/24/EC), and then only within the conditions that law sets;

(d) **modify** the Runtime, or create derivative works of it. Writing or installing plugins and skills, and configuring the Runtime through its documented settings, interfaces and contracts, is not modification;

(e) **remove or alter** any copyright, licence or other notice in or accompanying the Runtime;

(f) **circumvent** the Runtime's integrity checks, update mechanism or technical safeguards. Using the Runtime's documented settings, for example switching the update check off, is not circumvention; or

(g) use the Runtime in breach of applicable law.

5.2. This licence and the third-party notices accompany the Runtime in each official release archive, beside the executable after an install or update by Reply.io's official install scripts or the Runtime's own update command, and in the public repository. Installing or updating the Runtime that way keeps them; it does not remove them.

## 6. Versions and updates

6.1. Each version of the Runtime is licensed under the licence that accompanies that version.

6.2. Reply.io is not obliged to publish updates, fix defects or provide support. The Runtime may check whether a newer version exists and tell you; it installs an update only when you, or a tool or agent acting for you, ask it to.

6.3. When you install an update, the licence that accompanies the update applies to it. You may instead keep, or return to, an earlier version under the licence that accompanied that version.

## 7. Data

7.1. The Runtime runs on Your Machines and stores its data there. Reply.io does not receive Your Content, and apart from the requests described in section 7.2 the Runtime sends no telemetry, usage data or crash reports.

7.2. **Update check and downloads.** Unless you switch it off (by setting `Update:CheckEnabled` to `false`), the running Runtime asks whether a newer version exists five minutes after it starts and then once a day; both intervals can be configured, and a check you or your agent ask for (`jason update check`) is the same request. That check, and the downloads you request to install or update the Runtime, go to an address operated by Reply.io, which redirects them to GitHub. The update check identifies only the Runtime's version, including its build, and a download only the file requested; like any web server, that address also receives your IP address, the time of the request and the standard headers of the program that makes it. Reply.io uses these records only to operate and secure the service and to count downloads in aggregate, keeps them for no longer than 31 days, and does not combine them with any other data. Reply.io's privacy policy (https://reply.io/privacy-policy/) applies to them. After the redirect, and when you install skills, which are fetched from GitHub directly, GitHub's terms and privacy statement apply.

7.3. **Your providers and AI agents.** When you configure the Runtime to act at third-party providers through plugins, to start an AI agent, or to send notifications, the Runtime does so with your accounts and credentials, and the data involved is exchanged between you and those third parties under your agreements with them. Reply.io is not a party to those exchanges, even when the provider is Reply.io; Reply.io's services are governed by their own terms.

7.4. For personal data that you process with the Runtime, you are the controller. Apart from the records described in section 7.2, Reply.io does not process personal data through the Runtime.

## 8. Your responsibilities

8.1. The Runtime is a tool you operate. You decide which campaigns to run, which contacts to process, which messages to send, which actions require your approval and which AI agents and providers to use.

8.2. You are solely responsible for:

(a) complying with the laws that apply to your outreach and data, including laws on electronic marketing and unsolicited communications, data protection and privacy, and consumer protection;

(b) having a lawful basis for processing the personal data you use, and honouring opt-outs and data subject requests;

(c) complying with the terms of the providers and platforms you use through the Runtime, including any restrictions on automation (such as LinkedIn's);

(d) configuring and supervising approval rules, plugins and AI agents, and reviewing what they produce and do.

8.3. Features that help protect people, such as recording opt-outs or pausing campaigns, are aids, not guarantees, and do not shift your responsibilities to Reply.io. Nothing the Runtime, its skills or its documentation say is legal advice.

## 9. No warranty; limitation of liability

9.1. THE RUNTIME IS PROVIDED FREE OF CHARGE, "AS IS" AND "AS AVAILABLE", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING ANY WARRANTY OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, NON-INFRINGEMENT, ACCURACY, OR UNINTERRUPTED OR ERROR-FREE OPERATION, TO THE FULLEST EXTENT PERMITTED BY LAW.

9.2. TO THE FULLEST EXTENT PERMITTED BY LAW, REPLY.IO IS NOT LIABLE FOR ANY INDIRECT, INCIDENTAL, SPECIAL, CONSEQUENTIAL OR PUNITIVE DAMAGES, OR FOR LOSS OF PROFITS, REVENUE, BUSINESS, GOODWILL OR DATA, OR FOR FINES OR CLAIMS ARISING FROM YOUR OUTREACH, ARISING OUT OF OR IN CONNECTION WITH THE RUNTIME OR THIS LICENCE, HOWEVER CAUSED. REPLY.IO'S TOTAL LIABILITY UNDER THIS LICENCE SHALL NOT EXCEED THE GREATER OF THE AMOUNT YOU PAID FOR THE RUNTIME AND ONE HUNDRED US DOLLARS (USD 100).

9.3. Nothing in this licence excludes or limits liability that cannot be excluded or limited under applicable law, including liability for intentional breach or gross negligence, or for death or personal injury.

## 10. Termination

10.1. You may end this licence at any time by uninstalling the Runtime and deleting all copies.

10.2. This licence ends automatically and immediately if you breach it, without notice from Reply.io.

10.3. When this licence ends, you must stop using the Runtime and delete all copies of it. Your Content remains yours; nothing in this licence stops you from exporting or keeping it. Sections 4, 7.4, 8, 9, 10.3, 12 and 13 survive.

## 11. Other terms

Reply.io may license the Runtime to anyone on different terms, including a hosted or managed deployment under a separate commercial agreement. Where you have such a written agreement with Reply.io, it prevails over this licence to the extent they conflict.

## 12. Trademarks

This licence grants no right to use the names "Jason", "Jason AI", "Reply" or "Reply.io", or any related logo, except as described in the trademark note published at https://github.com/reply-team/jason-ai/blob/main/TRADEMARKS.md.

## 13. General

13.1. **Governing law and courts.** This licence is governed by the laws of the State of Delaware, USA, without regard to its conflict-of-law rules. Disputes are subject to the exclusive jurisdiction of the state courts located in New Castle County, Delaware, and the United States District Court for the District of Delaware.

13.2. **Export and sanctions.** You must comply with export-control and sanctions laws that apply to your use of the Runtime.

13.3. **Entire agreement.** This licence, with the documents it refers to, is the entire agreement between you and Reply.io about the Runtime, subject to section 11.

13.4. **Severability; no waiver; assignment.** If any provision is held unenforceable, it applies to the maximum extent permitted and the rest remains in force. A failure to enforce a provision is not a waiver. You may not assign this licence; Reply.io may assign it to a successor in its business.

13.5. **Language.** This licence is written in English. A translation is for convenience only.

13.6. **Contact.** Questions about this licence: legal@reply.io.
