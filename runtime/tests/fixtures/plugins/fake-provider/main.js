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

// What each of the provider's refusals means for a retry. The classes are the plugin's own judgement, which is
// exactly the judgement a contract's failure-code table records: a caller must never have to guess.
const FAILURE_CLASSES = {
  campaign_not_found: "permanent",
  campaign_not_enrollable: "permanent",
  collision_refused: "permanent",
  contact_not_found: "permanent",
  invalid_channel_value: "validation",
  limit_reached: "permanent",
  list_not_found: "permanent",
  rate_limited: "transient",
  suppressed: "permanent",
  unauthorized: "permanent",
};

// The provider's own words for a campaign's state, mapped onto the vocabulary the contract publishes. Anything
// this table has no word for is `other`, and the provider's own word survives under `vendor`.
const CAMPAIGN_STATUS = {
  Draft: "draft",
  Active: "live",
  Paused: "paused",
  Archived: "archived",
};

// One call to the vendor CLI: the request goes in on stdin and exactly one answer comes back on stdout.
function cli(context, subcommand, request) {
  const program = host.env("FAKE_CLI_DLL");
  const workspace = context.binding && context.binding.workspace;
  if (!program || !workspace) {
    throw host.fail({
      class: "permanent",
      code: "unauthorized",
      message: "This plugin needs a binding naming the account it should act in, and the vendor program to act with.",
    });
  }

  const answer = host.exec({
    executable: "dotnet",
    args: [program, "--workspace", workspace, ...subcommand],
    stdin: JSON.stringify(request),
  });

  if (answer.exit_code !== 0) {
    // The program was called and did not answer. Whether it acted is not knowable from here, which is what
    // `ambiguous` means; the contract's recovery read is how the next attempt finds out.
    throw host.fail({
      class: "ambiguous",
      code: "provider_answer_lost",
      message: "The provider was called and no answer came back.",
      details: { subcommand: subcommand.join(" "), exit_code: answer.exit_code },
    });
  }

  const parsed = JSON.parse(answer.stdout);
  if (parsed.error) {
    throw host.fail({
      class: FAILURE_CLASSES[parsed.error.code] || "permanent",
      code: parsed.error.code,
      message: parsed.error.message,
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
function channelOf(contact, name) {
  for (const channel of contact.channels) {
    if (channel.channel === name) {
      return channel;
    }
  }

  throw host.fail({
    class: "validation",
    code: "invalid_channel_value",
    message: "The contact carries no " + name + " channel.",
  });
}

// The precondition every write shares: the provider's own contact must exist, worked by the pin when there is
// one and created from the channel value only when there is not.
function ensureContact(context, input, channel) {
  const contact = input.contacts[0];
  const pinned = contact.external_ids && contact.external_ids.contact;
  return cli(context, ["contact", "ensure"], compact({
    channel: channel.channel,
    value: channel.value,
    external_id: pinned,
  }));
}

// The recovery read the contract obliges: on any attempt after the first, the ledger under this key is read
// before anything is written, so a lost answer costs a round trip rather than a second effect.
function recover(context, key) {
  if (!context.attempt_number || context.attempt_number < 2) {
    return null;
  }

  const recorded = cli(context, ["ledger", "get"], { key: key });
  return recorded.found ? recorded.entry : null;
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
  const answer = cli(context, ["campaign", "get"], { external_id: campaignOf(input) });

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

  const already = recover(context, key);
  if (already) {
    return membershipResult(contact.id, "already_member", already.contact_id);
  }

  const channel = channelOf(contact, input.args.channel);
  const ensured = ensureContact(context, input, channel);
  const added = cli(context, ["list", "add"], {
    list_id: input.args.list.external_id,
    contact_id: ensured.id,
    key: key,
  });

  return membershipResult(contact.id, added.status, ensured.id);
}

function campaignEnroll(input, context) {
  const contact = input.contacts[0];
  const key = input.idempotency_key;
  const campaign = campaignOf(input);

  const already = recover(context, key);
  if (already) {
    return enrollmentResult(contact.id, "already_enrolled", already.contact_id, campaign, already.live);
  }

  const channel = channelOf(contact, input.args.channel);
  const ensured = ensureContact(context, input, channel);
  const enrolled = cli(context, ["campaign", "enroll"], {
    campaign_id: campaign,
    contact_id: ensured.id,
    key: key,
    collision: input.args.collision,
    start: input.args.start.position === "step" ? "step:" + input.args.start.step : "first_step",
    first_touch: input.args.first_touch,
  });

  return enrollmentResult(contact.id, enrolled.status, ensured.id, campaign, enrolled.live);
}

export function invoke(operation, input, context) {
  if (operation.startsWith("fail.")) {
    throw host.fail(compact({
      class: operation.slice(5),
      code: input.code ?? "provoked",
      message: input.message ?? "provoked failure",
      details: input.details,
      external_ids: input.external_ids,
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
