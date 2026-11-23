import { expect, talkingDesktop, test } from './fixtures';

// Where Copy code is drawn over a code block, which the screen tests cannot see, since their page has no layout: a real
// engine lays out an answer with a block of one line and a block of five, as the desktop streams it. On the one-line
// block the button stands in the block's vertical middle; on the taller one it keeps the same place, at the top right,
// and does not slide to the middle.
test('Copy code is centred on a block of one line and stays at the top right of a taller one', async ({
    page,
    jason,
}) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.addInitScript(talkingDesktop);
    const { url } = await jason.uiPair();
    await page.goto(url);

    const start = page.getByRole('form', { name: 'Start' });
    await start.getByRole('textbox', { name: 'Who do you want to reach?' }).fill('Show me two commands');
    await start.getByRole('button', { name: 'Send' }).click();
    await page.waitForFunction(() => (window as unknown as { sentTo: string[] }).sentTo.length === 1);
    const id = await page.evaluate(() => (window as unknown as { sentTo: string[] }).sentTo[0]!);
    const answer = [
        'One line:',
        '',
        '```',
        'jason runtime status',
        '```',
        '',
        'Five lines:',
        '',
        '```',
        'jason runtime status',
        'jason campaign list',
        'jason workitem list',
        'jason approval list',
        'jason journal watch',
        '```',
    ].join('\n');
    for (const event of [
        { kind: 'text', text: answer },
        { kind: 'done', error: null },
    ]) {
        await page.evaluate(
            ([chat, said]) =>
                (window as unknown as { chatEvent: (id: string, event: unknown) => void }).chatEvent(chat!, said),
            [id, event] as const,
        );
    }
    const blocks = page.locator('.jc-console .area > .pane:not([hidden]) .chat-md-code');
    await expect(blocks).toHaveCount(2);
    await expect(blocks.first().getByRole('button', { name: 'Copy code' })).toBeVisible();

    const placed = (index: number) =>
        blocks.nth(index).evaluate((block) => {
            const pre = block.querySelector('pre')!.getBoundingClientRect();
            const button = block.querySelector('button')!.getBoundingClientRect();
            return {
                fromTop: button.top - pre.top,
                fromMiddle: button.top + button.height / 2 - (pre.top + pre.height / 2),
                fromRight: pre.right - button.right,
                height: pre.height,
            };
        });
    const one = await placed(0);
    const five = await placed(1);
    expect(five.height, 'the second block is the taller').toBeGreaterThan(one.height * 2);
    expect(Math.abs(one.fromMiddle), `one line: ${JSON.stringify(one)}`).toBeLessThanOrEqual(0.5);
    expect(Math.abs(five.fromTop - one.fromTop), `five lines: ${JSON.stringify(five)}`).toBeLessThanOrEqual(0.5);
    expect(five.fromMiddle, 'five lines: above the middle').toBeLessThan(-five.height / 4);
    expect(Math.abs(five.fromRight - one.fromRight), 'the same right edge').toBeLessThanOrEqual(0.5);
});
