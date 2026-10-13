// `engagement.summarize`: Reply's own counts for one sequence over one window, a row per channel it reports.
//
// One command, `reply sequence stats`, with the window as its flags. Reply answers an overview per channel; each
// becomes a row with the envelope that says what its numbers count. Only counts are mapped, and only the ones the
// document names — a rate is two counts and a denominator, and the denominator is exactly what Reply does not say,
// so its percentages are never copied.
import { run } from "./cli.js";
import { sequenceOf } from "./reading.js";

const OPERATION = "engagement.summarize";
const COMMAND = "sequence stats";

// The presets Jason names, and the CLI's word for each.
const PRESET = {
  "last_week": "last-week",
  "last_month": "last-month",
  "last_year": "last-year",
  "all_time": "all-time",
};

// Each overview Reply answers, the channel its row is about, and which of its counts the document names. A count
// Reply reports beyond these is not returned in this version.
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

export function engagementSummarize(input, context) {
  const sequence = sequenceOf(input, OPERATION);
  const learned = { campaign: sequence };
  const window = input.args.window;

  // The plugin's clock when it asked: Reply does not say when it counted, so this is the closest honest moment.
  const asOf = new Date().toISOString();
  const overviews = run(context, { operation: OPERATION, command: COMMAND, operands: [sequence], flags: windowFlags(window), learned: learned });

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

// The window as the CLI's flags: a preset, or a span. The input schema has already refused both and neither.
function windowFlags(window) {
  return typeof window.preset === "string"
    ? ["--preset", PRESET[window.preset]]
    : ["--from", window.from, "--to", window.to];
}

// The counts the document names, each only where Reply sent a whole number the schema can hold. Anything else is
// left out rather than coerced: absent says "the provider does not say", and a 0 would say something it did not.
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
