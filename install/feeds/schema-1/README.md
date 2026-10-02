# schema-1: a test fixture

A **test fixture**, not a feed: the layout of 0.1.0's release files with the values taken out. CI fills it over
the three archives it has just built — `win-x64`, `linux-x64` and `osx-arm64` — and installs from it, offline, to prove
the install scripts still read this layout.
Nothing here is downloadable, and nobody should point an installer at it.

`manifest.json` and `checksums.txt` are byte for byte what 0.1.0 published, with each value replaced by a
placeholder:

| Placeholder | Filled with |
|---|---|
| `{{version}}` | the version being installed |
| `{{published_at}}` | when it was assembled, ISO-8601 UTC |
| `{{sha256:<rid>}}` | the SHA-256 of that platform's archive, lowercase hexadecimal |
| `{{size:<rid>}}` | that archive's size in bytes |

`.github/scripts/Fill-FeedFixture.ps1` fills it. A test fills it with the values 0.1.0 published and compares the
result with 0.1.0's own two files, and a second test runs the script and compares what it writes with the same
fill done in the test.

The schema changes only with a change of layout. When it does, the layout it replaces stays here under its own
number, because an install script keeps reading every schema published under a tag it can be pointed at.
