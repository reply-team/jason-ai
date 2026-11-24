import { describe, expect, it } from 'vitest';
import type { DesktopUpdateItem } from '../platform/desktop';
import { cardOf, settledMark } from './update-card';

const item = (
    step: 'offered' | 'downloading' | 'downloaded' | null,
    newer: string | null = '0.51.0',
    label = 'Download Jason 0.51.0',
): DesktopUpdateItem => ({
    version: '0.50.1',
    item: { label, enabled: true },
    said: null,
    newer,
    step,
});

describe('the update card: what shows when', () => {
    it('shows the offered, downloading and downloaded steps, each with the version and the label as received', () => {
        expect(cardOf(item('offered'), null)).toEqual({
            step: 'offered',
            version: '0.51.0',
            label: 'Download Jason 0.51.0',
            enabled: true,
        });
        expect(cardOf(item('downloading', '0.51.0', 'Downloading Jason 0.51.0…'), null)?.step).toBe('downloading');
        expect(cardOf(item('downloaded', '0.51.0', 'Restart Jason and Its Runtime to Install 0.51.0'), null)).toEqual({
            step: 'downloaded',
            version: '0.51.0',
            label: 'Restart Jason and Its Runtime to Install 0.51.0',
            enabled: true,
        });
    });

    it('shows nothing without an answer, without an item, at every other step, and for a step it has no words for', () => {
        expect(cardOf(null, null)).toBeNull();
        expect(cardOf({ ...item('offered'), item: null }, null)).toBeNull();
        expect(cardOf(item(null), null)).toBeNull();
        expect(cardOf(item('installing' as never), null)).toBeNull();
        expect(cardOf(item('anything' as never), null)).toBeNull();
    });

    it('shows nothing from a desktop that sends no step, whatever its label says', () => {
        const older: DesktopUpdateItem = {
            version: '0.50.1',
            item: { label: 'Download Jason 0.51.0', enabled: true },
            newer: '0.51.0',
        };
        expect(cardOf(older, null)).toBeNull();
    });

    it('shows nothing where the version is not told', () => {
        expect(cardOf(item('offered', null), null)).toBeNull();
        expect(cardOf(item('offered', ''), null)).toBeNull();
    });

    it('carries whether the item can be pressed now', () => {
        const busy = { ...item('offered'), item: { label: 'Download Jason 0.51.0', enabled: false } };
        expect(cardOf(busy, null)?.enabled).toBe(false);
    });
});

describe('the update card: the hide rule', () => {
    const hidden = { step: 'offered', version: '0.51.0' } as const;

    it('hides the card while the same step offers the same version', () => {
        expect(cardOf(item('offered'), hidden)).toBeNull();
    });

    it('brings it back when the step changes: a download that ran', () => {
        expect(cardOf(item('downloading', '0.51.0', 'Downloading Jason 0.51.0…'), hidden)?.step).toBe('downloading');
        expect(cardOf(item('downloaded', '0.51.0', 'x'), hidden)?.step).toBe('downloaded');
    });

    it('brings it back when a newer version than the hidden one is offered', () => {
        expect(cardOf(item('offered', '0.51.1'), hidden)?.version).toBe('0.51.1');
        expect(cardOf(item('offered', '0.52.0'), hidden)?.version).toBe('0.52.0');
        expect(cardOf(item('offered', '0.51.10'), { step: 'offered', version: '0.51.9' })?.version).toBe('0.51.10');
    });

    it('keeps it hidden for an older version of the same step', () => {
        expect(cardOf(item('offered', '0.50.9'), hidden)).toBeNull();
    });

    it('brings it back for a version it cannot read that differs from the hidden one', () => {
        expect(cardOf(item('offered', '0.51.0-rc.2'), hidden)?.version).toBe('0.51.0-rc.2');
        expect(cardOf(item('offered', 'next'), hidden)?.version).toBe('next');
        expect(settledMark(hidden, item('offered', 'next'))).toBeNull();
    });

    it('comes back with the next start: the mark is the page memory alone, so a fresh page has none', () => {
        expect(cardOf(item('offered'), null)).not.toBeNull();
    });
});

describe('the update card: what the page keeps of a hidden card', () => {
    const hidden = { step: 'offered', version: '0.51.0' } as const;

    it('keeps the mark while the answer is the same, and while no answer has been read', () => {
        expect(settledMark(hidden, item('offered'))).toEqual(hidden);
        expect(settledMark(hidden, null)).toEqual(hidden);
    });

    it('forgets the mark when the step changes, so a download that failed offers the card again', () => {
        expect(settledMark(hidden, item('downloading', '0.51.0', 'x'))).toBeNull();
        expect(settledMark(hidden, item(null))).toBeNull();
    });

    it('forgets the mark when a newer version is offered', () => {
        expect(settledMark(hidden, item('offered', '0.51.1'))).toBeNull();
    });

    it('keeps no mark that was never made', () => {
        expect(settledMark(null, item('offered'))).toBeNull();
    });
});
