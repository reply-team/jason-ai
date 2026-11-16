# The demo page

`index.html` is the page the desktop shows in Demo: the console's Demo built for embedding, a mock of Jason at work on
sample data, as one static file with its script, its styles and its font inline. It is a built page, received as it was
built and committed as it is: nothing in it is edited by hand, and a new build replaces it whole. The file committed
here has SHA-256 `9ed69bba8026d3d3a6a23bdf5b88428d9a91e51cb9b9512f687e580e8b0066c4`, which `../test/demo-page.test.ts`
holds it to.

It has nothing to do with the runtime: it reads no installation, asks no runtime anything and sends nothing. It was
built to be shown in a frame sandboxed to scripts alone, with no same origin: storage, the clipboard and downloads are
optional to it, it opens no window and never navigates the window above it, and it keeps its own pages in memory. The
desktop shows it in a sealed view of its own held to that same form by the desktop's own refusals, not by the page's
promise, with our Demo | Live switch drawn over the right end of its top strip, which the page leaves free for one
(`../src/demo-page.ts`, `../src/demo-window.ts`, and [the desktop's page](../../docs/desktop.md#the-demo)).

## The addresses it holds

The page holds a few texts in the shape of an address: sample values shown as text, placeholders of fields, a
namespace. Each exact string, how many times it stands in the page and why it is inert there, is listed in
[`addresses.txt`](addresses.txt), and the tests hold the page to that list exactly: an address not listed, or one more or
one fewer of a listed one, fails. A new build of the page that brings or loses one updates the list and says why. What
makes them inert is where the page is shown: every request from its view to any address is refused, which
`../test/demo-page.test.ts` holds.

## What is packaged and what the page carries

Only `index.html` goes into the application (`../electron-builder.config.mjs`, `files`); this README, the list and the
licence text stay in the repository. The page carries the Inter font (`@fontsource-variable/inter` 5.3.0, SIL Open Font
License 1.1), whose licence text, as the page's build recorded it, is `LICENSE-Inter.txt`; `../scripts/notices.mjs` names
it in the desktop's `THIRD-PARTY-NOTICES`. A build that carries anything more adds it to the list in that script and its
licence text here.
