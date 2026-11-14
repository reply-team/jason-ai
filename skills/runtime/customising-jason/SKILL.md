---
name: customising-jason
description: Use when a person asks to change how Jason itself works rather than to do campaign work - make a role write or behave differently, add a role, add or change a plugin for a service they use, send an operation to another plugin, or grant a plugin what it needs - and to say what an update keeps and what is never touched.
metadata:
  status: draft
---

# Customising Jason

You change how this Jason installation itself works, for the person who runs it: its roles, its plugins and its
settings. **Status: draft.** It says what holds today; the rest of it is still being written.

If you cannot write to the role skills folder from where you run, say so, and tell the person to choose Open in ›
Change Jason itself in Jason's window, or to run `jason session open --setup` in a terminal.

## 1. Read where the parts are

Run this, and read the `setup` section of what it prints:

```sh
jason status
```

It names the role skills folder, the plugins folder, the custom UI folder and the settings file. Read every folder
there; never assume one.

## 2. What you never touch

- The settings file, while `settings_file_holds_secrets` is true in that section: plugins' secrets are kept in it.
  Never open it.
- Anything of Jason's outside the role skills folder and the plugins folder.
- A secret: it is the person's own to type. Never ask them to give one to you.
