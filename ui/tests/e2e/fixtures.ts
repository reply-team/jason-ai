import { test as base, expect, type Page } from '@playwright/test';
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

/** Opens the System screen in the console's Page tab, from the sidebar's More menu, as a person does. */
export async function openSystem(page: Page): Promise<void> {
    const side = page.locator('.jc-console .side');
    await side.getByRole('button', { name: 'More', exact: true }).click();
    await side.getByRole('menuitem', { name: /^System/ }).click();
}
