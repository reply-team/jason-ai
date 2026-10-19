import { expect, test } from './fixtures';

test('every request the page makes is an operation, the door, or a file under /ui/', async ({ page, jason }) => {
    const seen: string[] = [];
    page.on('request', (request) => seen.push(`${request.method()} ${request.url()}`));

    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.getByRole('navigation')).toBeVisible();
    await page.getByTitle('Refresh').click();
    await page.goto(`${jason.origin}/ui/#/system`);
    await expect(page.getByText('Paired browsers')).toBeVisible();
    await page.getByTitle('Refresh').click();
    await page.waitForLoadState('networkidle');

    const origin = jason.origin.replace(/\./g, '\\.');
    const allowed = [
        new RegExp(`^POST ${origin}/v1/[a-z]+\\.[a-z_]+$`),
        new RegExp(`^POST ${origin}/ui/session$`),
        new RegExp(`^GET ${origin}/ui/`),
    ];
    const strangers = seen.filter((line) => !allowed.some((rule) => rule.test(line)));
    expect(strangers).toEqual([]);
    expect(seen.some((line) => line.startsWith(`POST ${jason.origin}/v1/approval.list`))).toBe(true);
    expect(seen.some((line) => line.startsWith(`POST ${jason.origin}/v1/ui.tokens`))).toBe(true);
});
