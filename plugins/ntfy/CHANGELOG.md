# Changelog

This package versions independently of the Jason runtime, under SemVer.

## 1.0.2

The README says what a pointer may carry by pointing at the delivery contract, `notification.send`, and says in
place which environment a runtime started at logon reads, rather than pointing at pages this repository does not
publish. What the package does is unchanged; the version moves because the package's content digest covers its
README.

## 1.0.1

The README and the manifest's description no longer restate what the runtime makes a pointer from. The README says
nothing else about the work travels and points at docs/notifications.md, which says exactly what a pointer may carry, now that a pointer may also say that
protective actions failed and name up to three campaigns by public id. What the package does is unchanged; the version
moves because the package's content digest covers its README and its manifest.

## 1.0.0

First release. Performs the delivery contract `notification.send` version 1: Jason's pointer, published as JSON to an
ntfy server's root, with a bearer token from `NTFY_TOKEN` where the server needs one. What it does not do, and why, is
in [README.md](README.md).
