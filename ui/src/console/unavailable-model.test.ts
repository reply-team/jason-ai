import { describe, expect, it } from 'vitest';
import { fixtures } from '../test/fixtures';
import { pluginLabel, routedPlugins, unavailableRouted, type ListedPlugin } from './unavailable-model';

const listed = (name: 'plugin.list/reply-unavailable' | 'plugin.list/card-accounts-reply'): ListedPlugin[] =>
    (fixtures[name].response as { plugins: ListedPlugin[] }).plugins;

describe('The plugins a route names', () => {
    it('are the global default, and every operation, channel and campaign route', () => {
        expect(routedPlugins(fixtures['route.list/card-accounts-reply'].response)).toEqual(new Set(['reply']));
        expect(routedPlugins(fixtures['route.list/channel-routes'].response)).toEqual(new Set(['reply', 'other-tool']));
        expect(
            routedPlugins({
                global: {
                    default: { plugin_id: 'a' },
                    operations: { 'message.send': { plugin_id: 'b' } },
                    channels: { 'message.send': { email: { plugin_id: 'c' } } },
                },
                campaigns: [{ campaign_id: 'cmp_1', default: { plugin_id: 'd' }, operations: {}, channels: {} }],
            }),
        ).toEqual(new Set(['a', 'b', 'c', 'd']));
    });

    it('are none where no route stands, or the answer is not one', () => {
        expect(routedPlugins(fixtures['route.list/empty'].response)).toEqual(new Set());
        expect(routedPlugins(null)).toEqual(new Set());
        expect(routedPlugins({ global: null, campaigns: 'no' })).toEqual(new Set());
    });
});

describe('An unavailable plugin a route names', () => {
    it('is named as the Accounts card names it, with the runtime’s problem text and its repair', () => {
        expect(unavailableRouted(listed('plugin.list/reply-unavailable'), new Set(['reply']))).toEqual([
            {
                id: 'reply',
                label: 'Reply (reply)',
                problem:
                    "'reply' reports 0.21.0; the manifest asks for 0.22.0 or later. Install 'reply' 0.22.0 or later where the runtime can see it and run `jason plugin reload`.",
            },
        ]);
    });

    it('is not one where no route names it, or where it is available', () => {
        expect(unavailableRouted(listed('plugin.list/reply-unavailable'), new Set(['other']))).toEqual([]);
        expect(unavailableRouted(listed('plugin.list/card-accounts-reply'), new Set(['reply']))).toEqual([]);
    });

    it('carries no problem text where the runtime lists none', () => {
        expect(
            unavailableRouted([{ id: 'tool', name: null, status: 'unavailable', problems: [] }], new Set(['tool'])),
        ).toEqual([{ id: 'tool', label: 'tool', problem: '' }]);
    });

    it('is named by its id where its manifest gives no name', () => {
        expect(pluginLabel('tool', null)).toBe('tool');
        expect(pluginLabel('tool', '')).toBe('tool');
    });
});
