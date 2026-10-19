import { test as base, expect } from '@playwright/test';
import { Jason } from './harness';

/** A runtime with the default build; a test that needs another shape starts its own with Jason.start and stops it. */
export const test = base.extend<{ jason: Jason }>({
    // eslint-disable-next-line no-empty-pattern
    jason: async ({}, use) => {
        const jason = await Jason.start();
        await use(jason);
        await jason.stop();
        jason.dispose();
    },
});

export { expect };
