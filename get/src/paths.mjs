// The paths this handler answers, and the fixed addresses behind them. Nothing a request carries ever chooses an
// address: each one is a literal here.

/**
 * The one address the install page is read from: docs/INSTALL.md of main, which moves only when a release is
 * published. Its relative links are made whole against this same address.
 */
export const INSTALL_PAGE = 'https://raw.githubusercontent.com/reply-team/jason-ai/main/docs/INSTALL.md';

/**
 * The guide's first heading. A body read from the fixed address is served only when it begins with this line, so what
 * the address serves is the guide or nothing; a test of the runtime's suite holds it to docs/INSTALL.md's first line.
 */
export const INSTALL_HEADING = '# Installing Jason';

/** The largest body served as the guide, in bytes. The guide is a few tens of kilobytes. */
export const INSTALL_MAX_BYTES = 256 * 1024;

export const INSTALL_PATH = '/install';

export const STATS_PATH = '/stats';

/**
 * Each desktop path and the release file it redirects to, as a whole address under the product's releases, which the
 * existing redirect sends on to the latest release on GitHub. The file names are the release's own, and a test of the
 * runtime's suite holds them to the list the release publishes, both ways.
 */
export const DESKTOP = Object.freeze({
  '/desktop/windows': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-windows-x64-setup.exe',
  '/desktop/mac': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-macos-arm64.dmg',
  '/desktop/mac-intel': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-macos-x64.dmg',
  '/desktop/linux-deb': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-linux-x64.deb',
  '/desktop/linux-appimage': 'https://get.jasonharness.ai/releases/latest/download/jason-desktop-linux-x64.AppImage',
});
