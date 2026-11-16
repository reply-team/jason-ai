import { signal } from '@preact/signals-react';

/** The sections of the console's Settings, in the order its list names them. */
export const settingsSections = ['appearance', 'voice', 'modes', 'models', 'runtime'] as const;
export type SettingsSection = (typeof settingsSections)[number];

/** The section the console's Settings shows: kept here so a button elsewhere can open Settings at one. */
export const chosenSection = signal<SettingsSection>('appearance');
