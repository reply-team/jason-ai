import { describe, expect, it } from 'vitest';
import { activatePlugins, type UiPlugin } from './plugin';
import { SlotRegistry, type ScreenEntry, type SlotMap } from './slots';

const screen = (id: string, order = 0): ScreenEntry =>
    ({ id, order, label: id, href: () => `#/${id}`, component: () => null }) as ScreenEntry;

describe('the slot registry', () => {
    it('lists by order, then id', () => {
        const slots = new SlotRegistry();
        slots.register('screen', 'a', screen('people', 2));
        slots.register('screen', 'a', screen('system', 1));
        slots.register('screen', 'a', screen('chat', 2));
        expect(slots.list('screen').map((p) => p.id)).toEqual(['system', 'chat', 'people']);
    });
    it('refuses a second entry with the same id, naming who holds it', () => {
        const slots = new SlotRegistry();
        slots.register('screen', 'first', screen('system'));
        expect(() => slots.register('screen', 'second', screen('system'))).toThrow(/first/);
    });
    it('forgets an entry when it is disposed', () => {
        const slots = new SlotRegistry();
        slots.register('screen', 'a', screen('system'))();
        expect(slots.list('screen')).toEqual([]);
    });
    it('has the one slot the shell is built from, and none a package could fill', () => {
        const names: Record<keyof SlotMap, true> = { screen: true };
        expect(Object.keys(names)).toEqual(['screen']);
    });
});

describe('activating plugins', () => {
    const adds = (id: string): UiPlugin => ({ id, setup: ({ register }) => register('screen', screen(id)) });

    it('replaces what was active', () => {
        const slots = new SlotRegistry();
        activatePlugins([adds('people')], slots);
        activatePlugins([adds('system')], slots);
        expect(slots.list('screen').map((p) => p.id)).toEqual(['system']);
    });
    it('leaves nothing of a plugin that throws, and loads the rest', () => {
        const slots = new SlotRegistry();
        const broken: UiPlugin = {
            id: 'broken',
            setup({ register }) {
                register('screen', screen('people'));
                throw new Error('no');
            },
        };
        const problems = activatePlugins([broken, adds('system')], slots);
        expect(problems).toEqual([{ plugin: 'broken', message: 'no' }]);
        expect(slots.list('screen').map((p) => p.id)).toEqual(['system']);
    });
    it('runs what a plugin asked to be disposed', () => {
        const slots = new SlotRegistry();
        let disposed = 0;
        activatePlugins([{ id: 'p', setup: ({ onDispose }) => onDispose(() => disposed++) }], slots);
        activatePlugins([], slots);
        expect(disposed).toBe(1);
    });
});
