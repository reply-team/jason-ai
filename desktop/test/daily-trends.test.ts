import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { applyDailyTrends, parseSettings, readSettings, settingsAtStart, writeSettings, type DesktopSettings } from '../src/settings.ts';

const base: DesktopSettings = { feed: null, skillsDeployedFor: '0.35.0', theme: 'dark' };

test('a choice is written with the other settings kept beside it, and returned', () => {
    for (const on of [true, false]) {
        const written: DesktopSettings[] = [];
        const next = applyDailyTrends(on, base, (settings) => void written.push(settings));
        assert.deepEqual(written, [{ ...base, dailyTrends: on }]);
        assert.deepEqual(next, { ...base, dailyTrends: on });
    }
});

test('anything but true or false is refused with nothing written', () => {
    for (const bad of ['yes', 1, 0, null, undefined, {}, 'true']) {
        const written: DesktopSettings[] = [];
        assert.throws(() => applyDailyTrends(bad, base, (settings) => void written.push(settings)), /not a choice/);
        assert.deepEqual(written, []);
    }
});

test('the same choice again writes nothing', () => {
    const written: DesktopSettings[] = [];
    applyDailyTrends(false, { ...base, dailyTrends: false }, (settings) => void written.push(settings));
    assert.deepEqual(written, []);
});

test('the choice survives the file, and is absent from it until one was told', () => {
    const file = join(mkdtempSync(join(tmpdir(), 'trends-')), 'desktop', 'settings.json');
    writeSettings(file, { ...base, dailyTrends: false });
    assert.equal(parseSettings(readFileSync(file, 'utf8')).dailyTrends, false);
    assert.equal(JSON.parse(readFileSync(file, 'utf8')).daily_trends, false);
    writeSettings(file, base);
    assert.equal('daily_trends' in JSON.parse(readFileSync(file, 'utf8')), false);
    assert.equal(parseSettings(readFileSync(file, 'utf8')).dailyTrends, undefined);
});

test('a value in the file that is not a boolean is taken as never told', () => {
    assert.equal(parseSettings('{"daily_trends":"off"}').dailyTrends, undefined);
    assert.equal(parseSettings('{"daily_trends":0}').dailyTrends, undefined);
    assert.equal(parseSettings('{"daily_trends":true}').dailyTrends, true);
});

test('a settings file with no daily_trends reads as never told, is not rewritten by the read, and is written only when the switch moves', () => {
    const file = join(mkdtempSync(join(tmpdir(), 'trends-existing-')), 'desktop', 'settings.json');
    writeSettings(file, base);
    const before = readFileSync(file, 'utf8');
    assert.equal(parseSettings(before).dailyTrends, undefined);
    assert.equal(settingsAtStart(file, false).dailyTrends, undefined);
    assert.equal(readFileSync(file, 'utf8'), before);
    applyDailyTrends(false, readSettings(file), (next) => writeSettings(file, next));
    assert.notEqual(readFileSync(file, 'utf8'), before);
    assert.equal(parseSettings(readFileSync(file, 'utf8')).dailyTrends, false);
});
