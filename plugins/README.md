# Plugins

Plugins are JavaScript packages that execute Jason's canonical provider operations against a real
vendor tool — and, later, deliver notifications — inside a short-lived plugin-host process that the
runtime starts from its own executable. Official and community plugins use the same mechanism.

This directory is the plugin marketplace of the repository. Each plugin lives in its own folder:

```text
<plugin-id>/
├── plugin.yaml   # identity, version, supported contract versions, operations, requested capabilities
├── main.js       # invoke(operation, input, context)
└── modules/      # optional local JavaScript modules bundled with the plugin
```

The official Reply plugin and a reference notification plugin will land here. The plugin model is
described in [docs/architecture.md](../docs/architecture.md).
