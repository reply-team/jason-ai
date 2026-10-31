# The Runtime API

People call two different things around Jason "plugins". One is a package the runtime starts to reach a provider and
perform an operation there; how to write one is [docs/plugins.md](plugins.md). This page is about the other: a program
of your own that calls the runtime — a script, a report, a calculator that runs on its own timer, a page of your own —
reads what Jason holds and writes back what it worked out. On this page a *plugin* is always the first kind and a
*program* always the second.

The Runtime API is what the command line, the browser UI and the roles Jason starts all call, and a program calls the
same API with the same rights. This page describes everything about it that is open in this version, and nothing more.
That is not much yet, and asking for more is welcome.

## How a program finds the runtime

The runtime listens on the loopback address only: `127.0.0.1`, port `52766` by default, the next free port above it
when that one is taken, or the port `Runtime:Port` in `settings.json` names. A program does not build the address
itself. Once the runtime is listening it writes an **endpoint descriptor**, and a program reads the address there.

The descriptor is `run/runtime.json` under the data directory: `~/.jason/run/runtime.json`, or the same path under the
directory `JASON_DATA_DIR` names when that variable is set. It is one JSON object:

```json
{"api_version":"v1","runtime_version":"0.23.0","instance_id":"rt_…","pid":4242,"base_url":"http://127.0.0.1:52766","token":"…","started_at":"2026-10-26T09:00:00.000Z"}
```

| Field | What it is |
|---|---|
| `api_version` | the version segment of every route, `v1` |
| `runtime_version` | the version of the runtime that wrote it |
| `instance_id` | this run of the runtime; `system.info` answers the same id |
| `pid` | the runtime's process |
| `base_url` | where the API listens; every request goes here |
| `token` | the capability token, the next section's subject |
| `started_at` | when this run started |

The runtime writes the descriptor last, so a program that finds one finds an address that is already listening, and it
removes the descriptor when it stops. A descriptor can outlive its runtime, after a crash for one; a program that wants
to be sure asks `system.info` and compares the `instance_id` it answers with the descriptor's. No descriptor means no
runtime is running, and `jason runtime start` starts one.

Every start writes a new descriptor with a new `instance_id` and a new token. A program reads the descriptor when it
starts and again whenever the runtime answers `unauthorized`: the runtime it knew has stopped, and another may be
running.

Nothing reaches the API from another machine. The runtime listens on `127.0.0.1` alone, and a request that names any
host other than `127.0.0.1` or `localhost` is answered `400` with the code `invalid_request`.

## How a program authorises

Every request presents the **capability token** from the descriptor, in one header:

```
Authorization: Bearer <token>
```

The token is 256 random bits, made fresh at every start. The runtime keeps it in memory and writes it to the
descriptor, a file only the account that owns the installation can read, and to nowhere else a program could look.

The token is the whole authority. Whatever the API does, the token allows: there are no scopes, no second token for a
program, no key per program and no read-only token in this version. A program that holds it can do everything the
account that owns the installation can do with `jason`, including deciding work that reaches real people. The one
other holder is a browser paired with `jason ui open`, which is given a token of its own with the same rights
([docs/ui.md](ui.md)).

So a program keeps three rules:

- It reads the token from the descriptor, on the machine where the runtime runs, under the account that owns the
  installation.
- It never copies the token anywhere: not into another tool's configuration, a repository, a log, a message, a file of
  its own or a command line. It reads the descriptor when it needs the token, which changes at the next start anyway.
- It sends the token nowhere but the descriptor's `base_url`.

A request without the header, with a header that does not begin `Bearer `, or with a token that is neither the current
capability token nor a paired browser's is answered `401`, with `WWW-Authenticate: Bearer` and this body:

```json
{"error":{"code":"unauthorized","message":"A valid capability token is required: Authorization: Bearer <token> from the runtime descriptor.","retryable":false}}
```

A request that carries an `Origin` header naming anything but the runtime's own address is answered `403` with the code
`origin_refused` before its token is read. A program that is not a browser sends no `Origin`, and is not affected.

## The request form

- **Every operation is `POST /v1/{noun}.{verb}`**, reads included, with a JSON body; an operation that needs nothing
  takes `{}`. A name the runtime does not know is answered `404` with the code `not_found`.
- **JSON is snake_case**, in both directions: members, enum values and the keys of a map. A property written twice in
  one body is refused.
- **A member is matched by its exact spelling**, compared ordinally, at every depth of a request: `Name` is not `name`.
  At the top of a body, a name that differs from a member only in case is skipped like any member the operation does
  not take; inside a free-form object, such as a context or a patch's `set`, it is a key of its own beside the one it
  resembles; and among the arguments a work item carries for its operation, it is a name the operation's schema does
  not declare.
- **Timestamps are ISO-8601 in UTC**, as `2026-10-26T09:00:00.000Z`.
- **Ids are prefixed ULIDs**, and the prefix says what an id names. Among them: `cmp_` a campaign, `cnt_` a contact,
  `wi_` a work item, `att_` an attempt, `apr_` an approval, `dec_` a question, `blk_` a block, `jrn_` a journal entry,
  `rpt_` a report, `prj_` a project, `rol_` a role, `prf_` an execution profile, `sup_` a do-not-contact entry, `uit_` a
  UI token, `evs_` an event source, `evb_` a backfill, `rt_` a run of the runtime, and `rts_` and `snp_` the routing and
  plugin snapshots a route and an approval name.
- **Success is `200`** with the answer as it is, in no envelope.
- **An error is any other status**, with one shape:

  ```json
  {"error":{"code":"validation_failed","message":"…","retryable":false,"details":[{"field":"limit","code":"invalid","message":"…"}]}}
  ```

  `code` is snake_case and is what a program branches on; `message` is for a person; `retryable` says whether sending
  the same request again can succeed; `details` is there only when the error is about fields or names a record, one
  entry per problem, at most 50.
- **A list takes `limit` and `cursor`.** `limit` is 1 to 1000 and 100 when absent; the answer is
  `{"items":[…],"next_cursor":"…"}`, and `next_cursor` goes back verbatim as `cursor` for the next page, until it is
  `null`. Three lists answer whole and take neither: `route.list`, `plugin.list` and `ui.tokens`; so do
  `campaign.summary` and `campaign.activity`, for every campaign asked. `contact.threads` and
  `contact.thread` read a person's conversations from the provider while the request waits, and page as the provider
  does, by `limit` and `offset`, with `has_more`.
- **A request that changes something may carry `actor` and `reason`.** `actor` is
  `{"type":"human","id":"…"}`, with the type `human`, `role` or `attempt`; absent, it is a person who gave no name, and
  `system` is refused because it is the runtime's own. Some operations ask for more: `approval.approve`,
  `approval.reject`, `decision.answer`, `block.clear`, `preference.set` and `preference.unset` need an actor of type
  `human` that names the person, and `report.submit` needs a named reporter of any type but `system`. The runtime
  records the actor as the caller claims it and cannot verify it: it holds one token and knows no caller. The CLI
  claims one where its caller does not: without `--actor`, a launched executor's command is its attempt, and any other
  write is `human:<this account's name>`, the name the paired browser writes as — or, where the operating system gives
  no name, absent as before. The operations above that need a person, `workitem.resume` and `report.submit` take no
  such default: given no actor, the CLI refuses all eight itself, before any request, in one sentence that names the
  form — `--actor human:<Name>`, whoever ends the wait for a resume, and a person or a role for a report. `reason` is
  free text recorded beside the change, at most 2000 characters; `contact.create`, `contact.update`, `contact.archive`,
  `campaign.add_contacts`, `campaign.remove_contacts`, `suppression.add` and `suppression.remove` set it no limit of
  their own.
- **A partial update tells absent from `null`**: a field left out stays as it is, and a field sent as `null` is
  cleared where the field can be empty. A context is the exception: in `campaign.update_context` and
  `workitem.update`, its `set` stores `null` as a value, and its `unset` removes a key.
- **`campaign.create` and `workitem.create` take an `idempotency_key`** of the caller's choosing. The same request sent
  again under the same key answers with what the first one made instead of making another; a different request under
  a key already used is refused `409` with the code `idempotency_key_reused`. A campaign's key is unique in the
  installation and a work item's within its campaign; nothing makes a key up for you. `report.submit` takes an
  `idempotency_key` of its own, unique for the reporter.

## What `/v1/` holds

These are all the operations in this version, by noun, one line each. A line says what the operation does where the
runtime's own descriptions say it; an operation with only its name here does what its noun and verb say.

They are the runtime's own operations, on Jason's own records. The operations a plugin performs at a provider are
another vocabulary, one document each under `docs/contracts/` ([docs/contracts/](contracts/README.md)), and run as
`provider_op` work items. Four names are in both — `campaign.create`, `campaign.get`, `campaign.pause` and
`contact.update` — and here each of them means Jason's own record.
`docs/api/fixtures/` holds requests and answers the runtime's own tests recorded for many of them, one folder per
operation ([docs/api/fixtures/](api/fixtures/)).

Most of them are also a command: `campaign.list` is `jason campaign list`, an underscore in a verb is a hyphen in the
command (`campaign.list_contacts` is `jason campaign list-contacts`), and `--help` on a command says what it takes. The
`system` operations have no noun of their own on the command line, as their lines say.

### `system`

- `system.info`: what `jason runtime status` shows.
- `system.shutdown`: `jason runtime stop` sends it.
- `system.drain`: stop claiming; `jason update apply` sends it.
- `system.resume`: claim again. No command sends it.

### `campaign`

- `campaign.create`: Jason's campaign; the provider's campaign is a `provider_op` work item. Takes a caller's
  `idempotency_key`. A name is printed to people, so one holding a control character is refused, `validation_failed`
  with the detail `invalid` on `name`, here and by `campaign.update`.
- `campaign.get`: Jason's campaign, read without calling a provider.
- `campaign.list`: takes `project_id` and keeps that project's campaigns.
- `campaign.update`: takes `project_id`, which moves the campaign.
- `campaign.start`: starts Jason's own campaign, so its work can run; nothing at the provider is started.
- `campaign.pause`: Jason's own pause; the provider's pause is a `provider_op` work item.
- `campaign.archive`
- `campaign.update_context`
- `campaign.add_contacts`: the people Jason's campaign is about; enrolling them at the provider is a `provider_op`
  work item.
- `campaign.remove_contacts`
- `campaign.list_contacts`: the people of the campaigns named, or of every campaign not archived, each person once.
- `campaign.summary`: for each campaign named, or every campaign not archived, where its people stand now, whole.
- `campaign.activity`: for the same campaigns, what happened to their people between `since`, included, and `until`,
  not included, whole.
- `campaign.activity_by_period`: one count across the campaigns asked, a bucket at a time.

### `project`

- `project.create`: makes a project; the same create sent again is refused as `project_exists`, naming the project the
  first one made.
- `project.get`: the project, by exactly one of `project_id` and `slug`.
- `project.list`: every project, in the order they were made.
- `project.update`: renames a project; the slug and the folder stay as they are.
- `project.link`: records where the project's folder is, and its campaigns'.

### `preference`

- `preference.set`: sets a key's value whole.
- `preference.unset`: takes a key's value back.
- `preference.list`: every key the catalog knows, in key order, set or not.

### `contact`

- `contact.create`: a `first_name` or `last_name` holding a control character is refused as a campaign's name is, here,
  by `contact.update` and in a row of `campaign.add_contacts`.
- `contact.get`
- `contact.list`: `query` finds any part of a name, a company, a title or any channel value.
- `contact.update`: Jason's own record of the person; the provider's contact is a `provider_op` work item.
- `contact.archive`
- `contact.threads`: a person's threads, read from the provider when a person asks, outside any work item.
- `contact.thread`: one thread, read the same way.

### `workitem`

- `workitem.create`: takes a caller's `idempotency_key`.
- `workitem.get`
- `workitem.list`
- `workitem.count`
- `workitem.update`: a partial patch of what a caller owns: the window, the priority, the per-item limits, the execution
  profile, the result format and the context.
- `workitem.cancel`: stops the work for good.
- `workitem.heartbeat`: the attempt's "I am still here".
- `workitem.set_result`: progress worth keeping before the work is done; the whole result is replaced.
- `workitem.complete`: the attempt says how it ended.
- `workitem.wait`: the attempt ends by waiting, and its item waits until what it names has happened.
- `workitem.resume`: ends a wait before what it waited for has happened, on somebody's word.

`workitem.heartbeat`, `workitem.set_result`, `workitem.complete`, `workitem.wait` and `decision.raise` belong to the
attempt the runtime started for a work item: each takes that attempt's id and is refused for any other, so a program
that is not that attempt does not call them.

### `approval`

- `approval.list`: what is waiting; pending by default.
- `approval.get`
- `approval.approve`: the work is let back into the queue, where the claim checks that it is still what was approved.
- `approval.reject`: the work ends, carrying the decision and the person who made it.

### `block`

- `block.list`: what is blocked, newest first; open blocks by default.
- `block.get`: the block, or `block_not_found`.
- `block.clear`: a person's clear; the open block is cleared now, under their name.

### `decision`

- `decision.raise`: a running attempt's question; refused for any other caller.
- `decision.answer`: a person's answer.
- `decision.get`
- `decision.list`: what is waiting, oldest question first; pending by default.

### `report`

- `report.submit`: admits one report, or answers with the one it repeats.
- `report.get`: one admitted report, as it was submitted.
- `report.list`: what has been reported, oldest first.

### `role`

- `role.list`: the seeded roster first, and every later role in the order it was added.
- `role.add`
- `role.set_profile`: points a role at an execution profile, or takes the policy away.

### `rolenote`

- `rolenote.get`: one role's memory at one level.
- `rolenote.set`: replaces a role's note whole.
- `rolenote.list`: one level's memory, without any of it being read: which roles have written, how much and when.

### `profile`

- `profile.create`: a profile and the first revision of it, written together.
- `profile.update`: an edit, which is a new revision.
- `profile.get`: one profile, at the revision in force or at the one asked for.
- `profile.list`: the profiles in the order they were added; a disabled one only when it is asked for.
- `profile.disable`: out of service; the revisions stay, and new work no longer resolves to them.
- `profile.enable`

### `journal`

- `journal.append`
- `journal.list`

### `plugin`

- `plugin.list`: each plugin's status, granted over requested, and digest.
- `plugin.reload`: reads the plugins again.

### `ui`

- `ui.open`: mints a single-use ticket good for 60 seconds, which a page trades for a UI token.
- `ui.tokens`: each pairing, with who paired and when, and when it was last used.
- `ui.revoke`: revokes a UI token, which the runtime accepts as a bearer until then.

### `route`

- `route.resolve`: what would perform this operation for this campaign, and, when the route cannot be run, why not.
- `route.list`: every route, or the global set plus one campaign's.
- `route.set`: sends one campaign's work, or one of its operations, to a plugin.
- `route.unset`: takes one campaign route away.

### `suppression`

- `suppression.add`: adds to the do-not-contact list, global across campaigns and scoped to one channel; an existing
  pair answers with the entry already there.
- `suppression.remove`: lifts a suppression; the reason is mandatory.
- `suppression.list`: the do-not-contact list.

### `events`

- `events.sources`: every account that has a source, a page at a time, in the order each was first bound.
- `events.backfill`: asks for a span of an account to be read again.
- `events.signal`: asks for an account to be read sooner.

## What a program can read

- **Campaigns**: `campaign.list` and `campaign.get`; `campaign.summary` for counts, `campaign.activity` and
  `campaign.activity_by_period` for what happened over a span; `campaign.list_contacts` for who is in one.
- **People**: `contact.list` and `contact.get`, with a contact's channels and custom fields; `contact.threads` and
  `contact.thread` for their conversations, read from the provider when asked and kept nowhere.
- **Work**: `workitem.list`, `workitem.get` and `workitem.count`.
- **What waits for a person**: `approval.list` and `approval.get` for work that reaches a real person and waits for a
  person's approval, `decision.list` and `decision.get` for the questions roles have asked.
- **Blocks**: `block.list` and `block.get`, for what a provider said it refuses until a person clears the cause.
- **Events**: `events.sources`, for every account whose events the runtime reads and how far that reading has got.
- **The journal**: `journal.list`, the record of what happened in each campaign, to its people and to its work, one
  line at a time, each with a `kind`, the actor, the time, and what changed. It is append-only: no operation changes
  or removes a line, so a line read once stays read. `journal.list` answers the newest line first, and its cursor pages
  back toward older ones; it narrows by `campaign_id`, `contact_id`, `work_item_id`, `kind` or `kinds`, and by `since`,
  which keeps the lines at or after a moment. A line carries the moment it was written, which can come before the
  moment it can be read. So a program that runs on its own timer reads with `since` set a minute before its last
  read, pages until `next_cursor` is `null`, and skips the ids it has already handled.

## What a program can write so its result comes back as a fact

- **A contact's custom fields**, through `contact.update` with `custom`. `custom` is replaced whole and the last write
  wins, so a program reads the contact, adds or changes its own fields in the object, and sends the whole object back.
- **A line in a campaign's journal**, through `journal.append`: a `kind` of the program's own — a lowercase letter, then
  lowercase letters, digits or underscores, up to 64 characters — and an optional `key` and `new` value. A kind the
  runtime writes itself is refused, and the runtime's kinds grow with its releases, so a program gives its kinds a
  prefix of its own.
- **An effect performed outside Jason**, through `report.submit`, when something was sent, enrolled or replied by hand
  or through another tool and Jason should know. The report is kept as the claim it is; what it carries is in
  [the reporting skill](../skills/runtime/reporting-outside-effects/SKILL.md).
- **A decision in a person's name**: `approval.approve`, `approval.reject` and `decision.answer`. Each needs an
  `actor` of type `human` that names the person, and each is refused for any other type. The runtime cannot tell a
  person from a program that holds the token, so a program records only what a person decided
  ([the approvals skill](../skills/runtime/approvals-and-questions/SKILL.md)).
- **Work**: `workitem.create`, with `kind` `ai_role` for a role's work or `provider_op` for an operation a plugin
  performs, its arguments in the context's `input` and checked against the operation's contract
  ([docs/contracts/](contracts/README.md)). Work that reaches a real person waits for a person's approval, however it
  was created.
- **A nudge**: `events.signal` asks the runtime to read one account's events sooner, for a program that receives a
  provider's webhooks. It names the account by its id from `events.sources` and carries nothing the webhook said;
  what is read is what the account's own read finds.

## Where a program's own pages can live

The runtime serves one browser build under `/ui/` at its address: the one in `ui/custom/` under the data directory when
that directory holds a build, else the release's own in `ui/default/`. A build is an `index.html` and, beside it, a
`manifest.json` that holds exactly `version` and `api_version`: the build's version and the API version it was built
for. A manifest with any other member makes the directory not a build, and the release's UI is served instead.

The runtime serves files of these kinds and no other: `.html`, `.js`, `.css`, `.json`, `.map`, `.svg`, `.png`, `.ico`,
`.woff2`, `.woff` and `.txt` — pages, scripts, styles, data, source maps, SVG, PNG and ICO images, WOFF and WOFF2
fonts, and plain text. Every answer carries
`Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; base-uri 'none'`, and the runtime reads the
directory at every request, so a build placed there is served at once and deleting it brings the release's back.

A build in `ui/custom/` is served instead of the release's UI, not beside it. Its pages pair as the release's do. The
link `jason ui open` prints carries a ticket, good for 60 seconds and for one use. The page trades it at
`POST /ui/session`, with the body `{"ticket":"…"}` and the runtime's own address as its `Origin`, and is answered with
a UI token and the token's id; it presents that token as a bearer from then on ([docs/ui.md](ui.md)).

## What is stable

`/v1/` is the only version, and it holds the operations listed above. Within a version the runtime changes the API by
adding to it: an operation, a field, a section of an answer. Jason is in alpha, and what it does still changes from one
release to the next ([README](../README.md)); what a release changes is in its notes.

What is not open in this version:

- **No push.** The runtime pushes nothing to a program: no webhooks, streams or subscriptions, and a program asks.
  The only programs it starts are its plugins and the roles it launches for work (`role.add`).
- **No remote access.** The API answers on `127.0.0.1` alone, and nothing reaches it from another machine.
- **No users.** One runtime serves the one account that owns the installation, and every caller acts with that
  account's authority.
- **No scoped token.** There is no token narrower than the capability token: none read-only, none for one campaign,
  none for one program. A paired browser's token is no narrower either, and unlike the capability token it is kept
  across restarts until `ui.revoke` revokes it.
