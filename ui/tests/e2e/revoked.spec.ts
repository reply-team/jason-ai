import { expect, test } from './fixtures';

test('a revoked token lands on the pair-again screen and the stored pairing is cleared', async ({ page, jason }) => {
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.getByRole('navigation')).toBeVisible();

    await jason.uiRevoke('--all');
    await page.reload();
    await expect(page.getByText(/isn't paired/)).toBeVisible();
    expect(await page.evaluate(() => localStorage.getItem('jason.ui.session'))).toBeNull();
});
