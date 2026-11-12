import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, readdirSync, readFileSync, symlinkSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { loggedCli, startInstallation, StartFailed } from '../src/installation.ts';
import type { DesktopSettings } from '../src/settings.ts';
import { bench, memoryLog, type Bench } from './harness.ts';

const bundleVersion = '0.34.0-rc.1';

async function start(b: Bench, settings: DesktopSettings = { feed: null, skillsDeployedFor: null, theme: null }) {
    const log = memoryLog();
    const saved: DesktopSettings[] = [];
    const started = await startInstallation({
        cli: loggedCli(b.cli, log),
        paths: b.paths,
        log,
        bundle: join(b.root, 'bundle'),
        bundleVersion,
        settings,
        saveSettings: (next) => void saved.push(next),
        programs: b.programs,
    });
    return { started, log, saved, verbs: b.record().filter((entry) => entry.name === 'jason').map((entry) => entry.args) };
}

function runtimeAlreadyUp(b: Bench): void {
    mkdirSync(join(b.paths.data, 'run'), { recursive: true });
    writeFileSync(b.paths.runFile, JSON.stringify({ base_url: 'http://127.0.0.1:41234', token: 't' }));
}

test('with no runtime up, the desktop starts one through jason runtime start and loads the pairing link', async (t) => {
    const b = bench(t);
    const { started, verbs, saved } = await start(b, { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null, theme: null });
    const bundle = join(b.root, 'bundle');
    assert.deepEqual(verbs, [
        ['status'],
        ['runtime', 'start'],
        ['status'],
        ['runtime', 'status'],
        ['ui', 'install', '--feed', 'http://127.0.0.1:9/manifest.json'],
        ['skills', 'install', '--roles-only', '--source', bundle],
        ['skills', 'install', '--root', join(b.paths.chat, '.claude', 'skills'), '--host', 'claude_code', '--source', bundle],
        ['skills', 'install', '--root', join(b.paths.chat, '.agents', 'skills'), '--host', 'codex', '--source', bundle],
        ['ui', 'open', '--no-open'],
    ]);
    assert.equal(started.origin, 'http://127.0.0.1:41234');
    assert.equal(started.link, 'http://127.0.0.1:41234/ui/#pair=planted-pair-code');
    assert.equal(started.versionsDiffer, false);
    assert.ok(existsSync(b.paths.chat));
    // The CLI deploys into the directory --root names, so the skills sit where Claude Code started in the chat folder
    // with project settings reads them, and where Codex reads them from the folder it runs in, and nothing of them at the
    // folder's top level.
    assert.deepEqual(readdirSync(b.paths.chat).sort(), ['.agents', '.claude']);
    for (const host of ['.claude', '.agents']) {
        assert.deepEqual(readdirSync(join(b.paths.chat, host)), ['skills']);
        assert.deepEqual(readdirSync(join(b.paths.chat, host, 'skills')).sort(), ['campaign-launch'], host);
    }
    // Each deployment's record lives outside the chat folder, under the desktop's own folder.
    assert.deepEqual(readdirSync(b.paths.skillsRecords).sort(), ['agents.json', 'claude.json']);
    assert.deepEqual(saved, [{ feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: bundleVersion, theme: null }]);
});

test('a chat folder whose Codex skills could not be deployed is deployed again at the next start', async (t) => {
    const b = bench(t, { env: { STUB_SKILLS_FAIL_HOST: 'codex' } });
    const { saved } = await start(b, { feed: null, skillsDeployedFor: null, theme: null });
    assert.deepEqual(saved, []);
});

test('a runtime already up is used as it is: no start, and never runtime run', async (t) => {
    const b = bench(t);
    runtimeAlreadyUp(b);
    const { verbs, log } = await start(b);
    assert.equal(verbs.some((args) => args[0] === 'runtime' && args[1] !== 'status'), false);
    assert.ok(log.lines.some((line) => line.name === 'runtime_found'));
    assert.equal(log.lines.some((line) => line.name === 'runtime_starting'), false);
});

test('a runtime of another version keeps its own UI: no ui install, no role skills, and the versions are said to differ', async (t) => {
    const b = bench(t, { env: { STUB_SAME_VERSION: 'no', STUB_RUNTIME_VERSION: '0.33.0' } });
    runtimeAlreadyUp(b);
    const { started, verbs, log, saved } = await start(b);
    assert.equal(verbs.some((args) => args[0] === 'ui' && args[1] === 'install'), false);
    assert.equal(verbs.some((args) => args.includes('--roles-only')), false);
    assert.ok(verbs.some((args) => args.includes('--root')));
    assert.equal(started.versionsDiffer, true);
    assert.equal(started.runtimeVersion, '0.33.0');
    assert.deepEqual(log.lines.find((line) => line.name === 'versions_differ')?.fields, { RuntimeVersion: '0.33.0', BundleVersion: bundleVersion });
    assert.deepEqual(saved, [], 'skills deployed half-way are deployed again next time');
});

test('a runtime that already serves its own UI is not asked to install one: no fetch at every start', async (t) => {
    const b = bench(t, { env: { STUB_UI_MODULE: 'ok' } });
    runtimeAlreadyUp(b);
    const { verbs, started } = await start(b);
    assert.equal(verbs.some((args) => args[0] === 'ui' && args[1] === 'install'), false);
    assert.equal(started.link, 'http://127.0.0.1:41234/ui/#pair=planted-pair-code');
});

test('with no UI installed, the UI is installed, and an install that fails stops the start, since there is nothing to show', async (t) => {
    const b = bench(t, { env: { STUB_UI_MODULE: 'absent' } });
    runtimeAlreadyUp(b);
    assert.ok((await start(b)).verbs.some((args) => args[0] === 'ui' && args[1] === 'install'));

    const failing = bench(t, { env: { STUB_UI_MODULE: 'absent', STUB_UI_INSTALL_FAILS: 'yes' } });
    runtimeAlreadyUp(failing);
    await assert.rejects(start(failing), (error: unknown) => error instanceof StartFailed && error.step === 'ui install' && /could not be reached/.test(error.message));
});

test('a UI that is served but not this runtime’s is installed again, and an install that fails is logged and the start goes on', async (t) => {
    const b = bench(t, { env: { STUB_UI_MODULE: 'failed', STUB_UI_INSTALL_FAILS: 'yes' } });
    runtimeAlreadyUp(b);
    const { verbs, log, started } = await start(b);
    assert.ok(verbs.some((args) => args[0] === 'ui' && args[1] === 'install'));
    assert.deepEqual(log.lines.find((line) => line.name === 'ui_installed')?.fields, { Exit: 1 });
    assert.equal(started.link, 'http://127.0.0.1:41234/ui/#pair=planted-pair-code');
});

test('the skills are deployed once per bundle version', async (t) => {
    const b = bench(t);
    runtimeAlreadyUp(b);
    mkdirSync(b.paths.skillsRecords, { recursive: true });
    for (const name of ['claude.json', 'agents.json']) writeFileSync(join(b.paths.skillsRecords, name), '{"version":1,"packs":[]}');
    const { verbs } = await start(b, { feed: null, skillsDeployedFor: bundleVersion, theme: null });
    assert.equal(verbs.some((args) => args[0] === 'skills'), false);
});

test('a chat skills folder whose deployment the desktop keeps no record of is deployed afresh, and what stood there unrecorded is gone', async (t) => {
    const b = bench(t);
    runtimeAlreadyUp(b);
    mkdirSync(join(b.paths.chat, '.claude', 'skills', 'planted'), { recursive: true });
    writeFileSync(join(b.paths.chat, '.claude', 'skills', 'planted', 'SKILL.md'), '---\nallowed-tools: Bash\n---');
    writeFileSync(join(b.paths.chat, '.claude', 'skills', '.jason-skills.json'), '{"version":1,"packs":[]}');
    const { verbs } = await start(b, { feed: null, skillsDeployedFor: bundleVersion, theme: null });
    assert.ok(verbs.some((args) => args[0] === 'skills' && args.includes('claude_code')));
    assert.deepEqual(readdirSync(join(b.paths.chat, '.claude', 'skills')), ['campaign-launch']);
    assert.deepEqual(readdirSync(b.paths.skillsRecords).sort(), ['agents.json', 'claude.json']);
});

test('a start that fails, or a link that is not the runtime’s UI, stops with what the CLI said', async (t) => {
    const failing = bench(t, { env: { STUB_START_FAILS: 'yes' } });
    await assert.rejects(start(failing), (error: unknown) => error instanceof StartFailed && error.step === 'runtime start' && /did not start/.test(error.message));

    const foreign = bench(t, { env: { STUB_LINK: 'https://example.com/ui/#pair=x' } });
    await assert.rejects(start(foreign), (error: unknown) => error instanceof StartFailed && error.step === 'ui open');
});

test('the desktop’s environment reaches the CLI as it is: the same data directory and harness root a terminal has', async (t) => {
    const b = bench(t);
    await start(b);
    for (const entry of b.record()) {
        assert.equal(entry.env.JASON_DATA_DIR, b.paths.data);
        assert.equal(entry.env.JASON_Harness__Root, b.env.JASON_Harness__Root);
    }
});

test('every CLI run is logged by its verb, exit and time, never by its arguments', async (t) => {
    const b = bench(t);
    const { log } = await start(b, { feed: 'http://127.0.0.1:9/manifest.json', skillsDeployedFor: null, theme: null });
    const runs = log.lines.filter((line) => line.name === 'cli_exited');
    assert.equal(runs.length, 9);
    for (const run of runs) assert.deepEqual(Object.keys(run.fields).sort(), ['ElapsedMs', 'Exit', 'Verb']);
    assert.equal(JSON.stringify(log.lines).includes('manifest.json'), false);
});

// The Reply skills at the start, for a person already signed in to Reply, while the desktop keeps no record of them.

const replySkillCalls = (b: Bench) => b.record().filter((entry) => entry.name === 'reply' && entry.args.join(' ') === 'skills install adapter --project --agent codex');
const replyRecordFile = (b: Bench) => join(b.paths.skillsRecords, 'reply.json');

test('a first start with no record of the Reply skills and Reply signed in deploys them and records them with their digests', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'yes' } });
    runtimeAlreadyUp(b);
    await start(b);
    assert.equal(replySkillCalls(b).length, 1);
    const kept = JSON.parse(readFileSync(replyRecordFile(b), 'utf8')) as Record<string, { path: string; sha256: string }[]>;
    for (const folder of ['.claude', '.agents']) {
        const skills = readdirSync(join(b.paths.chat, folder, 'skills')).filter((name) => name.startsWith('reply-')).sort();
        assert.deepEqual(skills, ['reply-api', 'reply-cli'], folder);
        assert.deepEqual(kept[folder]?.map((each) => each.path).sort(), ['reply-api/SKILL.md', 'reply-cli/SKILL.md'], folder);
        assert.equal(kept[folder]?.find((each) => each.path === 'reply-cli/SKILL.md')?.sha256, createHash('sha256').update('# reply-cli\n').digest('hex'));
    }
    // With the record kept, a later start runs nothing of Reply's.
    await start(b, { feed: null, skillsDeployedFor: bundleVersion, theme: null });
    assert.equal(replySkillCalls(b).length, 1);
});

test('not signed in to Reply, or no Reply CLI, a start runs no install of the Reply skills and records nothing', async (t) => {
    const out = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'no' } });
    runtimeAlreadyUp(out);
    await start(out);
    assert.equal(replySkillCalls(out).length, 0);
    assert.ok(!existsSync(replyRecordFile(out)));
    const none = bench(t);
    runtimeAlreadyUp(none);
    await start(none);
    assert.ok(!none.record().some((entry) => entry.name === 'reply'));
    assert.ok(!existsSync(replyRecordFile(none)));
});

test('a reply- folder that stands there with no record is removed and the CLI\'s copy put in its place, never read or kept', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'yes' } });
    runtimeAlreadyUp(b);
    // Jason's own skills are deployed and recorded already, so nothing but the Reply step touches the folders.
    mkdirSync(b.paths.skillsRecords, { recursive: true });
    for (const name of ['claude.json', 'agents.json']) writeFileSync(join(b.paths.skillsRecords, name), '{"version":1,"packs":[]}');
    for (const folder of ['.claude', '.agents']) {
        mkdirSync(join(b.paths.chat, folder, 'skills', 'reply-cli'), { recursive: true });
        writeFileSync(join(b.paths.chat, folder, 'skills', 'reply-cli', 'SKILL.md'), '---\nallowed-tools: Bash\n---');
        mkdirSync(join(b.paths.chat, folder, 'skills', 'reply-x'), { recursive: true });
        writeFileSync(join(b.paths.chat, folder, 'skills', 'reply-x', 'SKILL.md'), 'planted');
    }
    await start(b, { feed: null, skillsDeployedFor: bundleVersion, theme: null });
    assert.equal(replySkillCalls(b).length, 1);
    for (const folder of ['.claude', '.agents']) {
        assert.ok(!existsSync(join(b.paths.chat, folder, 'skills', 'reply-x')), folder);
        assert.equal(readFileSync(join(b.paths.chat, folder, 'skills', 'reply-cli', 'SKILL.md'), 'utf8'), '# reply-cli\n', folder);
    }
    const kept = JSON.parse(readFileSync(replyRecordFile(b), 'utf8')) as Record<string, { path: string }[]>;
    assert.ok(!kept['.claude']?.some((each) => each.path.startsWith('reply-x/')));
});

test('a failed install of the Reply skills lets the start go on with no record, and the next start tries again', async (t) => {
    const b = bench(t, { programs: ['reply'], env: { STUB_REPLY_SIGNED_IN: 'yes', STUB_REPLY_SKILLS_FAIL: 'yes' } });
    runtimeAlreadyUp(b);
    const { started, log } = await start(b);
    assert.equal(started.link, 'http://127.0.0.1:41234/ui/#pair=planted-pair-code');
    assert.ok(!existsSync(replyRecordFile(b)));
    assert.ok(log.lines.some((line) => line.name === 'skills_deployed' && line.fields.Target === 'chat_reply' && line.fields.Exit === 1));
    await start(b, { feed: null, skillsDeployedFor: bundleVersion, theme: null });
    assert.equal(replySkillCalls(b).length, 2, 'tried again, since the record is still absent');
});

test('a start never deploys through a link at the chat folder\'s .claude: the link is removed, its target left byte for byte, the skills deployed into a real folder', async (t) => {
    const b = bench(t);
    runtimeAlreadyUp(b);
    const person = join(b.root, 'person');
    mkdirSync(join(person, 'skills', 'mine'), { recursive: true });
    writeFileSync(join(person, 'skills', 'mine', 'SKILL.md'), 'the person\'s own skill');
    mkdirSync(b.paths.chat, { recursive: true });
    try {
        symlinkSync(person, join(b.paths.chat, '.claude'), 'junction');
    } catch {
        t.skip('links cannot be made here');
        return;
    }
    await start(b);
    assert.deepEqual(readdirSync(person), ['skills']);
    assert.deepEqual(readdirSync(join(person, 'skills')), ['mine']);
    assert.equal(readFileSync(join(person, 'skills', 'mine', 'SKILL.md'), 'utf8'), 'the person\'s own skill');
    const claude = lstatSync(join(b.paths.chat, '.claude'));
    assert.ok(claude.isDirectory() && !claude.isSymbolicLink());
    assert.deepEqual(readdirSync(join(b.paths.chat, '.claude', 'skills')), ['campaign-launch']);
});
