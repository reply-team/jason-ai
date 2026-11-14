# Changing Jason

Jason is meant to be changed by the person who runs it, and the easiest way is to ask your own AI. Tell it what you
want different - "make the copywriter write shorter in every campaign", "add a plugin for my CRM" - and it changes the right part. A
role's new instruction applies to its next piece of work, a plugin after one reload, and an update keeps what you
changed.

## How to ask

- **In the desktop**, choose **Open in › Change Jason itself** ([the desktop guide](desktop.md#open-in)). It opens your
  own AI (the one your preference names, else Claude Code, else Codex) on Jason's roles and plugins. The desktop's chat cannot do this: it is kept from changing
  them on purpose (on Codex it can read those files but not change them), so that it can never rewrite the instructions Jason's own agents work by.
- **From a terminal**, run `jason session open --setup`. It opens the same session, and opening it changes nothing.

Then say what you want. The session's first message points the AI to the `customising-jason` skill
([`skills/runtime/customising-jason`](../skills/runtime/customising-jason/SKILL.md)); where that skill is not installed
for your AI yet, it asks you before it installs it with `jason skills install`. This page is the same content for a
person.

## Where the parts are

You never need to remember a folder. `jason status --human` prints where this installation keeps its role skills, its
plugins, its custom UI and its settings file, and says whether the settings file is also where plugin secrets are kept.
`jason status` prints the same as JSON under `setup`, and it answers when Jason is not running.

## What can be changed, and when it takes effect

| You want | What happens | In force |
|---|---|---|
| A role to write or behave differently | its skill file is edited | at the role's next piece of work |
| A role of your own | `jason role add`, a folder and a file for it, optionally a profile | when work names it |
| A plugin for a service you use | the package is written or copied in, then `jason plugin reload` | at the reload |
| A plugin to be allowed something | `jason plugin grant` | at once |
| An operation sent to another plugin | `jason route set --global` | at once |
| A secret for a plugin | you type it yourself | at the next reload if written in the settings file; after a runtime restart if set as an environment variable |

### A role's instruction

A role's skill teaches that role for every campaign. If what you want is for one campaign, for one project, or is how you
yourself write, that is not a change to Jason: a standing rule is kept in the memory file of that campaign, that
project or your own, a fact the campaign's roles share in its context, and how you sound in the tone of voice
preference. Your AI asks which you mean before it edits a
role's file.

Each role is taught by one skill file in its own folder. Your AI edits that file and the change applies to the role's
next piece of work; nothing is reloaded, and work already running keeps what it started with. A role that is refused at
launch, because its file is unreadable or names itself differently from its folder, is reported by `jason status`. If you ask it to loosen what a role reports or what it asks you before acting, it tells you
first what the change removes. Under an automatic send mode that matters most for three things: the critic, whose pass
is the last look before a message goes without you reading it, the doubts a drafting role raises, and the replies the
responder marks for you. Your AI tells you first when a change would let more go out unread.

### A new role

`jason role add <name>` registers the role. Your AI then writes its folder and skill file, and, if the role should run
on a particular agent host, names its execution profile with `jason role set-profile` ([profiles](execution-profiles.md)).
A role nobody's work names is never launched: it runs when the manager is told to use it, which is a line in the
manager's own skill, or when someone creates work for it with `jason workitem create --kind ai_role --role <name>`.
A role's description and entry command cannot be changed after `role add`, and the role cannot be removed, so your AI
asks you before it registers one. It may offer to create
one piece of work for the new role to see it run; it says what that work will do, and creates it only if you say yes.

### A plugin

A plugin is a package in the plugins folder: a directory with its manifest and `main.js`. Your AI writes or copies it
there and runs `jason plugin reload`, which loads every package again or keeps the old ones and says what is wrong.
`jason plugin list` shows the result. [The plugin guide](plugins.md#4-installing-reloading-and-listing) is the
contract. For an example, read a package already in your plugins folder: the smallest one a release publishes is
[`ntfy`](../plugins/ntfy/README.md), which `jason plugin install ntfy` places there, and
[`reply`](../plugins/reply/README.md) is the one provider.

A new plugin is given nothing. It asks, in its manifest, for the hosts, programs, variables, Runtime API families, folders and
journal subscriptions it needs, and you decide what it gets:

`jason plugin grant <id> --http <host>`, `--exec <program>`, `--env <name>`, `--api <family>`, `--files <directory>` or
`--subscription <kind>`, and `jason plugin revoke` to take one back. A grant is in force at once, and a grant the
plugin's manifest does not ask for is refused. Work already running keeps what it was started with.

### A secret

A secret is yours to type. The plugin's guide names the variable that holds it; set it yourself, and never paste it into
a chat or have it written into a package. There is no command that takes a secret for you. A value written in the settings file reaches the plugin at the next
`jason plugin reload`; a variable set in the runtime's own environment is read when the runtime starts, so one set
after it started needs `jason runtime restart`.

### A route

A route says which plugin performs an operation. `jason route set --global --operation <operation> --plugin <id>` sends
that operation's work to a plugin for every campaign that has no route of its own, and `jason route unset --global`
takes it away. Jason writes the settings file itself, loads the plugins again, and puts the file back if the load
refuses. `jason route list` shows what is in force. [Routing](routing.md) is the contract.

## What an update keeps

- A role skill you edited is kept and reported; `jason skills update --force`, which is yours to type, overwrites it with
  the release's text.
- A plugin package you wrote or edited is left as it is; `jason update apply` replaces only the packages installed as an
  earlier release's with the release's own.
- Your settings file, with the grants, routes and plugin configuration in it, and your database stay: an update touches
  neither. A setting a release renames stays in the file under its old name until you carry it over.

## What is never touched

The skill tells your AI never to open or change:

- the run file, the keys or the database;
- the settings file, while that file is where plugins' secrets are kept (`jason status` says whether it is);
- any other file of Jason's outside the role skills folder and the plugins folder: routes and grants are changed with
  Jason's own commands, which are the ones above.

A secret is yours to type, and the skill tells your AI never to ask you for one. The desktop's chat has skills of its own that
belong to the desktop and are not meant to be edited.
