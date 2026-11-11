import { registry, type SlotMap, type SlotName, type SlotRegistry } from './slots';

export interface PluginContext {
    readonly id: string;
    register<S extends SlotName>(slot: S, entry: SlotMap[S]): void;
    /** Called when the plugin is taken away: a subscription, a timer. */
    onDispose(dispose: () => void): void;
}

/** One built-in part of the shell: everything it adds, it adds through `setup`. */
export interface UiPlugin {
    id: string;
    setup(context: PluginContext): void;
}

export interface PluginProblem {
    plugin: string;
    message: string;
}

let disposers: (() => void)[] = [];

/** Replaces what is active by `plugins`. A plugin that throws leaves nothing behind and stops no other plugin. */
export function activatePlugins(plugins: readonly UiPlugin[], into: SlotRegistry = registry): PluginProblem[] {
    for (const dispose of disposers.reverse()) dispose();
    disposers = [];
    const problems: PluginProblem[] = [];
    for (const plugin of plugins) {
        const own: (() => void)[] = [];
        try {
            plugin.setup({
                id: plugin.id,
                register: (slot, entry) => void own.push(into.register(slot, plugin.id, entry)),
                onDispose: (dispose) => void own.push(dispose),
            });
            disposers.push(...own);
        } catch (error) {
            for (const dispose of own.reverse()) dispose();
            problems.push({ plugin: plugin.id, message: error instanceof Error ? error.message : String(error) });
        }
    }
    return problems;
}
