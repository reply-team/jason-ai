# The demo page

`index.html` is the page the desktop shows in Demo: a mock of Jason at work on sample data, supplied as one static file,
with its script, its styles and its fonts inline. It has nothing to do with the runtime: it reads no installation, asks
no runtime anything and sends nothing, and the desktop shows it in a sealed view of its own, with our Demo | Live switch
drawn over the place where the page draws one (`../src/demo-page.ts`, `../src/demo-window.ts`, and
[the desktop's page](../../docs/desktop.md#the-demo)).

## A new drop

A new version of the page replaces `index.html` whole. Nothing in it is edited by hand, with one exception: before it is
committed, the repository's publication patterns are run over it (the patterns `RepositoryHygieneTests` and the
`hygiene.yml` workflow hold every file to, and a reading for real people's names and handles, for real provider
identifiers, and for links into private systems), and what they refuse is replaced by the smallest substitution that
lets it pass. Every substitution is listed below; a new drop lists its own, replacing this list.

The page as it is committed differs from the page as it was supplied by ten substitutions, each made wherever its text
occurs: once each, but the last, which occurs twice. What was replaced is described here, not quoted, so this list does
not publish it again:

| Supplied | Committed | Why |
| --- | --- | --- |
| A real person's Telegram handle | `@your_handle` | No real person's account is published. |
| The first of six campaign numbers in the provider's own form, `Reply #` and five digits | `Reply #demo-1` | A provider identifier that may be real is not published. |
| The second | `Reply #demo-2` | The same. |
| The third | `Reply #demo-3` | The same. |
| The fourth | `Reply #demo-4` | The same. |
| The fifth | `Reply #demo-5` | The same. |
| The sixth | `Reply #demo-6` | The same. |
| A link to the connector settings of an AI vendor's chat application | `https://example.com/settings/connectors` | The patterns refuse links into that vendor's application by its host. |
| A LinkedIn profile path built from a common first and last name, the first of two | the same path with `-example` at its end | A path of a common name may well be a real person's account; the suffix makes it plainly invented. |
| The second, which occurs twice | the same path with `-example` at its end | The same. |

What the patterns met and left alone, being made up or public: the handles `@gtmtools`, `@dataops`, `@socialsell` and
`@northwind_sales_bot`, addresses under `example.com`, the public names and hosts of the provider the page shows, and one campaign id in Jason's own form.

## What is packaged and what the page carries

Only `index.html` goes into the application (`../electron-builder.config.mjs`, `files`); this README and the licence
text stay in the repository. The page carries the Inter font (`@fontsource-variable/inter` 5.3.0, SIL Open Font
License 1.1), whose licence text is `LICENSE-Inter.txt`; `../scripts/notices.mjs` names it in the desktop's
`THIRD-PARTY-NOTICES`. A drop that carries anything more adds it to the list in that script and its licence text here.
