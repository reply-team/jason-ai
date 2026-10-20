# Jason UI

The browser UI the Jason runtime serves at `http://127.0.0.1:<port>/ui/`. One more client of the Runtime API
(`POST /v1/<noun>.<verb>`), paired once with `jason ui open`, then bookmarked. Nothing in the runtime knows a screen exists.

## Build

Node 24. `npm ci`, then `npm run build` writes `dist/`. `npm run package` wraps it as `artifacts/jason-ui.zip`: `index.html`,
`manifest.json`, `favicon.svg`, `assets/*`, `THIRD-PARTY-NOTICES` at the root.

## Where it lands

The runtime serves `<data>/ui/custom/` when it holds a build (an `index.html` and a `manifest.json`), else
`<data>/ui/default/` when that holds a build: the release's own, which `jason ui install` places (a release builds
`ui/` at its tag and publishes `jason-ui.zip` beside its archives). To try a build against a real Jason: unzip
`jason-ui.zip` into `~/.jason/ui/custom/` and run `jason ui open`; `jason status` says which build is served. Delete `custom/` to go back.

## Dev loop

A scratch runtime (`JASON_DATA_DIR` set to a scratch directory, never `~/.jason`), `dist/` linked as that directory's
`ui/custom/` (a symbolic link on Linux and macOS, a junction on Windows), `npm run dev` rebuilding `dist/` on every
change, and `jason ui open` once. The build writes the `manifest.json` the runtime asks of a directory before it serves
it, so `dist/` is served as it stands. No hot reload, and no dev-only origin allowance in the runtime.

## Preact, not React

The model is React's; the differences an agent should know: `class` not `className`; events are native DOM events;
`@preact/signals` replaces most effects (`signal`, `computed`, `effect`); import from `preact` and `preact/hooks`.

## Testing

Three layers, each proving one kind of thing, and nothing proven twice. The static gate (`npm run check`: `tsc`,
ESLint, Prettier) runs first and is not a layer.

| Layer         | Lives in                               | Runs as                                               | Belongs here                                                                                                          | Does not belong                                                            |
| ------------- | -------------------------------------- | ----------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| Unit          | `src/**/*.test.ts`, beside the module  | `vitest --project unit`: Node, no DOM                 | dense, isolated logic: time, grouping, the paging merge, the route grammar                                            | mappings, wiring, anything whose correctness needs a screen or the runtime |
| Screen        | `src/screens/<name>/*.screen.test.tsx` | `vitest --project screen`: happy-dom, testing-library | one test per screen behaviour, against a fake client fed only from `docs/api/fixtures/`                               | hand-written response JSON, HTTP mocking libraries, the real client        |
| Browser smoke | `tests/e2e/*.spec.ts`                  | `playwright test`: Chromium, a real runtime           | what only a browser proves: pairing, bookmark after restart, revoke, custom over default, 404, CSP, the request guard | business flows, seeded data, the fake agent host, any provider             |

**Why.** A unit test of a mapping proves nothing until a screen reacts, so mappings are tested where the reaction is
visible. A screen test against a hand-written fixture tests a belief about the runtime; the fixtures here were
recorded by the runtime's own tests, so a screen can only render what the runtime proved. A browser test of a business
flow would test the runtime twice, through the CLI and through the page; the smoke tests the seams a browser alone
can prove.

**Recording a fixture.** A screen needs an exchange the runtime has not recorded: add the case to `ApiFixtures.Cases`
in the runtime's test suite, write (or find) the runtime test that produces it and make it
post through `RecordAsync("<operation>", "<case>", body)`, then run that test with `JASON_UPDATE_FIXTURES=1` and commit
the file under `docs/api/fixtures/<operation>/<case>.json`. Import it in `src/test/fixtures.ts` with `satisfies`, which
holds this project's types to it. A fixture that goes stale fails `dotnet test`; a type that disagrees with a fixture
fails `npm run check`.

**What holds the rules.** The unit project runs in Node (a DOM use fails) and its tests may not import Preact or a
test renderer; no `.json` lives under `ui/` but the three configuration files; `package.json`'s dependencies are an
allowlist a test holds; the fixture catalog is one-to-one with the files on disk; only `tests/e2e/harness.ts` may
start a process; the CI job runs all of it on every push.

**Running.** `npm test` (unit and screen), `npm run e2e` (needs `npm run build`, a runtime built from its source in Release, or `JASON_EXE` naming an installed `jason`, and `npx playwright install chromium` once).
