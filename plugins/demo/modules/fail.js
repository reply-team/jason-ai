// Every failure this package reports, in a code the operation's own document declares, under the class it gives it.

export function fail(cls, code, message, extra) {
  const failure = { class: cls, code, message };
  if (extra && extra.details) {
    failure.details = extra.details;
  }

  if (extra && extra.external_ids) {
    failure.external_ids = extra.external_ids;
  }

  return host.fail(failure);
}
