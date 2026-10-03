// `engagement.summarize`: Reply's own counts for one sequence over one window, a row per channel it reports.
//
// One read: `POST /v3/sequences/{id}/stats`, a POST that changes nothing, with the window on stdin. Reply answers an
// overview per channel; each becomes a row with the envelope that says what its numbers count. Only counts are
// mapped, and only the ones the document names — a rate is two counts and a denominator, and the denominator is
// exactly what Reply does not say, so its percentages are never copied.
import { call } from "./cli.js";
import { callOf, fail, lostAnswerRow, observe, statusRow } from "./errors.js";
import { sequenceOf } from "./reading.js";

const OPERATION = "engagement.summarize";

// The presets Jason names, and Reply's word for each.
const PRESET = {
  "last_week": "lastWeek",
  "last_month": "lastMonth",
  "last_year": "lastYear",
  "all_time": "allTime",
};

// Each overview Reply answers, the channel its row is about, and which of its counts the document names. A count
// Reply reports beyond these — not reached, connection notes, regular messages, likes, follows, endorsements,
// comments — is not returned in this version.
const OVERVIEWS = [
  {
    overview: "emailOverview",
    channel: "email",
    measures: {
      "contacted": "contacted",
      "delivered": "delivered",
      "opened": "opened",
      "replied": "replied",
      "interested": "interested",
      "bounced": "bounced",
      "optedOut": "opted_out",
      "outOfOffice": "out_of_office",
      "autoReplied": "auto_replied",
      "meetingsBooked": "meetings_booked",
    },
  },
  {
    overview: "linkedInOverview",
    channel: "linkedin",
    measures: {
      "connectionsSent": "connections_sent",
      "connectionsAccepted": "connections_accepted",
      "messagesSent": "messages_sent",
      "replied": "replied",
      "inMailsSent": "inmails_sent",
      "inMailsReplied": "inmails_replied",
      "profileViews": "profile_views",
    },
  },
];

// The refusal Reply gives an account whose role may not read a sequence's statistics.
const FORBIDDEN = "sequenceStats.forbidden";

export function engagementSummarize(input, context) {
  const sequence = sequenceOf(input, OPERATION);
  const learned = { campaign: sequence };
  const path = "/v3/sequences/" + sequence + "/stats";
  const known = callOf("POST", path);
  const window = input.args.window;

  // The plugin's clock when it asked: Reply does not say when it counted, so this is the closest honest moment.
  const asOf = new Date().toISOString();
  const answer = call(context, "POST", path, { filters: filters(window) }, learned, OPERATION);

  if (answer.code === 404) {
    fail(OPERATION, observe(known, answer, "campaign_not_found"), undefined);
  }

  if (answer.code !== 200) {
    const seen = observe(known, answer, statusRow(answer.code, known));
    if (answer.code === 403 && seen.provider_code === FORBIDDEN) {
      seen.row = "feature_not_included";
    }

    // Reply's validation problem carries `errors[]` and no code, and the window is the whole of what this call
    // asked: it is the window Reply refused, which no later attempt with the same window can change.
    const data = answer.data !== null && typeof answer.data === "object" ? answer.data : {};
    if (answer.code === 400 && Array.isArray(data.errors)) {
      seen.row = "window_refused";
    }

    fail(OPERATION, seen, learned);
  }

  const overviews = answer.data;
  if (overviews === null || typeof overviews !== "object" || Array.isArray(overviews)) {
    fail(OPERATION, observe(known, answer, lostAnswerRow(known)), learned);
  }

  const rows = [];
  for (const shape of OVERVIEWS) {
    const overview = overviews[shape.overview];
    if (overview === null || typeof overview !== "object" || Array.isArray(overview)) {
      // A channel Reply does not report has no row, which is not the same as a row of zeros.
      continue;
    }

    rows.push({
      group: { channel: shape.channel },
      envelope: {
        as_of: asOf,
        window: JSON.parse(JSON.stringify(window)),
        basis: "provider_reported",
        denominator: "unknown",
        detection: "provider",
      },
      measures: measures(overview, shape.measures),
      vendor: { overview: shape.overview },
    });
  }

  return { result: { rows: rows, vendor: {} }, external_ids: learned };
}

// The window as Reply's filter: a preset, or a span. The input schema has already refused both and neither.
function filters(window) {
  return typeof window.preset === "string" ? { dateRangePreset: PRESET[window.preset] } : { from: window.from, to: window.to };
}

// The counts the document names, each only where Reply sent a whole number the schema can hold. Anything else —
// a missing count, a fraction, a negative — is left out rather than coerced: absent says "the provider does not
// say", and a 0 or a rounding would say something it did not.
function measures(overview, named) {
  const mapped = {};
  for (const reply of Object.keys(named)) {
    const value = overview[reply];
    if (typeof value === "number" && isFinite(value) && Math.floor(value) === value && value >= 0 && value <= 2147483647) {
      mapped[named[reply]] = value;
    }
  }

  return mapped;
}
