import { expect, quietDesktop, test } from './fixtures';

// Back and the title "Settings" are one line of the head, read along one baseline: the screen tests have no layout to
// see where Back is drawn, so this row has a real engine draw the head, in a wide window and in a narrow one, where the
// console pads its content less. A baseline is where a marker of no height that stands on it sits; Back's words are in a
// row of their own with the chevron, where no marker stands on a baseline, so theirs is found from where the words' own
// box is: the share of a box's height above its baseline is the same for every size of one font, and the title, drawn
// in the same family as Back's words, says what that share is.
for (const width of [2000, 800]) {
    test(`Settings' Back stands on the baseline of the title, in a window ${width} pixels wide`, async ({
        page,
        jason,
    }) => {
        await page.setViewportSize({ width, height: 800 });
        await page.addInitScript(quietDesktop);
        const { url } = await jason.uiOpen();
        await page.goto(url);
        await expect(page.locator('.jc-console .area')).toBeVisible();
        await page.waitForFunction(
            () => typeof (window as unknown as { menuCommand?: unknown }).menuCommand === 'function',
        );
        await page.evaluate(() =>
            (window as unknown as { menuCommand: (name: string) => void }).menuCommand('navigate.settings'),
        );
        const settings = page.locator('.harness .settings');
        await expect(settings.getByRole('heading', { name: 'Settings', level: 1 })).toBeVisible();
        await expect(settings.getByRole('button', { name: 'Back' })).toBeVisible();
        await page.evaluate(() => document.fonts.ready);
        const lines = await settings.evaluate((root) => {
            const words = (element: Element) => {
                const text = Array.from(element.childNodes).find(
                    (node) => node.nodeType === Node.TEXT_NODE && node.textContent!.trim() !== '',
                )!;
                const range = document.createRange();
                range.selectNodeContents(text);
                return range.getBoundingClientRect();
            };
            const title = root.querySelector(':scope > h1')!;
            const marker = document.createElement('span');
            marker.style.display = 'inline-block';
            marker.style.width = '0';
            marker.style.height = '0';
            marker.style.verticalAlign = 'baseline';
            title.append(marker);
            const titleBaseline = marker.getBoundingClientRect().bottom;
            marker.remove();
            const titleWords = words(title);
            const share = (titleBaseline - titleWords.top) / titleWords.height;
            const button = root.querySelector(':scope > .settings-back > button')!;
            const back = words(button);
            return {
                title: titleBaseline,
                back: back.top + back.height * share,
                fonts: [getComputedStyle(title).fontFamily, getComputedStyle(button).fontFamily],
            };
        });
        // The share holds for one font: the two must be drawn in the same family for the measure to mean anything.
        expect(lines.fonts[1], "Back is in the title's font").toBe(lines.fonts[0]);
        expect(Math.abs(lines.back - lines.title), `Back ${lines.back}, the title ${lines.title}`).toBeLessThanOrEqual(
            0.5,
        );
    });
}
