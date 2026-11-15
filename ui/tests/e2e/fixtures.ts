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

/**
 * Opens a page Live's sidebar draws no row for through Search, as a person does: the sidebar's own Search row (found by
 * its action, since Recent's "Search agents" button shares the name's prefix), the query, Enter on the first result.
 */
export async function openBySearch(page: Page, text: string): Promise<void> {
    await page.locator('.jc-console .side [data-action="side.search"]').click();
    await page.getByPlaceholder('Search agents, people, pages and settings').fill(text);
    await page.keyboard.press('Enter');
}

/** Opens the System screen in the console's area, through Search. */
export async function openSystem(page: Page): Promise<void> {
    await openBySearch(page, 'System');
}
