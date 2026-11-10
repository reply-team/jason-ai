import type { UiPlugin } from '../platform/plugin';
import { attentionPlugin } from './attention';
import { campaignsPlugin } from './campaigns';
import { chatPlugin } from './chat';
import { peoplePlugin } from './people';
import { performancePlugin } from './performance';
import { settingsPlugin } from './settings';
import { systemPlugin } from './system';
import { workItemsPlugin } from './workitems';

/** The harness's screens, in both shells: taking a row away takes the screen away, and nothing else changes. */
export const screenPlugins: readonly UiPlugin[] = [
    attentionPlugin,
    performancePlugin,
    campaignsPlugin,
    workItemsPlugin,
    peoplePlugin,
    systemPlugin,
    settingsPlugin,
];

/** The browser's shell: the screens, and the Chat row that points to the desktop application. */
export const browserPlugins: readonly UiPlugin[] = [chatPlugin, ...screenPlugins];
