# Plugins

Plugins are JavaScript packages that execute Jason's canonical provider operations against a real
vendor tool — and, later, deliver notifications — inside a short-lived plugin-host process that the
runtime starts from its own executable. Official and community plugins use the same mechanism.

**A plugin here is reached because a route sends work to it.** A `provider_op` work item is routed at
the claim to the plugin an operator named for that campaign and operation, and to the account their
binding names; without a route it fails at the claim with `no_route`, and nothing falls back to
whichever other plugin happens to implement the operation. [docs/routing.md](../docs/routing.md) is
that side of it; [docs/plugins.md](../docs/plugins.md) §13 lists what is still missing.

This directory is the plugin marketplace of the repository. Each plugin lives in its own folder:

```text
<plugin-id>/
├── plugin.yaml   # identity, version, supported contract versions, operations, requested capabilities
├── main.js       # invoke(operation, input, context)
└── modules/      # optional local JavaScript modules bundled with the plugin
```

The index is empty: the official Reply plugin and a reference notification plugin will land here.

**Writing one?** [docs/plugins.md](../docs/plugins.md) is the author's guide — package layout, every
manifest rule and its problem code, the Host SDK with every limit, capabilities and grants, the
invocation protocol, and how to test a package locally. The design behind it is in
[docs/architecture.md](../docs/architecture.md).

**What to implement** is §7 of that guide, "Implementing a canonical operation": the operations
themselves are published under [docs/contracts/](../docs/contracts/README.md), one document each,
and every one of them ends with the `conformance` list a conforming plugin has to demonstrate. The
four rules that hold whatever the operation — the recovery read on a repeated attempt, where a
failure's identifiers go, answering in the operation's own vocabulary, and working by a pin — are
under "What the runtime holds you to" in that same section.

`runtime/tests/fixtures/plugins/fake-provider/` is a complete, valid package that exercises every SDK
function. It is a **test fixture**, not a marketplace plugin — copy its shape, not its manifest.
