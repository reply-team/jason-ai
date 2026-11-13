# Jason UI

The browser UI the Jason runtime serves at `http://127.0.0.1:<port>/ui/`. One more client of the Runtime API
(`POST /v1/<noun>.<verb>`, and the one `GET`, the journal stream `GET /v1/journal.stream`), paired once with
`jason ui open`, then bookmarked. Nothing in the runtime knows a screen exists.

## Build

Node 24. `npm ci`, then `npm run build` writes `dist/`, with the `THIRD-PARTY-NOTICES` of exactly the packages the
bundle carries. `npm run package` wraps it as `artifacts/jason-ui.zip`: `index.html`,
`manifest.json`, `favicon.svg` (the mark), `brand-wordmark.svg` and `brand-wordmark-dark.svg` (the wordmark for a light and a dark ground), `assets/*`, `THIRD-PARTY-NOTICES` at the root. The build refuses a package it carries
that has no licence file, and the package step refuses notices that leave out a package `package.json` names as a
dependency.

The brand files are the mark, the two wordmarks and the app icons under `desktop/`; the small `Alpha` label beside the wordmark
is text in the page, in the browser and in the desktop alike, and the first screen of the desktop says the interface is optional and that a terminal with your AI, through the CLI and API, does the same.

## Where it lands

The runtime serves `<data>/ui/custom/` when it holds a build (an `index.html` and a `manifest.json`), else
`<data>/ui/default/` when that holds a build: the release's own, which `jason ui install` places (a release builds
`ui/` at its tag and publishes `jason-ui.zip` beside its archives). To try a build against a real Jason: unzip
`jason-ui.zip` into `~/.jason/ui/custom/` and run `jason ui open`; `jason status` says which build is served. Delete `custom/` to go back.

## Two shells, one build

The words of the page: an **agent** is the harness's campaign with its chat, and only on the screen. Identifiers, routes,
API fields, file names, test ids and fixtures keep `campaign`. The AI that runs commands is "the AI", never "the agent".

The same build mounts one of two shells, chosen once as the page loads (`src/main.tsx`):

- **In a browser, the classic shell**: the sidebar and the screens under `src/screens/`, and a Chat screen that says
  the chat with the AI is part of the desktop application, where a page can start the AI on the computer and ask
  the person before it acts. A tab the desktop opened with a bridge gets the console instead, below.
- **Inside Jason Desktop, the console** (`src/console/`). The desktop puts `window.jasonDesktop` on the page before the
  page runs; with it there, the chat is home, in a dock (dockview) with panels beside it: what needs the person
  (approvals and questions), the agents, the journal's activity, and Connect, which shows the AI hosts found on
  the computer, the model keys, the Reply connection and the notification channels. The browser's other screens open in a page panel
  of the same dock. The console reaches the desktop only through the seam typed in `src/platform/desktop.d.ts`, and the
  runtime only through the API client and the journal stream (`src/platform/journal-stream.ts`, a WebSocket at
  `GET /v1/journal.stream` under the page's own pairing).

**Three places, one console.** The console is always handed an object of the desktop's whole shape, built in
`src/platform/places.ts`: the window's own (everything), a tab's over the bridge (the bridge's table, `src/platform/bridge.ts`;
every other member present, answering the refusal its own type has, and `platform` the browser's own), and, later, one for a
tab with no desktop. The page asks one question before it draws or does anything a place may not, `can(member)`, and draws
what the place cannot do off, in its place, with one sentence. `src/start.ts` chooses: in the window the console starts at
once; in a browser the pairing is read first. `tests/guards/desktop-calls.test.ts` reads the page's source and holds every
use of a member to the bridge's table or to that question, and the members' list is `desktop.d.ts` itself.

The console's live panels read the same facts as the classic Attention and Agents screens, with the same
operations, and for now duplicate them: the classic screens stay for the browser. Each panel keeps its lists apart
from the screen's, so the page panel can show that screen at a table or a filter of its own while a journal line
re-reads the panel.

**How the console draws.** A panel is markup from one tagged template, `html` in `src/console/html.ts`, which escapes
every value it interpolates unless the tag itself made it, and is written into the page by one painter, `paint`. A
control in that markup carries `data-action` and its arguments, answered by one closed table of actions
(`src/console/actions.ts`) through one delegated listener; the chat's composer is plain JSX. Nothing it draws carries
an inline handler, a style attribute or a style element, which the runtime's policy (`default-src 'self'`) refuses; and
the tag refuses text inside a tag, where it could add an attribute of its own. A repaint keeps the control a person is
in focused, with its selection. The
classic screens in the page panel take the browser's stylesheet, scoped under `.harness` when the build runs
(`src/console/scope.ts`), so the two stylesheets do not meet.

**Words and theme.** The browser shell's words are in `src/locale/en.ts`, one table read through `t()`. The theme
(light, dark, or the system's) is chosen under Settings; light is what an installation that has chosen nothing meets.
The choice is kept in this browser and never sent to the runtime; inside the desktop it is also told to the desktop, which
keeps it so its window and starting page open in the same theme before the page loads. The document's `lang` is `en`. The console's own words are English in this release: the table serves the browser shell's.

## Dev loop

A scratch runtime (`JASON_DATA_DIR` set to a scratch directory, never `~/.jason`), `dist/` linked as that directory's
`ui/custom/` (a symbolic link on Linux and macOS, a junction on Windows), `npm run dev` rebuilding `dist/` on every
change, and `jason ui open` once. The build writes the `manifest.json` the runtime asks of a directory before it serves
it, so `dist/` is served as it stands. No hot reload, and no dev-only origin allowance in the runtime.

## Screens and their pieces

A screen lives in `src/screens/<name>/`: its view, its store, which makes every read and write and drops an answer the
screen has moved past, and its tests. What every screen draws the same way is written once and used by all:
`src/layout/` (toolbar, switches, filters bar, panel header, actions row, tabs, dialog, chips, notice), `src/pickers/`
(the campaign and person pickers), `src/lists/` and `src/time/`. A screen gives them words, nodes and callbacks, never
its data.

## React

The UI is written in React 18, with `@preact/signals-react` for its state, because React 18 is the framework the UI
and what is built on it are written against. The move from Preact changed the framework, the toolchain that compiles
it, the guards that hold the layers, and how the notices are written: the build writes them from its bundle, where the
package step once did. It changed three screen tests, where React's renderer runs an effect or an update before the
test ends; the other screen tests changed only the testing library they import. What the screens do, their stores,
the API client and the recorded exchanges the screen tests run against are what they were. Signals still replace most
effects (`signal`, `computed`, `effect`); a build-time transform in `vite.config.ts` makes each component re-render
when a signal it read changes, which Preact did on its own.

## Testing

Three layers, each proving one kind of thing, and nothing proven twice. The static gate (`npm run check`: `tsc`,
ESLint, Prettier) runs first and is not a layer.

| Layer         | Lives in                                                                | Runs as                                               | Belongs here                                                                                                                                                                                                  | Does not belong                                                                                                                                                 |
| ------------- | ----------------------------------------------------------------------- | ----------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Unit          | `src/**/*.test.ts`, beside the module                                   | `vitest --project unit`: Node, no DOM                 | dense, isolated logic: time, grouping, the paging merge, the route grammar                                                                                                                                    | mappings, wiring, anything whose correctness needs a screen or the runtime                                                                                      |
| Screen        | `src/screens/<name>/*.screen.test.tsx`, `src/console/*.screen.test.tsx` | `vitest --project screen`: happy-dom, testing-library | one test per screen or panel behaviour, against a fake client fed only from `docs/api/fixtures/`; the console's with a stand-in desktop and journal stream (`src/test/desktop.ts`)                            | hand-written response JSON, HTTP mocking libraries, the real client                                                                                             |
| Browser smoke | `tests/e2e/*.spec.ts`                                                   | `playwright test`: Chromium, a real runtime           | what only a browser proves: pairing, bookmark after restart, revoke, custom over default, 404, CSP, the request guard, and the console under the policy with a stand-in desktop, following the journal stream | business flows, data beyond the one campaign the console's row creates through the command line for a journal line to stream, the fake agent host, any provider |

**Why.** A unit test of a mapping proves nothing until a screen reacts, so mappings are tested where the reaction is
visible. A screen test against a hand-written fixture tests a belief about the runtime; the fixtures here were
recorded by the runtime's own tests, so a screen can only render what the runtime proved. A browser test of a business
flow would test the runtime twice, through the CLI and through the page; the smoke tests the seams a browser alone
can prove.

**A second input of the screen layer.** The two published texts under `docs/prompts/` (`approval.txt`, `question.txt`)
are what the Copy prompt button must put on the clipboard; the screen tests read them as raw text (`?raw`), and
`vite.config.ts` widens the allow list of the server to that one folder, `../docs/prompts`, and nothing more. A test of the command line
writes the files, so the button and `jason approval get --prompt` cannot drift.

**Recording a fixture.** A screen needs an exchange the runtime has not recorded: add the case to `ApiFixtures.Cases`
in the runtime's test suite, write (or find) the runtime test that produces it and make it
post through `RecordAsync("<operation>", "<case>", body)`, then run that test with `JASON_UPDATE_FIXTURES=1` and commit
the file under `docs/api/fixtures/<operation>/<case>.json`. Import it in `src/test/fixtures.ts` with `satisfies`, which
holds this project's types to it. A fixture that goes stale fails `dotnet test`; a type that disagrees with a fixture
fails `npm run check`.

**What holds the rules.** The unit project runs in Node (a DOM use fails) and its tests may not import React or a
test renderer; no `.json` lives under `ui/` but the three configuration files; `package.json`'s dependencies are an
allowlist a test holds; the fixture catalog is one-to-one with the files on disk; the fake client answers a request
only with a recording whose request equals the one sent, and a request a test recorded no answer for fails the test,
even when the screen caught the error; the console's source makes markup only with its tag, writes it into the page
only through its painter, and carries no inline handler or style attribute in markup or JSX
(`tests/guards/console-templates.test.ts`, which reads the source as TypeScript); the desktop seam's members are
named once more in `tests/guards/desktop-seam.test.ts`, so a member added or dropped is a red build, and every use of one is held to the bridge's table or to the one question (`tests/guards/desktop-calls.test.ts`); only
`tests/e2e/harness.ts` may start a process, and it gives each runtime a data directory and a harness root of its own;
the CI job runs all of it on every push.

**Running.** `npm test` (unit and screen), `npm run e2e` (needs `npm run build`, a runtime built from its source in Release, or `JASON_EXE` naming an installed `jason`, and `npx playwright install chromium` once).
