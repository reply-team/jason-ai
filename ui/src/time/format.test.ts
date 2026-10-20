import { describe, expect, it } from 'vitest';
import { clock, dayKey, dayLabel, relative } from './format';

const at = (iso: string) => new Date(iso);

describe('clock', () => {
    it.each([
        ['2026-10-16T09:12:00Z', 'Europe/Kyiv', '12:12'],
        ['2026-10-16T23:30:00Z', 'America/Bogota', '18:30'],
        ['2026-10-16T09:05:00Z', 'UTC', '09:05'],
    ])('%s in %s is %s', (iso, zone, expected) => {
        expect(clock(at(iso), zone)).toBe(expected);
    });
});

describe('dayKey', () => {
    it.each([
        ['2026-10-16T23:30:00Z', 'Europe/Kyiv', '2026-10-17'],
        ['2026-10-16T23:30:00Z', 'UTC', '2026-10-16'],
        ['2026-10-17T02:00:00Z', 'America/Bogota', '2026-10-16'],
    ])('%s in %s falls on %s', (iso, zone, expected) => {
        expect(dayKey(at(iso), zone)).toBe(expected);
    });
});

describe('dayLabel', () => {
    it.each([
        ['2026-10-16', '2026-10-16', 'Today'],
        ['2026-10-15', '2026-10-16', 'Yesterday'],
        ['2026-10-17', '2026-10-16', 'Tomorrow'],
        ['2026-10-13', '2026-10-16', 'Tuesday 13 October'],
        ['2025-12-31', '2026-10-16', '31 December 2025'],
    ])('%s seen from %s reads %s', (key, today, expected) => {
        expect(dayLabel(key, today)).toBe(expected);
    });
});

describe('relative', () => {
    const now = at('2026-10-16T12:00:00Z');
    it.each([
        ['2026-10-16T11:59:40Z', 'just now'],
        ['2026-10-16T11:58:00Z', '2 min ago'],
        ['2026-10-16T10:00:00Z', '2 h ago'],
        ['2026-10-15T12:00:00Z', 'yesterday'],
        ['2026-10-13T12:00:00Z', '3 d ago'],
        ['2026-09-01T12:00:00Z', '1 Sep'],
        ['2026-10-16T12:05:00Z', 'in 5 min'],
        ['2026-10-18T12:00:00Z', 'in 2 d'],
    ])('%s is %s', (iso, expected) => {
        expect(relative(at(iso), now)).toBe(expected);
    });
});
