// `schedule.list`: the sending schedules of the Reply account, each with its time zone and its windows by weekday.
//
// `reply schedule list` gives every schedule the team can use in one answer, with no paging and in no order Reply
// states, so this package orders them by Reply's identifier, the lowest first, and cuts the page itself: a page asked
// for again starts where it did. Reply holds a schedule's time zone as a Windows time zone identifier, and that is what
// is carried, under the `windows` scheme, never converted. The windows are Reply's as authored; nothing here works out
// on which dates a campaign sends.
//
// What Reply's answer does not say plainly is left out rather than guessed at: a schedule with no time zone, a weekday
// that is none of the seven or named twice, a range whose times are not times of day, and a day's ranges past the
// twenty-fourth, the most the document takes. A schedule without a usable identifier never reaches this package:
// reply-cli refuses the whole list for one (`reply.unavailable`), which is `provider_unavailable` and asked again, so
// the check for one below is a defence only.
import { run } from "./cli.js";
import { fail, lostAnswerRow } from "./errors.js";
import { fill } from "./page.js";
import { cut, text } from "./reading.js";

const OPERATION = "schedule.list";
const LIST = "schedule list";

const WEEKDAYS = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

// A time of day as the CLI writes one, and as the document takes it.
const CLOCK = /^([01][0-9]|2[0-3]):[0-5][0-9]$/;

// The end of the day, which Reply writes as 24:00: a window may end there, and none starts there.
const END_OF_DAY = "24:00";

// The document's bound on the ranges of one day.
const RANGES = 24;

export function scheduleList(input, context) {
  const args = input.args;
  const listed = run(context, { operation: OPERATION, command: LIST, operands: [], flags: [] });
  if (!Array.isArray(listed.items)) {
    fail(OPERATION, { call: LIST, row: lostAnswerRow(LIST) }, undefined);
  }

  const schedules = [];
  for (const entry of listed.items) {
    const schedule = scheduleOf(entry);
    if (schedule !== null) {
      schedules.push(schedule);
    }
  }

  schedules.sort((one, other) => one.id - other.id);
  const rest = { has_more: false, vendor: { ordering: "id_asc" } };
  const page = fill(schedules.slice(args.offset, args.offset + args.limit).map((schedule) => schedule.item), rest);
  return {
    result: {
      items: page.items,
      has_more: page.cut || schedules.length > args.offset + page.items.length,
      vendor: rest.vendor,
    },
  };
}

// One schedule as the document lists it, beside the number it is ordered by, or null for one Reply did not say enough
// about to list.
function scheduleOf(entry) {
  if (entry === null || typeof entry !== "object" || Array.isArray(entry)) {
    return null;
  }

  if (typeof entry.id !== "number" || !Number.isSafeInteger(entry.id) || entry.id <= 0) {
    return null;
  }

  const zone = entry.time_zone_id;
  if (typeof zone !== "string" || zone.trim().length === 0 || zone.length > 64) {
    return null;
  }

  return {
    id: entry.id,
    item: {
      external_id: String(entry.id),
      name: typeof entry.name === "string" && entry.name.trim().length > 0 ? cut(entry.name, 200) : null,
      time_zone: { id: zone, scheme: "windows" },
      in_recipient_time_zone: entry.use_recipient_time_zone === true,
      skips_holidays: entry.exclude_holidays === true,
      default: entry.is_default === true,
      windows: weekOf(entry.main_timings),
      follow_up_windows: entry.use_follow_up_schedule === true && Array.isArray(entry.follow_up_timings) ? weekOf(entry.follow_up_timings) : null,
      vendor: { status: text(entry.reply_status, 32) },
    },
  };
}

// The weekdays Reply holds, in Reply's order, each once.
function weekOf(timings) {
  const week = [];
  for (const day of Array.isArray(timings) ? timings : []) {
    if (day === null || typeof day !== "object" || WEEKDAYS.indexOf(day.week_day) < 0) {
      continue;
    }

    if (week.some((held) => held.weekday === day.week_day)) {
      continue;
    }

    const ranges = [];
    for (const range of Array.isArray(day.ranges) ? day.ranges : []) {
      if (ranges.length < RANGES && range !== null && typeof range === "object" && clock(range.from) && (clock(range.to) || range.to === END_OF_DAY)) {
        ranges.push({ from: range.from, to: range.to });
      }
    }

    week.push({ weekday: day.week_day, active: day.active === true, ranges: ranges });
  }

  return week;
}

function clock(value) {
  return typeof value === "string" && CLOCK.test(value);
}
