// The plugin every test drives, and the shape a real plugin copies: one ES module exporting one function, which
// is handed the operation, its input and the context, and answers with { result } or throws host.fail({...}).
// There is no console, no require and no filesystem here — everything a plugin can reach is on `host`.
import { helper } from "./modules/helper.js";

// The SDK reads a request object strictly, so a key whose value is undefined is dropped rather than passed on.
function compact(request) {
  const cleaned = {};
  for (const key of Object.keys(request)) {
    if (request[key] !== undefined) {
      cleaned[key] = request[key];
    }
  }

  return cleaned;
}

function probe(attempt) {
  try {
    return { reached: attempt() };
  } catch (error) {
    return { error: error.name };
  }
}

// ---------------------------------------------------------------------------------------------------------
// The canonical operations, implemented against the stand-in vendor's account
// ---------------------------------------------------------------------------------------------------------

// What this plugin reports, for every canonical operation it performs: the endings every one of them can meet, then
// the provider's refusals each one can meet, keyed by the provider's own code. Each row is the code and the class the
// operation's contract publishes for it, because the runtime accepts a failure only under a code the operation
// declares and with the class it declares it with: a caller must never have to guess. A refusal no row names is
// `unknown_refusal`: this plugin has no word for it, so it is a failed call rather than a code no contract publishes.
// The table is a strict JSON object literal so that a test can read it and hold every row to the contracts.
export const REPORTS = {
  "shared": {
    "no_binding": { "code": "unauthorized", "class": "permanent" },
    "not_called": { "code": "provider_call_failed", "class": "permanent" },
    "answer_lost": { "code": "provider_answer_lost", "class": "ambiguous" },
    "unknown_refusal": { "code": "provider_call_failed", "class": "permanent" },
    "account_blocked": { "code": "provider_blocked", "class": "blocked" },
    "rate_limited": { "code": "rate_limited", "class": "transient" },
    "unauthorized": { "code": "unauthorized", "class": "permanent" }
  },
  "per_operation": {
    "campaign.get": {
      "campaign_not_found": { "code": "campaign_not_found", "class": "permanent" }
    },
    "list_membership.add": {
      "no_channel": { "code": "invalid_channel_value", "class": "validation" },
      "contact_not_found": { "code": "contact_not_found", "class": "permanent" },
      "invalid_channel_value": { "code": "invalid_channel_value", "class": "validation" },
      "limit_reached": { "code": "limit_reached", "class": "permanent" },
      "list_not_found": { "code": "list_not_found", "class": "permanent" },
      "suppressed": { "code": "suppressed", "class": "permanent" }
    },
    "campaign.enroll": {
      "no_channel": { "code": "invalid_channel_value", "class": "validation" },
      "campaign_not_enrollable": { "code": "campaign_not_enrollable", "class": "permanent" },
      "campaign_not_found": { "code": "campaign_not_found", "class": "permanent" },
      "collision_refused": { "code": "collision_refused", "class": "permanent" },
      "contact_not_found": { "code": "contact_not_found", "class": "permanent" },
      "invalid_channel_value": { "code": "invalid_channel_value", "class": "validation" },
      "limit_reached": { "code": "limit_reached", "class": "permanent" },
      "suppressed": { "code": "suppressed", "class": "permanent" }
    }
  }
};

function has(table, key) {
  return table !== null && typeof table === "object" && Object.prototype.hasOwnProperty.call(table, key);
}

// The row `key` is while performing `operation`: the operation's own, then the shared one, then `unknown_refusal`.
function report(operation, key) {
  const own = REPORTS.per_operation[operation];
  if (has(own, key)) {
    return own[key];
  }

  return has(REPORTS.shared, key) ? REPORTS.shared[key] : REPORTS.shared.unknown_refusal;
}

// The one way a canonical operation fails here: the row decides the code and the class, and a block travels only
// beside the class that carries one.
function fail(operation, key, failure) {
  const row = report(operation, key);
  throw host.fail(compact({
    class: row.class,
    code: row.code,
    message: failure.message,
    details: failure.details,
    external_ids: failure.learned,
    block: row.class === "blocked" ? failure.block : undefined,
  }));
}

// The provider's own words for a campaign's state, mapped onto the vocabulary the contract publishes. Anything
// this table has no word for is `other`, and the provider's own word survives under `vendor`.
const CAMPAIGN_STATUS = {
  Draft: "draft",
  Active: "live",
  Paused: "paused",
  Archived: "archived",
};

// The vendor program's own exit codes, and the two of them that say the call never became work: the host
// answers -1 when it could not start the program at all, and the program answers 2 when it will not make
// sense of the call or of the account it was pointed at. Neither can have had an effect at the provider.
const NOT_STARTED = -1;
const REFUSED_THE_CALL = 2;

// The vendor program this plugin drives, by the name the manifest declared and the user granted. The runtime
// decided what that name means at reload; nothing here looks a program up for itself.
const PROVIDER_CLI = "Jason.FakeProviderCli";

// One call to the vendor CLI: the request goes in on stdin and exactly one answer comes back on stdout. The
// `learned` argument is what this plugin already knows the provider calls our entities at the moment of the call:
// it travels out with any failure, because after a lost answer a pin is the only trace of what was done. The
// `operation` is the canonical one being performed, which decides the row a failure is reported under.
function cli(operation, context, subcommand, request, learned) {
  const workspace = context.binding && context.binding.workspace;
  if (!workspace) {
    fail(operation, "no_binding", { message: "This plugin needs a binding naming the account it should act in." });
  }

  const answer = host.exec({
    executable: PROVIDER_CLI,
    args: ["--workspace", workspace, ...subcommand],
    stdin: JSON.stringify(request),
  });

  if (answer.exit_code !== 0) {
    // Not every non-zero exit is the same not-knowing. A program that never started, or that refused the call
    // before touching the account, cannot have acted: that is a `permanent` failure and says so. A timeout, or
    // an exit once the call was under way, may have acted — which is what `ambiguous` is for, and the
    // contract's recovery read is how the next attempt finds out.
    const acted = answer.timed_out === true
      || (answer.exit_code !== NOT_STARTED && answer.exit_code !== REFUSED_THE_CALL);

    fail(operation, acted ? "answer_lost" : "not_called", {
      message: acted
        ? "The provider was called and no answer came back."
        : "The provider was never called: the program would not start, or refused the call it was given.",
      details: {
        subcommand: subcommand.join(" "),
        exit_code: answer.exit_code,
        timed_out: answer.timed_out === true,
      },
      learned: learned,
    });
  }

  const parsed = JSON.parse(answer.stdout);
  if (parsed.error) {
    // A block is the contract's own code, whatever the account calls it, and says what a person clears. A refusal
    // this plugin has no row for keeps the account's own word in the details, where a person reads it.
    const named = has(REPORTS.per_operation[operation], parsed.error.code) || has(REPORTS.shared, parsed.error.code);
    fail(operation, parsed.error.code, {
      message: parsed.error.message,
      details: named ? undefined : { provider_code: parsed.error.code },
      learned: learned,
      block: { scope: "account", reason: "billing", link: "https://provider.example/billing" },
    });
  }

  return parsed;
}

// The provider's own campaign identifier: the pin when there is one, otherwise the one the planner supplied to
// make the first link.
function campaignOf(input) {
  return (input.args && input.args.campaign && input.args.campaign.external_id)
    || (input.campaign && input.campaign.external_ids && input.campaign.external_ids.campaign);
}

// The one channel this operation consumes. The projection carries exactly that channel and no other, so a
// person who arrives without it is a call that no later attempt could accept either.
function channelOf(operation, contact, name) {
  for (const channel of contact.channels) {
    if (channel.channel === name) {
      return channel;
    }
  }

  fail(operation, "no_channel", { message: "The contact carries no " + name + " channel." });
}

// The precondition every write shares: the provider's own contact must exist, worked by the pin when there is
// one and created from the channel value only when there is not.
function ensureContact(operation, context, input, channel) {
  const contact = input.contacts[0];
  const pinned = contact.external_ids && contact.external_ids.contact;
  return cli(operation, context, ["contact", "ensure"], compact({
    channel: channel.channel,
    value: channel.value,
    external_id: pinned,
  }));
}

// The provider's identifier for this person, where Jason already holds one. Everything a later attempt can read
// about what an earlier one did hangs off it.
function pinnedContact(input) {
  const contact = input.contacts[0];
  return (contact.external_ids && contact.external_ids.contact) || null;
}

// The ledger this account keeps per idempotency key: what the prior run under this key decided.
function ledgerEntry(operation, context, key) {
  const recorded = cli(operation, context, ["ledger", "get"], { key: key });
  return recorded.found ? recorded.entry : null;
}

// The recovery read `list_membership.add` obliges, in full: on any attempt after the first, the membership of the
// pinned contact in the list is read, and then the ledger under this key, before anything is written. Both,
// because the contract names both — the ledger says what the prior run decided, the membership says what the
// account actually holds, and an account can hold the member with no ledger entry a crash never reached.
function recoverMembership(context, input, key) {
  if (!context.attempt_number || context.attempt_number < 2) {
    return null;
  }

  const pinned = pinnedContact(input);
  const held = pinned
    ? cli("list_membership.add", context, ["list", "membership"], { list_id: input.args.list.external_id, contact_id: pinned })
    : { member: false };
  const recorded = ledgerEntry("list_membership.add", context, key);

  if (held.member) {
    return { contact_id: pinned };
  }

  return recorded;
}

// The same obligation for `campaign.enroll`: the prior run's own outcome under this key, and the campaign's live
// state, because whether the enrollment was a send is what makes repeating it expensive.
function recoverEnrollment(context, input, key) {
  if (!context.attempt_number || context.attempt_number < 2) {
    return null;
  }

  // In the order the document names them: what the prior run decided, then whether the campaign is live, which
  // is what says whether that decision was a send.
  const recorded = ledgerEntry("campaign.enroll", context, key);
  const live = cli("campaign.enroll", context, ["campaign", "get"], { external_id: campaignOf(input) }).live === true;
  return recorded ? { contact_id: recorded.contact_id, live: live } : null;
}

// Whether the provider found the contact or created it, as its own answer to `contact ensure` says. An answer that does
// not say leaves the origin unsaid: the runtime records none, and never guesses one.
function withOrigin(answer, ensured) {
  if (!ensured || typeof ensured.created !== "boolean") {
    return answer;
  }

  return Object.assign({}, answer, { external_id_origins: { contact: ensured.created ? "created" : "found" } });
}

function membershipResult(contactId, status, providerContact) {
  return {
    result: { items: [{ contact_id: contactId, status: status, external_ids: { contact: providerContact } }] },
    external_ids: { contact: providerContact },
  };
}

function enrollmentResult(contactId, status, providerContact, providerCampaign, live) {
  return {
    result: {
      items: [{ contact_id: contactId, status: status, external_ids: { contact: providerContact } }],
      campaign_live: live === true,
    },
    external_ids: { contact: providerContact, campaign: providerCampaign },
  };
}

function campaignGet(input, context) {
  const answer = cli("campaign.get", context, ["campaign", "get"], { external_id: campaignOf(input) });

  return {
    result: {
      campaign: {
        external_id: answer.id,
        name: answer.name,
        status: CAMPAIGN_STATUS[answer.status] || "other",
        counts: answer.counts,
      },
      vendor: answer.vendor,
    },
    external_ids: { campaign: answer.id },
  };
}

function listMembershipAdd(input, context) {
  const contact = input.contacts[0];
  const key = input.idempotency_key;

  const already = recoverMembership(context, input, key);
  if (already) {
    return membershipResult(contact.id, "already_member", already.contact_id);
  }

  const channel = channelOf("list_membership.add", contact, input.args.channel);
  const ensured = ensureContact("list_membership.add", context, input, channel);
  const added = cli("list_membership.add", context, ["list", "add"], {
    list_id: input.args.list.external_id,
    contact_id: ensured.id,
    key: key,
  }, { contact: ensured.id });

  return withOrigin(membershipResult(contact.id, added.status, ensured.id), ensured);
}

function campaignEnroll(input, context) {
  const contact = input.contacts[0];
  const key = input.idempotency_key;
  const campaign = campaignOf(input);

  const already = recoverEnrollment(context, input, key);
  if (already) {
    return enrollmentResult(contact.id, "already_enrolled", already.contact_id, campaign, already.live);
  }

  const channel = channelOf("campaign.enroll", contact, input.args.channel);
  const ensured = ensureContact("campaign.enroll", context, input, channel);
  const enrolled = cli("campaign.enroll", context, ["campaign", "enroll"], {
    campaign_id: campaign,
    contact_id: ensured.id,
    key: key,
    collision: input.args.collision,
    start: input.args.start.position === "step" ? "step:" + input.args.start.step : "first_step",
    first_touch: input.args.first_touch,
  }, { contact: ensured.id, campaign: campaign });

  return withOrigin(enrollmentResult(contact.id, enrolled.status, ensured.id, campaign, enrolled.live), ensured);
}

export function invoke(operation, input, context) {
  if (operation.startsWith("fail.")) {
    throw host.fail(compact({
      class: operation.slice(5),
      code: input.code ?? "provoked",
      message: input.message ?? "provoked failure",
      details: input.details,
      external_ids: input.external_ids,
      block: input.block,
    }));
  }

  switch (operation) {
    case "campaign.get":
      return campaignGet(input, context);

    case "list_membership.add":
      return listMembershipAdd(input, context);

    case "campaign.enroll":
      return campaignEnroll(input, context);

    case "echo.run":
      return { result: { echo: input } };

    case "context.echo":
      return { result: context };

    case "exec.run":
      return {
        result: host.exec(compact({
          executable: input.executable,
          args: input.args ?? [],
          stdin: input.stdin,
          timeout_ms: input.timeout_ms,
          env: input.env,
        })),
      };

    case "http.get":
      return {
        result: host.http(compact({
          method: input.method ?? "GET",
          url: input.url,
          headers: input.headers,
          body: input.body,
          timeout_ms: input.timeout_ms,
        })),
      };

    case "env.read":
      return { result: { value: host.env(input.name) ?? null } };

    case "config.read":
      return { result: { value: host.config(input.key), type: typeof host.config(input.key) } };

    case "log.emit":
      host.log(input.level ?? "info", input.message, input.data);
      return { result: { logged: true } };

    case "hang.forever":
      for (;;) {
        // Never ends: the engine's own limits are what stop this.
      }

    case "spew.bytes":
      return { result: "x".repeat(input.bytes) };

    case "throw.plain":
      throw new Error("boom");

    case "return.bare":
      return 42;

    case "escape.clr":
      return {
        result: {
          system: probe(() => typeof System !== "undefined" && System.IO.File.Exists("x")),
          import_namespace: probe(() => typeof importNamespace("System") !== "undefined"),
          clr: probe(() => typeof clr !== "undefined"),
        },
      };

    case "escape.eval":
      return { result: probe(() => eval("1+1") === 2) };

    case "escape.function":
      return { result: probe(() => new Function("return 1")() === 1) };

    case "escape.constructor":
      return { result: probe(() => typeof ({}).constructor.constructor("return this")()) };

    case "module.import":
      return { result: { helper: helper(input.value) } };

    default:
      throw host.fail({
        class: "validation",
        code: "unknown_operation",
        message: "unknown operation " + operation,
      });
  }
}
