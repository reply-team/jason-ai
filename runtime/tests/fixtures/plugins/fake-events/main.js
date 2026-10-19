// A provider plugin that reads its accounts' events, in the two ways a provider allows. With a feed by position, the
// contract's cursor is the provider's own position. With no feed, only a list newest first by offset, the plugin
// carries its own cursor: where the walk is, the moment it reads back to, and the newest moment it has seen - and a
// read pages newest first until the last item's moment passes where it reads back to.
const PROVIDER_CLI = "Jason.FakeProviderCli";

// The provider's own words for a campaign's state, in the contract's vocabulary.
const CAMPAIGN_STATUS = { Draft: "draft", Active: "live", Paused: "paused", Archived: "archived" };

function cli(context, subcommand, request) {
  const workspace = context.binding && context.binding.workspace;
  if (!workspace) {
    throw host.fail({
      class: "permanent",
      code: "events_unauthorized",
      message: "This plugin needs a binding naming the account it should read.",
    });
  }

  const answer = host.exec({
    executable: PROVIDER_CLI,
    args: ["--workspace", workspace, ...subcommand],
    stdin: JSON.stringify(request),
  });
  if (answer.exit_code !== 0) {
    throw host.fail({ class: "transient", code: "events_unreachable", message: "The provider did not answer." });
  }

  const parsed = JSON.parse(answer.stdout);
  if (parsed.error) {
    throw host.fail({ class: "permanent", code: parsed.error.code, message: parsed.error.message });
  }

  return parsed;
}

function campaignGet(input, context) {
  const answer = cli(context, ["campaign", "get"], { external_id: input.args.campaign.external_id });
  return {
    result: {
      campaign: { external_id: answer.id, name: answer.name, status: CAMPAIGN_STATUS[answer.status] || "other", counts: answer.counts },
      vendor: answer.vendor,
    },
    external_ids: { campaign: answer.id },
  };
}

// A provider with a feed: the cursor is its position, handed back exactly as it came.
function fromFeed(input, context) {
  return {
    result: cli(context, ["events", "feed"], { after: input.cursor, since: input.since, until: input.until, limit: input.limit }),
  };
}

// A provider with only a list newest first by offset. The cursor is "o|<offset>|<reads back to>|<newest seen>".
function byOffset(input, context) {
  let skip = 0;
  let low = input.since;
  let high = input.since;
  if (input.cursor) {
    const parts = input.cursor.split("|");
    if (parts.length !== 4 || parts[0] !== "o") {
      throw host.fail({ class: "permanent", code: "events_cursor_unknown", message: "This plugin did not write that cursor." });
    }

    skip = Number(parts[1]);
    low = parts[2];
    high = parts[3];
  }

  const page = cli(context, ["events", "list"], { top: input.limit, skip: skip });
  const lowAt = Date.parse(low);
  let newestAt = Date.parse(high);
  let newest = high;
  let passed = false;
  const kept = [];
  for (const event of page.items) {
    const at = Date.parse(event.occurred_at);
    if (at > newestAt) {
      newestAt = at;
      newest = event.occurred_at;
    }

    if (at < lowAt) {
      passed = true;
      break;
    }

    if (!input.until || at < Date.parse(input.until)) {
      kept.push(event);
    }
  }

  if (page.items.length === input.limit && !passed) {
    return { result: { events: kept, cursor: ["o", skip + input.limit, low, newest].join("|"), has_more: true } };
  }

  return { result: { events: kept, cursor: ["o", 0, newest, newest].join("|"), has_more: false } };
}

export function invoke(operation, input, context) {
  switch (operation) {
    case "campaign.get":
      return campaignGet(input, context);

    case "events.read":
      return context.binding && context.binding.paging === "offset" ? byOffset(input, context) : fromFeed(input, context);

    default:
      throw host.fail({ class: "permanent", code: "operation_unsupported", message: "This plugin does not perform " + operation + "." });
  }
}
