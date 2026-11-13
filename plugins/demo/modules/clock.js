// The story's clock. A plugin run keeps nothing between runs, so the replies are written on a schedule: each one at its
// offset after the moment the person's installation names under Plugins:Config:demo:story_start. Without that moment the
// story has not started, and no thread or reply exists yet.

const MOMENT = /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$/;

/** The story's start in milliseconds, or null where none is configured or the text is no moment. */
export function storyStart() {
  const text = host.config("story_start");
  if (text === null || !MOMENT.test(text)) {
    return null;
  }

  const ms = Date.parse(text);
  return isNaN(ms) ? null : ms;
}

export function now() {
  return Date.now();
}

export function iso(ms) {
  return new Date(ms).toISOString();
}

/** When a written reply lands: its offset after the story's start. */
export function replyAt(start, reply) {
  return start + reply.offset_seconds * 1000;
}
