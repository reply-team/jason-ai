# Changelog

This package versions independently of the Jason runtime, under SemVer. It declares every journal kind a plugin may
subscribe to, so a version that adds a kind to its manifest is a new version of the package.

## 1.0.1

The package is published with every release of Jason from 0.30.0 on, as `jason-plugin-journal-forwarder.zip`, and the
README installs it with `jason plugin install journal-forwarder` rather than a copy out of a checkout. A receiver on
another host is an edit of the installed copy's `plugin.yaml`, after which the copy is the person's own: the README says
that `jason update` leaves it and says it differs, that a later version is theirs to apply, and that `--replace`
discards the edit. The plugin's code and manifest are otherwise unchanged.

## 1.0.0

First release. Performs the subscription contract `subscription.handle` version 1: each journal line of a kind the
person granted is POSTed as JSON to the URL in `JOURNAL_FORWARDER_URL`, with a bearer token from
`JOURNAL_FORWARDER_TOKEN` where the receiver needs one, and the envelope it was handed is answered unchanged. What it
does not do, and why, is in [README.md](README.md).
