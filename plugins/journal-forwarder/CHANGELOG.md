# Changelog

This package versions independently of the Jason runtime, under SemVer. It declares every journal kind a plugin may
subscribe to, so a version that adds a kind to its manifest is a new version of the package.

## 1.0.0

First release. Performs the subscription contract `subscription.handle` version 1: each journal line of a kind the
person granted is POSTed as JSON to the URL in `JOURNAL_FORWARDER_URL`, with a bearer token from
`JOURNAL_FORWARDER_TOKEN` where the receiver needs one, and the envelope it was handed is answered unchanged. What it
does not do, and why, is in [README.md](README.md).
