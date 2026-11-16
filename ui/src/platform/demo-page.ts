// What the demo's view of a browser tab is drawn from, apart from its drawing (`demo-frame.tsx`), so the guards that hold
// it read it in Node.

/** The frame, whole: every attribute it carries, and nothing else. */
export const demoFrame = {
    /** Relative to the console's own address, so the page comes from the build the runtime serves. */
    src: 'sealed/demo.html',
    /** The form the page was built and accepted in: scripts alone, never the same origin, forms, pop-ups or the top. */
    sandbox: 'allow-scripts',
    /** No feature delegated. */
    allow: '',
    referrerPolicy: 'no-referrer',
    loading: 'eager',
    title: 'Demo: an example of what you can build on Jason, with sample data',
} as const;

/**
 * Where the page draws its strip: 38 pixels at its top, where the desktop's own switch stands over it too
 * (`desktop/src/window-frame.ts`, `stripHeight`, through `demoGeometry` in `desktop/src/demo-page.ts`); a guard holds
 * the two equal.
 */
export const demoStrip = {
    height: 38,
    /**
     * The narrowest the page is shown at, the desktop's window's own least width (`desktop/src/main.ts`, `minWidth`):
     * below it the page's narrow layout is not one it was built and walked in, so it keeps this width and scrolls.
     */
    narrowest: 900,
} as const;
