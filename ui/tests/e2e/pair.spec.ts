import { expect, test } from './fixtures';

test('jason ui open pairs a browser once; the ticket leaves the address bar; the used link is harmless; opening again keeps one token', async ({
    page,
    jason,
}) => {
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.getByRole('navigation')).toBeVisible();
    await expect(page.getByText('Nothing needs you')).toBeVisible();
    expect(page.url()).not.toContain('t=');

    await page.goto(url);
    await expect(page.getByRole('navigation')).toBeVisible();
    await expect(page.getByText(/isn't paired/)).toHaveCount(0);

    const again = await jason.uiOpen();
    await page.goto('about:blank');
    await page.goto(again.url);
    await expect(page.getByRole('navigation')).toBeVisible();
    await expect.poll(async () => (await jason.uiTokens()).items.length).toBe(1);
});
