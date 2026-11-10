// A stand-in for the jason executable: answers the verbs the desktop runs in the shapes the real CLI prints, and
// writes only what the real CLI would (the run file, as a started runtime does).
const { existsSync, mkdirSync, readFileSync, writeFileSync } = require('node:fs');
const { join } = require('node:path');
const record = require('./record.cjs');

const args = process.argv.slice(2);
const stdin = record.readStdin();
record('jason', args, stdin);

const data = process.env.JASON_DATA_DIR;
const runFile = join(data, 'run', 'runtime.json');
const origin = process.env.STUB_ORIGIN || 'http://127.0.0.1:41234';
const verb = args.slice(0, 2).join(' ');
const print = (value) => process.stdout.write(`${JSON.stringify(value)}\n`);

if (args[0] === 'status') {
    const up = existsSync(runFile);
    print({
        ready: false,
        checks: [
            { name: 'runtime', required: true, state: up ? 'ok' : 'failed', fact: '', fix: '' },
            { name: 'runtime_version', required: false, state: up ? (process.env.STUB_SAME_VERSION === 'no' ? 'failed' : 'ok') : 'unknown', fact: '', fix: '' },
            { name: 'ui_module', required: false, state: up ? process.env.STUB_UI_MODULE || 'absent' : 'unknown', fact: '', fix: '' },
        ],
    });
    process.exit(1);
} else if (verb === 'runtime start') {
    if (process.env.STUB_START_FAILS === 'yes') {
        process.stderr.write('The runtime did not start.\n');
        process.exit(1);
    }
    mkdirSync(join(data, 'run'), { recursive: true });
    writeFileSync(runFile, JSON.stringify({ api_version: 'v1', base_url: origin, token: 'planted-run-token-77', pid: 1 }));
    print({ started: true });
} else if (verb === 'runtime status') {
    print({ runtime_version: process.env.STUB_RUNTIME_VERSION || '0.34.0-rc.1' });
} else if (verb === 'ui open') {
    print({ url: process.env.STUB_LINK || `${origin}/ui/#pair=planted-pair-code` });
} else if (verb === 'plugin list') {
    if (process.env.STUB_PLUGIN_LIST_FAILS === 'yes') {
        process.stderr.write('No runtime answered.\n');
        process.exit(3);
    }
    const granted = (http, env) => ({ exec: null, http: { requested: [], granted: http }, env: { requested: [], granted: env }, api: null, subscriptions: null });
    print({
        plugins: [
            { id: 'reply', status: 'valid', capabilities: granted([], []) },
            { id: 'slack', status: 'valid', capabilities: granted(['slack.com'], ['SLACK_PROXY']) },
            { id: 'ntfy', status: 'invalid', capabilities: granted([], []) },
        ],
    });
} else if (verb === 'plugin reload' && process.env.STUB_RELOAD_REFUSED === 'yes') {
    print({
        error: {
            code: 'plugin_reload_rejected',
            message: 'The reload was rejected; the previous plugins stay active.',
            retryable: false,
            details: [{ field: 'settings.json', code: 'settings_file_unreadable', message: 'the settings file could not be read (not valid JSON at line 1, byte 2), so nothing it grants or routes can be resolved; fix it, or close what holds it open, and reload again.' }],
        },
    });
    process.exit(1);
} else if (verb === 'ui install' && process.env.STUB_UI_INSTALL_FAILS === 'yes') {
    process.stderr.write('The release could not be reached.\n');
    process.exit(1);
} else if (verb === 'route list') {
    // The global default is the one the settings file holds, as the runtime's reload froze it.
    let settings = {};
    try {
        settings = JSON.parse(readFileSync(join(data, 'config', 'settings.json'), 'utf8'));
    } catch {}
    const route = settings.Routes && settings.Routes.Default;
    const global = route ? { plugin_id: route.Plugin, binding: route.Binding || null, binding_identity: null } : null;
    print({ routing_snapshot_id: 'rts_1', plugin_snapshot_id: 'snp_1', global: { default: global, operations: {} }, campaigns: [] });
} else if (['ui install', 'skills install', 'plugin install', 'plugin reload'].includes(verb)) {
    print({});
} else {
    process.stderr.write(`the stub does not answer ${verb}\n`);
    process.exit(2);
}
