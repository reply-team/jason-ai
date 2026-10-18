import { expect, test } from './fixtures';

const policy = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'";

test('the bundle runs under the policy the runtime sends: no refused resource, no page error', async ({
    page,
    jason,
}) => {
    const violations: string[] = [];
    page.on('console', (message) => {
        if (/Content Security Policy|Refused to/.test(message.text())) violations.push(message.text());
    });
    page.on('pageerror', (error) => violations.push(`pageerror: ${error.message}`));

    const { url } = await jason.uiOpen();
    const first = await page.goto(url);
    expect(first?.headers()['content-security-policy']).toBe(policy);
    await expect(page.getByRole('navigation')).toBeVisible();
    await expect(page.getByRole('navigation')).toBeVisible();
    await page.goto(`${jason.origin}/ui/#/system`);
    await expect(page.getByText('Paired browsers')).toBeVisible();
    await expect(page.locator('.sidebar')).toHaveCSS('font-family', /Inter/);

    expect(violations).toEqual([]);
});
