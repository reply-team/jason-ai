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
    // STUB_START_REFUSES=<code> answers as the real verb does for a layout it refuses: the error on stdout, nothing started.
    if (process.env.STUB_START_REFUSES) {
        print({ error: { code: process.env.STUB_START_REFUSES, message: process.env.STUB_START_SAYS || 'The layout is refused.', retryable: false } });
        process.exit(1);
    }
    mkdirSync(join(data, 'run'), { recursive: true });
    writeFileSync(runFile, JSON.stringify({ api_version: 'v1', base_url: origin, token: 'planted-run-token-77', pid: 1 }));
    print({ started: true });
} else if (verb === 'runtime status') {
    // STUB_HARNESS_ROOT names the harness root the runtime reports, in the state STUB_HARNESS_STATE says (else marked).
    const harness = process.env.STUB_HARNESS_ROOT ? { harness: { root: process.env.STUB_HARNESS_ROOT, root_state: process.env.STUB_HARNESS_STATE || 'marked' } } : {};
    print({ runtime_version: process.env.STUB_RUNTIME_VERSION || '0.34.0-rc.1', ...harness });
} else if (verb === 'runtime autostart') {
    // The real verb registers with the machine; the stub only answers as if it had, and registers nothing.
    const registered = args[2] === 'enable' ? true : args[2] === 'disable' ? false : process.env.STUB_AUTOSTART === 'yes';
    print({ registered, platform: 'windows', data_directory: registered ? data : null, executable: null, executable_exists: null, artifact_path: null });
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
} else if (verb === 'campaign get') {
    // The fields of the campaign DTO the chat reads; the folder is the one a test names, or none.
    if (process.env.STUB_CAMPAIGN_MISSING === 'yes') {
        print({ error: { code: 'campaign_not_found', message: 'No campaign has that id.', retryable: false } });
        process.exit(1);
    }
    print({ id: args[2], name: process.env.STUB_CAMPAIGN_NAME || 'Spring', status: process.env.STUB_CAMPAIGN_STATUS || 'active', project_id: 'prj_01STUB', project_slug: 'default', folder_path: process.env.STUB_CAMPAIGN_FOLDER || null });
} else if (verb === 'campaign list') {
    // Every campaign STUB_CAMPAIGNS_FILE holds, in pages of STUB_PAGE_SIZE (else --limit), the cursor the index of the
    // next page; STUB_LIST_FAIL_PAGE names a page (from 1) that is refused, 'all' every page.
    let items = [];
    try {
        items = JSON.parse(readFileSync(process.env.STUB_CAMPAIGNS_FILE, 'utf8'));
    } catch {}
    const size = Number(process.env.STUB_PAGE_SIZE || args[args.indexOf('--limit') + 1] || 1000);
    const from = args.includes('--cursor') ? Number(args[args.indexOf('--cursor') + 1]) : 0;
    const page = from / size + 1;
    if (process.env.STUB_LIST_FAIL_PAGE === 'all' || process.env.STUB_LIST_FAIL_PAGE === String(page)) {
        process.stderr.write('No runtime answered.\n');
        process.exit(3);
    }
    const next = from + size < items.length ? String(from + size) : null;
    print({ items: items.slice(from, from + size), next_cursor: next });
} else if (verb === 'campaign create') {
    // Adds the campaign to STUB_CAMPAIGNS_FILE, as a draft of the default project, unless one was made under the same
    // --idempotency-key, which is answered again, as the runtime does. STUB_CREATE_FAILS=1 answers the runtime's refusal
    // (STUB_CREATE_SAYS its message), 3 an unreachable runtime; STUB_CREATE_LOST=yes makes the campaign and then loses the
    // answer, as a timeout does.
    if (process.env.STUB_CREATE_FAILS === '3') {
        process.stderr.write('No runtime answered.\n');
        process.exit(3);
    }
    if (process.env.STUB_CREATE_FAILS === '1') {
        print({ error: { code: 'validation_failed', message: process.env.STUB_CREATE_SAYS || 'The name is not valid.', retryable: false } });
        process.exit(1);
    }
    let items = [];
    try {
        items = JSON.parse(readFileSync(process.env.STUB_CAMPAIGNS_FILE, 'utf8'));
    } catch {}
    const flag = (name) => (args.includes(name) ? args[args.indexOf(name) + 1] : null);
    const key = flag('--idempotency-key');
    let made = key === null ? undefined : items.find((each) => each.idempotency_key === key);
    // As the CLI does: with no --project, the project is the one the nearest marker above the working folder names,
    // else the default.
    let project = flag('--project');
    for (let dir = process.cwd(); project === null; ) {
        try {
            const marker = JSON.parse(readFileSync(join(dir, '.jason', 'marker.json'), 'utf8'));
            if (marker.kind === 'project') project = marker.id;
        } catch {}
        const up = join(dir, '..');
        if (up === dir) break;
        dir = up;
    }
    if (made === undefined) {
        made = { id: `cmp_MADE${items.length + 1}`, name: flag('--name'), status: 'draft', created_at: new Date().toISOString(), idempotency_key: key, project_id: project || 'prj_01STUB' };
        items.push(made);
        writeFileSync(process.env.STUB_CAMPAIGNS_FILE, JSON.stringify(items));
    }
    if (process.env.STUB_CREATE_LOST === 'yes') process.exit(1);
    print({ id: made.id, name: made.name, status: made.status, project_id: made.project_id, folder_path: null });
} else if (verb === 'journal list') {
    // The lines STUB_JOURNAL_FILE holds, as one page: newest first, as the runtime answers; STUB_JOURNAL_MORE=yes says
    // there is a next page, and STUB_JOURNAL_FAILS=yes answers as an unreachable runtime does.
    if (process.env.STUB_JOURNAL_FAILS === 'yes') {
        process.stderr.write('No runtime answered.\n');
        process.exit(3);
    }
    // STUB_JOURNAL_FAILS_KIND=<kind> fails a request that names that kind alone; STUB_JOURNAL_FAILS_EXCEPT=<kind> fails every
    // request that does not name it: one of the Feed's two reads failing, the other not.
    const named = args.flatMap((arg, i) => (arg === '--kind' ? [args[i + 1]] : []));
    if (
        (process.env.STUB_JOURNAL_FAILS_KIND && named.includes(process.env.STUB_JOURNAL_FAILS_KIND)) ||
        (process.env.STUB_JOURNAL_FAILS_EXCEPT && !named.includes(process.env.STUB_JOURNAL_FAILS_EXCEPT))
    ) {
        process.stderr.write('No runtime answered.\n');
        process.exit(3);
    }
    let items = [];
    try {
        items = JSON.parse(readFileSync(process.env.STUB_JOURNAL_FILE, 'utf8'));
    } catch {}
    // STUB_JOURNAL_SLEEP holds the answer back that many milliseconds, as a slow runtime does.
    // As the runtime does, a request that names kinds gets lines of those kinds only.
    const asked = args.flatMap((arg, i) => (arg === '--kind' ? [args[i + 1]] : []));
    if (asked.length > 0) items = items.filter((item) => asked.includes(item.kind));
    const answer = () => print({ items, next_cursor: process.env.STUB_JOURNAL_MORE === 'yes' ? 'jrn_next' : null });
    if (process.env.STUB_JOURNAL_SLEEP) setTimeout(answer, Number(process.env.STUB_JOURNAL_SLEEP));
    else answer();
} else if (['profile list', 'profile create', 'profile get', 'profile set-default'].includes(verb)) {
    // The execution profiles, kept beside the record (outside the data directory, as the runtime's database is no file a
    // test reads): each { name, disabled, revision: { host, program } }, plus what the desktop's own settings file held when
    // it was made. STUB_PROFILE_LIST_FAILS=yes answers as an unreachable runtime does; STUB_PROFILE_CREATE_FAILS=yes refuses
    // the create; STUB_PROFILE_CREATE_LOST=yes makes the profile and still exits 1, as an answer lost on its way would;
    // STUB_PROFILE_CREATE_STOPS_RUNTIME=yes makes it, and every later profile verb answers as an unreachable runtime does;
    // STUB_PROFILE_CREATE_NAMES_DEFAULT=<name> makes it and names that default in the installation's settings file, as a
    // person editing the file at that moment would. A made profile gets an id of the runtime's shape.
    // The default in force is the one the settings file names, as the runtime binds it (any case of the keys; null or
    // absent is none), or STUB_DEFAULT_IN_FORCE=<name>, as a JASON_ variable sets it; every page of the list says it, and
    // STUB_PROFILE_LIST_NO_DEFAULT=yes leaves it out, as a runtime older than the field does; --cursor continues a list
    // where the last page's next_cursor said, and STUB_PROFILE_LIST_CURSOR=<cursor> answers that cursor on every page. set-default writes it into
    // the settings file, keeping every other key, and answers it; STUB_PROFILE_SET_DEFAULT_REFUSED=<code> refuses it with
    // that code, STUB_PROFILE_SET_DEFAULT_ANSWERS=<name> answers that name instead, STUB_PROFILE_SET_DEFAULT_LOST=yes writes
    // it and answers request_timed_out, as a request the runtime took and did not answer in time does, and
    // STUB_PROFILE_SET_DEFAULT_UNREACHABLE=yes answers as an unreachable runtime does. Under STUB_DEFAULT_IN_FORCE it is
    // refused setting_shadowed, as the runtime refuses a default a JASON_ variable sets.
    const { dirname } = require('node:path');
    const store = join(dirname(process.env.STUB_RECORD), 'profiles.json');
    const stopped = join(dirname(process.env.STUB_RECORD), 'runtime-stopped');
    let profiles = [];
    try {
        profiles = JSON.parse(readFileSync(store, 'utf8'));
    } catch {}
    const refuse = (code, message) => {
        print({ error: { code, message, retryable: false } });
        process.exit(1);
    };
    const settingsFile = join(data, 'config', 'settings.json');
    const isObject = (value) => typeof value === 'object' && value !== null && !Array.isArray(value);
    const keyOf = (object, key) => Object.keys(object).find((each) => each.toLowerCase() === key.toLowerCase()) ?? key;
    const readHeld = () => {
        try {
            const held = JSON.parse(readFileSync(settingsFile, 'utf8'));
            return isObject(held) ? held : {};
        } catch {
            return {};
        }
    };
    const inForce = () => {
        if (process.env.STUB_DEFAULT_IN_FORCE) return process.env.STUB_DEFAULT_IN_FORCE;
        const held = readHeld();
        const roles = held[keyOf(held, 'Roles')];
        const value = isObject(roles) ? roles[keyOf(roles, 'DefaultExecutionProfile')] : undefined;
        return typeof value === 'string' && value !== '' ? value : null;
    };
    if (verb !== 'profile create' && existsSync(stopped)) {
        process.stderr.write('No runtime answered.\n');
        process.exit(3);
    }
    if (verb === 'profile list') {
        if (process.env.STUB_PROFILE_LIST_FAILS === 'yes') {
            process.stderr.write('No runtime answered.\n');
            process.exit(3);
        }
        const listed = args.includes('--include-disabled') ? profiles : profiles.filter((profile) => !profile.disabled);
        const size = args.includes('--limit') ? Number(args[args.indexOf('--limit') + 1]) : 50;
        const from = args.includes('--cursor') ? Number(args[args.indexOf('--cursor') + 1]) : 0;
        const next = process.env.STUB_PROFILE_LIST_CURSOR || (listed.length > from + size ? String(from + size) : null);
        const page = { items: listed.slice(from, from + size), next_cursor: next };
        print(process.env.STUB_PROFILE_LIST_NO_DEFAULT === 'yes' ? page : { ...page, default_execution_profile: inForce() });
    } else if (verb === 'profile set-default') {
        if (process.env.STUB_PROFILE_SET_DEFAULT_UNREACHABLE === 'yes') {
            process.stderr.write('No runtime answered.\n');
            process.exit(3);
        }
        if (process.env.STUB_PROFILE_SET_DEFAULT_REFUSED) refuse(process.env.STUB_PROFILE_SET_DEFAULT_REFUSED, 'The default was not written.');
        if (process.env.STUB_DEFAULT_IN_FORCE) refuse('setting_shadowed', 'A JASON_ variable sets the default, so the file would not be in force.');
        const name = args[2];
        if (!profiles.some((profile) => profile.name === name)) refuse('profile_not_found', 'No profile has that name.');
        const held = readHeld();
        const rolesKey = keyOf(held, 'Roles');
        const roles = isObject(held[rolesKey]) ? held[rolesKey] : {};
        held[rolesKey] = { ...roles, [keyOf(roles, 'DefaultExecutionProfile')]: name };
        mkdirSync(join(data, 'config'), { recursive: true });
        writeFileSync(settingsFile, JSON.stringify(held, null, 2));
        if (process.env.STUB_PROFILE_SET_DEFAULT_LOST === 'yes') refuse('request_timed_out', 'The runtime took the request and did not answer in time.');
        print({ execution_profile: process.env.STUB_PROFILE_SET_DEFAULT_ANSWERS || name });
    } else if (verb === 'profile get') {
        const found = profiles.find((profile) => profile.name === args[2]);
        if (found === undefined) refuse('profile_not_found', 'No profile has that name.');
        print(found);
    } else {
        if (process.env.STUB_PROFILE_CREATE_FAILS === 'yes') refuse('validation_failed', 'The profile was refused.');
        const name = args[2];
        if (profiles.some((profile) => profile.name === name)) refuse('profile_exists', 'A profile has that name.');
        const option = (flag) => (args.includes(flag) ? args[args.indexOf(flag) + 1] : null);
        let mark = null;
        try {
            mark = JSON.parse(readFileSync(join(data, 'desktop', 'settings.json'), 'utf8')).roles_profile_pending ?? null;
        } catch {}
        const id = `exp_01JD${String(profiles.length + 1).padStart(22, '0')}`;
        const made = { id, name, disabled: false, revision: { host: option('--host'), program: option('--program') }, mark_when_made: mark };
        writeFileSync(store, JSON.stringify([...profiles, made]));
        if (process.env.STUB_PROFILE_CREATE_STOPS_RUNTIME === 'yes') writeFileSync(stopped, '');
        if (process.env.STUB_PROFILE_CREATE_NAMES_DEFAULT) {
            const file = join(data, 'config', 'settings.json');
            let held = {};
            try {
                held = JSON.parse(readFileSync(file, 'utf8'));
            } catch {}
            held.Roles = { ...(held.Roles ?? {}), DefaultExecutionProfile: process.env.STUB_PROFILE_CREATE_NAMES_DEFAULT };
            mkdirSync(join(data, 'config'), { recursive: true });
            writeFileSync(file, JSON.stringify(held, null, 2));
        }
        if (process.env.STUB_PROFILE_CREATE_LOST === 'yes') {
            process.stderr.write('No answer came back.\n');
            process.exit(1);
        }
        print(made);
    }
} else if (verb === 'project get') {
    // STUB_PROJECT_GET_FAILS=yes: the runtime does not answer, as when it is down for a moment.
    if (process.env.STUB_PROJECT_GET_FAILS === 'yes') process.exit(3);
    // By --slug the default project answers by its own id, prj_01STUB, as every installation's default does.
    const bySlug = args.includes('--slug');
    print({ id: bySlug ? 'prj_01STUB' : args[2], name: 'Default', slug: 'default', default: true, folder_path: process.env.STUB_PROJECT_FOLDER || null });
} else if (verb === 'skills install' && args.includes('--root')) {
    // As the CLI deploys: into the very directory --root names, a record of the deployment and one folder per skill.
    // STUB_SKILLS_FAIL_HOST names a host whose deployment fails.
    if (process.env.STUB_SKILLS_FAIL_HOST && args[args.indexOf('--host') + 1] === process.env.STUB_SKILLS_FAIL_HOST) {
        print({ error: { code: 'skills_not_deployed', message: 'The stub refused this host.', retryable: false } });
        process.exit(1);
    }
    const root = args[args.indexOf('--root') + 1];
    mkdirSync(join(root, 'campaign-launch'), { recursive: true });
    const skill = '# campaign-launch\n';
    writeFileSync(join(root, 'campaign-launch', 'SKILL.md'), skill);
    // The record as the CLI writes it (SkillsRecord): every file written, relative, with the digest of what was written.
    const sha256 = require('node:crypto').createHash('sha256').update(skill).digest('hex');
    const deployment = { pack: 'business', source: 'bundle', ref: 'stub', ref_overridden: false, commit: null, installed_at: '2026-11-12T00:00:00Z', files: [{ path: 'campaign-launch/SKILL.md', sha256 }] };
    writeFileSync(join(root, '.jason-skills.json'), `${JSON.stringify({ version: 1, packs: [deployment] })}\n`);
    print({});
} else if (['ui install', 'skills install', 'plugin install', 'plugin reload'].includes(verb)) {
    print({});
} else {
    process.stderr.write(`the stub does not answer ${verb}\n`);
    process.exit(2);
}
