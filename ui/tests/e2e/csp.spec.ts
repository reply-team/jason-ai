import { expect, openSystem, test } from './fixtures';

const policy = "default-src 'self'; connect-src 'self' http://127.0.0.1:*; frame-ancestors 'none'; base-uri 'none'";

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
    await expect(page.locator('.jc-console .side')).toBeVisible();
    await openSystem(page);
    await expect(page.getByText('Paired browsers')).toBeVisible();
    await expect(page.locator('.jc-console')).toHaveCSS('font-family', /Inter/);

    expect(violations).toEqual([]);
});
