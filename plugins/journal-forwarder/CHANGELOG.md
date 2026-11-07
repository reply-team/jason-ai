# Changelog

This package versions independently of the Jason runtime, under SemVer. It declares every journal kind a plugin may
subscribe to, so a version that adds a kind to its manifest is a new version of the package.

## 1.2.0

A line can go to a file, beside the receiver or instead of it. The manifest asks for `capabilities.files`, so this asks for
more than 1.1.0 did, and names no place: a person grants the directories under `Plugins:Grants:journal-forwarder:Files`. The
plugin reads the key `file` under `Plugins:Config:journal-forwarder` first and `JOURNAL_FORWARDER_FILE` where it says
nothing; the line in the file is the POST's body, one JSON object per line. A file goes first, and a disk that refuses the
line still leaves the POST, after which the handler ends with the disk's failure. Nothing changes for a package that names
a URL and no file.

`forwarder_url_missing` is now `forwarder_receiver_missing`, since a URL is no longer the only receiver: neither a URL nor a
file is named.

Jason 0.32.0 or newer: an earlier runtime rejects the reload, as every package problem does, over the manifest's new word.

## 1.1.0

A receiver on another host no longer asks for an edit of the package. The manifest says `any_granted_host: true`, so a
host a person names in `Plugins:Grants:journal-forwarder:Http` is honoured, while `*` still means `localhost:8787` alone;
this asks for more than 1.0.1 did. The plugin reads the keys `url` and `token` under `Plugins:Config:journal-forwarder`
first, and `JOURNAL_FORWARDER_URL` and `JOURNAL_FORWARDER_TOKEN` where they say nothing. The failure codes are unchanged.
A copy edited under an earlier version stays the person's: `jason plugin install journal-forwarder --replace` puts the
release's package in its place, and the host and the URL then go in the settings as the README says.

Jason 0.31.0 or newer: an earlier runtime rejects the reload, as every package problem does, over the manifest's new word.

## 1.0.1

The package is published with every release of Jason from 0.30.0 on, as `jason-plugin-journal-forwarder.zip`, and the
README installs it with `jason plugin install journal-forwarder` rather than a copy out of a checkout. A receiver on
another host is an edit of the installed copy's `plugin.yaml`, after which the copy is the person's own: the README says
that `jason update` leaves it and says it differs, that `jason status` reports it as behind once a later version is
published and names the `--replace` that discards the edit, that a later version is theirs to apply, and that
`--replace` discards the edit. The plugin's code and manifest are otherwise unchanged.

## 1.0.0

First release. Performs the subscription contract `subscription.handle` version 1: each journal line of a kind the
person granted is POSTed as JSON to the URL in `JOURNAL_FORWARDER_URL`, with a bearer token from
`JOURNAL_FORWARDER_TOKEN` where the receiver needs one, and the envelope it was handed is answered unchanged. What it
does not do, and why, is in [README.md](README.md).
