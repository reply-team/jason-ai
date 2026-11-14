---
name: customising-jason
description: Use when a person asks to change how Jason itself works rather than to do campaign work - make a role write or behave differently, add a role, add or change a plugin for a service they use, send an operation to another plugin, or grant a plugin what it needs - and to say what an update keeps and what is never touched.
metadata:
  status: draft
---

# Customising Jason

If you cannot write to the role skills folder from where you run, say so, and tell the person to choose Open in ›
Change Jason itself in Jason's window, or to run `jason session open --setup` in a terminal. The desktop's chat is one
such place: it is kept from changing Jason's own files, and on Codex it can read them but not change them.

This skill is about changing Jason itself: the instructions its roles work by, the plugins that perform provider
work, where an operation's work goes, and what a plugin may reach. It is not about campaign work, which is
`managed-campaign-work`, and not about running the installation, which is `operating-the-installation`. A person
reaches it by saying what they want different, for example "make the copywriter write shorter in every campaign" or "add a plugin for
my CRM". Ask what they want changed if they have not said, change exactly that, and say afterwards where the change
now takes effect.

**Status: draft.** Its commands parse against this build.

## Read where the parts are

Never assume a folder. Ask the installation:

```
jason status --human
```

Below its checks it prints where this installation's parts are: the role skills folder, the plugins folder, the custom
UI folder and the settings file, and whether that file is also where plugin secrets are kept. The JSON body of
`jason status` carries the same under `setup`: `role_skills_directory`, `plugins_directory`, `custom_ui_directory`,
`settings_file` and `settings_file_holds_secrets`. It answers without a runtime, so it works when Jason is stopped.
Take every path from there; never assume one. Work in those folders and nowhere else: a path remembered from another
machine, another version or another installation is the wrong place to write.

`settings_file_holds_secrets` is a fact about how this Jason is laid out, not about what is set today: it is true
while plugin secrets are kept in the settings file, whether or not any is set yet.

## What a person may ask to change, and where it takes effect

| The person wants | What you do | It takes effect |
|---|---|---|
| a role to behave or write differently | edit that role's `SKILL.md` in the role skills folder | at the role's next piece of work, with nothing to reload |
| a role of their own | `jason role add`, then its folder and file, then optionally its profile, then give it work | when work names it |
| a plugin for a service they use | write or copy the package into the plugins folder, then `jason plugin reload`, then grants | at the reload, and the grants when written |
| an operation sent to another plugin | `jason route set --global` | at once |
| a plugin to be given a host, a program, a variable, an API family, a folder or a subscription | `jason plugin grant` | at once |
| a secret for a plugin | they type it themselves | at the next reload if written in the settings file; after a runtime restart if set as an environment variable |

### A role's instruction

A role's skill teaches that role for every campaign. When the person wants something for one campaign, for one
project, or as how they themselves write, that is not a change to Jason: it is kept where the pack's other skills keep
it. A standing rule for one campaign, for one project or for all of their work is the memory file at that level, the
campaign's, the project's or the client's (`planner-in-session`); a fact or a decision every role of one campaign
shares is that campaign's context (`managed-campaign-work`). How they sound is the `tone_of_voice` preference
(`planner-in-session`). Ask which of these the person means before you edit a role's file,
and edit the file only when they mean every campaign.

A role is taught by the `SKILL.md` in its own folder under the role skills folder, named by the role: the copywriter's
is in the folder `copywriter`. Edit that file. The runtime reads the folder at every launch, so what you write applies
to the role's next piece of work and nothing needs reloading. Work already running keeps the instruction it started
with.

Keep the file's front matter as it is: its `name` must equal the folder's name, and a role whose file is a link, cannot
be read, names itself differently from its folder or is over `Roles:MaxSkillBytes` is refused at launch rather than run
untaught, which `jason status` reports in a sentence ending in its code. Change what the role is told to do, not its
launch contract: how it reports, what it asks a person before acting, and the approvals around real people are the
parts that keep a role honest. Before a change that loosens one of them, say plainly what is removed, naming the thing
removed and not only that something is, and make the change once the person has heard it. Under an automatic send mode,
three of those parts are what a message stands on when it goes without the person reading it: the critic's pass on its
text, the last look before it goes; the doubts the role that drafted it raises, which send it to the person instead; and
the responder's judgement of a reply, which marks the ones a person must answer. An edit that makes the critic pass
more, a drafting role doubt less or the responder mark fewer replies for the person lets more go out unread: say that,
in those words, before you make it. Whether messages go automatically is the person's own word, set by them and never by
you (`approvals-and-questions`).

### A new role

```
jason role add <name> --description "<what the role does, in a sentence>" --entry-command <program> --entry-command <argument>
jason role set-profile <name> --execution-profile <profile> --actor human:<Name>
jason role list --human
```

A role is a name that matches `^[a-z][a-z0-9-]{0,63}$`. `role add` registers it. Then make its folder under the role
skills folder, named exactly like the role, with a `SKILL.md` whose front matter names the same: `name`, a
`description` that says when Jason launches it, and `metadata.status`. The file is what teaches it its job; write it
the way the shipped roles' files are written. Ask the person before registering one, and say why: its description and
entry command cannot be changed after `role add`, and it cannot be removed; its profile is the one thing
`jason role set-profile` changes later.

How the role runs is its profile or its entry command. A role with neither fails its work with `role_not_launchable`;
`jason role set-profile <name> --execution-profile <profile>` names a profile that exists (`jason profile list`), and
`--clear` takes it away. Naming a profile is the person's act, so it needs `--actor human:<Name>`.

**A role nobody's work names is never launched.** Registering it and writing its file does nothing by themselves.
Work reaches a role as a work item of kind `ai_role` that names it, and two things create one: the manager's plan, when
its role skill tells it the role exists and what it is for, and a person or agent by hand:

```
jason workitem create <campaign-id> --kind ai_role --role <name> --reason "<why this work>"
```

So say to the person that the new role runs when the manager is told to use it, or when they create its work, and offer
to create one such work item to see it work. It is an offer only: say what that work item will do, and create it only
on the person's yes. Anything that makes the manager use it belongs in the manager's own role file,
which is a change of an existing role's instruction like the first.

### A plugin

A plugin is a package: a directory named by its id with `plugin.yaml`, `main.js` and any modules. To add one, write it
or copy it into the plugins folder, then load it:

```
jason plugin reload --reason "<what was added or changed>"
jason plugin list --human
```

The reload reads every package again and swaps the snapshot, or keeps the old one and says what is wrong; fix what it
names and reload again. A package built here is yours: an update and `jason plugin install` leave it as it is unless
the person says otherwise. The plugin guide is `docs/plugins.md`
(<https://github.com/reply-team/jason-ai/blob/main/docs/plugins.md>). For an example, read a package already in the plugins folder: the smallest one a
release publishes is `ntfy`, which `jason plugin install ntfy` places there if it is not, and `reply` is the one
provider.

A plugin loads with nothing granted. Its manifest says what it asks for; what it gets is the person's grant:

```
jason plugin grant my-crm --http <host> --actor human:<Name>
jason plugin grant my-crm --exec <program> --actor human:<Name>
jason plugin grant my-crm --env <name> --actor human:<Name>
jason plugin grant my-crm --api <family> --actor human:<Name>
jason plugin grant my-crm --files <directory> --actor human:<Name>
jason plugin grant my-crm --subscription <kind> --actor human:<Name>
jason plugin revoke my-crm --http <host> --actor human:<Name>
```

A grant is written to the settings file by the runtime and is in force at once. The verb refuses what the manifest does
not request, except a host where the manifest says `any_granted_host`; so if a grant is refused, the plugin is not
asking for it, and the fix is the plugin's manifest, then a reload. Grant what the person agreed to, name what it lets
the plugin reach, and never grant `*`-wide anything the person has not heard. A revoke does not stop work already
running: it keeps what it was started with.

**A secret is the person's own to type.** A plugin's guide names the variable that holds its secret. Tell the person
the name and where to set it, and let them do it: never ask them to paste a secret into the chat, never write one into a
package, a role file, a command you run or a file of Jason's. There is no `jason plugin configure`.

When a secret reaches the plugin depends on where the person sets it. A value they write in the settings file is read
at the next `jason plugin reload`. A variable of the runtime's own environment, whether the plugin's configuration form
`JASON_Plugins__Config__<id>__<field>` or a variable the plugin is granted, is read when the runtime starts and not
again, so one set after the runtime started reaches the plugin only after `jason runtime restart`; ask the person
before you run that, as `operating-the-installation` says of any stop.

### A route

```
jason route list --human
jason route set --global --operation <operation> --plugin my-crm --actor human:<Name>
jason route unset --global --operation <operation> --actor human:<Name>
```

A route says which plugin performs an operation. The global route is the one every campaign without a route of its own
uses; a campaign's own route, set with `--campaign`, is read first. A global route is written to the settings file by
the runtime, which loads the plugins again, so the route is in force at once, and a load that refuses puts the file
back and says why.
`jason route list` shows what is in force. A route never falls back to another plugin: where the named plugin cannot do
the work, the work stops, which is the safe failure, so before you route it check that the plugin performs the
operation, in `jason plugin list`'s JSON (`--human` shortens the list), or with
`jason route resolve --campaign <id> --operation <operation>` after the route is set.

## What an update keeps

- **A role skill you edited is kept and reported.** Updating the skills, which `operating-the-installation` teaches, writes the release's files around a file of
  yours and names each one it kept; reconciling the two is yours. `--force` overwrites your edit with the release's
  text, and it is the person's to type, never yours to add. A file you deleted is put back. `jason update apply`
  redeploys the skills the installation recorded as its last step.
- **A package built or edited here is left as it is.** `jason update apply` replaces each plugin package installed as
  an earlier release's with the release's own, and touches no other package under the plugins folder, one you wrote
  included.
- **The settings file, the database and the grants and plugin configuration in the file are kept.** An update replaces
  the executable and its notice files, those release packages, the redeployed skills and the release's UI; it does not
  touch the rest of the data directory. The file is left as written, so a setting a release renames stays under its old
  name until the person carries it over.

## What you never touch

- The run file, the keys and the database: the first two are how the runtime and its clients trust each other, and the
  runtime alone writes the third.
- The settings file, while `settings_file_holds_secrets` is true in that section: plugins' secrets are kept in it. Never open it.
- Any file of Jason's outside the role skills folder and the plugins folder: routes and grants are changed with Jason's own commands, as above.
- A secret: it is the person's own to type. Never ask them to give one to you.

The chat's own skills belong to the desktop and are kept out of reach on purpose; nobody is meant to edit them, and
changing a role or a plugin never needs to.

## When it goes wrong

- A reload that refuses leaves the previous plugins in force: read its message, correct the package, reload again.
- A role refused at launch has a file that breaks the rules above: `jason status` says which and why.
- A route or grant refused names the reason in its error; do not work around it by editing the settings file.
