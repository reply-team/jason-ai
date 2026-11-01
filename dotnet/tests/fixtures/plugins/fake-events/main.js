// A provider plugin that reads its accounts' events, in the two ways a provider allows. With a feed by position, the
// contract's cursor is the provider's own position. With no feed, only a list newest first by offset, the plugin
// carries its own cursor: where the walk is, the moment it reads back to, and the newest moment it has seen - and a
// read pages newest first until the last item's moment passes where it reads back to.
const PROVIDER_CLI = "Jason.FakeProviderCli";

// The provider's own words for a campaign's state, in the contract's vocabulary.
const CAMPAIGN_STATUS = { Draft: "draft", Active: "live", Paused: "paused", Archived: "archived" };

// Each contract names its own failures: an events read speaks the events contract's codes, campaign.get its operation's.
const EVENTS_CODES = { "unauthorized": "events_unauthorized", "unreachable": "events_unreachable" };
const CAMPAIGN_GET_CODES = { "unauthorized": "unauthorized", "unreachable": "provider_unavailable" };

function cli(context, subcommand, request, codes) {
  const workspace = context.binding && context.binding.workspace;
  if (!workspace) {
    throw host.fail({
      class: "permanent",
      code: codes.unauthorized,
      message: "This plugin needs a binding naming the account it should read.",
    });
  }

  const answer = host.exec({
    executable: PROVIDER_CLI,
    args: ["--workspace", workspace, ...subcommand],
    stdin: JSON.stringify(request),
  });
  if (answer.exit_code !== 0) {
    throw host.fail({ class: "transient", code: codes.unreachable, message: "The provider did not answer." });
  }

  // The provider's own refusals are already in the contracts' words: campaign_not_found, events_cursor_unknown.
  const parsed = JSON.parse(answer.stdout);
  if (parsed.error) {
    throw host.fail({ class: "permanent", code: parsed.error.code, message: parsed.error.message });
  }

  return parsed;
}

function campaignGet(input, context) {
  const answer = cli(context, ["campaign", "get"], { external_id: input.args.campaign.external_id }, CAMPAIGN_GET_CODES);
  return {
    result: {
      campaign: { external_id: answer.id, name: answer.name, status: CAMPAIGN_STATUS[answer.status] || "other", counts: answer.counts },
      vendor: answer.vendor,
    },
    external_ids: { campaign: answer.id },
  };
}

// A provider with a feed: the cursor is its position, handed back exactly as it came. A read that ends at a moment keeps
// that moment in its own cursor, "r|<until>|<position>", so that the pages its cursors chain stay before it.
function fromFeed(input, context) {
  let after = input.cursor;
  let until = input.until;
  if (after && after.startsWith("r|")) {
    const bar = after.indexOf("|", 2);
    until = after.slice(2, bar);
    after = after.slice(bar + 1);
  }

  const page = cli(context, ["events", "feed"], { after: after, since: input.since, until: until, limit: input.limit }, EVENTS_CODES);
  if (until) {
    page.cursor = "r|" + until + "|" + page.cursor;
  }

  return { result: page };
}

// A provider with only a list newest first by offset. The cursor is "o|<offset>|<reads back to>|<newest seen>", and
// "|<until>" after it for a read that ends at a moment.
function byOffset(input, context) {
  let skip = 0;
  let low = input.since;
  let high = input.since;
  let until = input.until;
  if (input.cursor) {
    const parts = input.cursor.split("|");
    if ((parts.length !== 4 && parts.length !== 5) || parts[0] !== "o") {
      throw host.fail({ class: "permanent", code: "events_cursor_unknown", message: "This plugin did not write that cursor." });
    }

    skip = Number(parts[1]);
    low = parts[2];
    high = parts[3];
    until = parts[4];
  }

  const page = cli(context, ["events", "list"], { top: input.limit, skip: skip }, EVENTS_CODES);
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

    if (!until || at < Date.parse(until)) {
      kept.push(event);
    }
  }

  const bounded = until ? [until] : [];
  if (page.items.length === input.limit && !passed) {
    return { result: { events: kept, cursor: ["o", skip + input.limit, low, newest, ...bounded].join("|"), has_more: true } };
  }

  return { result: { events: kept, cursor: ["o", 0, newest, newest, ...bounded].join("|"), has_more: false } };
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
