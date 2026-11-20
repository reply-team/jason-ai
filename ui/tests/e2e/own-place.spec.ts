import { expect, test } from './fixtures';

// A plain browser tab at the runtime's `/ui/`, over the release's build and with no web interface of the person's own
// installed: one sentence and nothing else, no link, no Demo and no console, at a wide window and at a phone's width. It
// asks the runtime nothing.
for (const size of [
    { name: 'wide', width: 1280, height: 800 },
    { name: 'narrow', width: 390, height: 844 },
]) {
    test(`a plain tab shows one message and nothing else, ${size.name}`, async ({ page, jason }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        const problems: string[] = [];
        const asked: string[] = [];
        page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`));
        page.on('console', (message) => {
            if (message.type() === 'error' || /Content Security Policy|Refused to/.test(message.text()))
                problems.push(`${message.type()}: ${message.text()}`);
        });
        page.on('request', (request) => asked.push(new URL(request.url()).pathname));

        await page.goto(`${jason.origin}/ui/#/attention/approvals/apr_00000000000000000000000000`);

        const message = page.locator('#app > p.own-place');
        await expect(message).toHaveText(
            'This is the place for a web interface of your own. To build one, ask your agent how.',
        );
        await expect(page.locator('#app')).toHaveText(await message.innerText());
        for (const selector of ['a', 'button', 'input', 'iframe', 'nav', '.jc-console'])
            await expect(page.locator(selector), selector).toHaveCount(0);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        expect(asked.filter((path) => !path.startsWith('/ui/'))).toEqual([]);
        expect(problems).toEqual([]);
    });
}
