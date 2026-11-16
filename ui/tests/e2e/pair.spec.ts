import { expect, quietDesktop, test } from './fixtures';

test('jason ui open pairs a browser once; the ticket leaves the address bar; the used link is harmless; opening again keeps one token', async ({
    page,
    jason,
}) => {
    await page.addInitScript(quietDesktop);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    // Paired, the console is shown: the sidebar, and the area on Start, which every window opens on.
    await expect(page.locator('.jc-console .side')).toBeVisible();
    await expect(page.locator('.jc-console .area > .pane:not([hidden])')).toHaveAttribute('data-pane', 'start');
    await expect(page.getByRole('heading', { name: 'Who do you want to reach?', level: 1 })).toBeVisible();
    // Needs you is a page of the menu. A fresh runtime has nothing waiting, and the page says so once it has read.
    await page
        .locator('.jc-console .side')
        .getByRole('navigation', { name: 'Menu' })
        .getByRole('button', { name: /^Needs you/ })
        .click();
    await expect(page.getByRole('heading', { name: 'Needs you', level: 1 })).toBeVisible();
    await expect(page.getByText("You're all caught up")).toBeVisible();
    expect(page.url()).not.toContain('t=');

    await page.goto(url);
    await expect(page.locator('.jc-console .side')).toBeVisible();
    await expect(page.getByText(/isn't paired/)).toHaveCount(0);

    const again = await jason.uiOpen();
    await page.goto('about:blank');
    await page.goto(again.url);
    await expect(page.locator('.jc-console .side')).toBeVisible();
    await expect.poll(async () => (await jason.uiTokens()).items.length).toBe(1);
});
