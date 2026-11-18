// The paths this handler answers, and the fixed addresses behind them. Nothing a request carries ever chooses an
// address: each one is a literal here, and the one part that varies, the latest release's tag, is held to a release
// tag's shape before it is written into one.

/**
 * The fixed address of the latest published release. It answers with a redirect to that release's page, whose last part
 * is the release's tag; that redirect is read and never followed. GitHub's latest release is never a pre-release.
 */
export const LATEST_RELEASE = 'https://github.com/reply-team/jason-ai/releases/latest';

/**
 * The one shape of a location the latest release's redirect is believed in: this repository's release page of a tag
 * that is `v` and three numbers, nothing before and nothing after.
 */
export const LATEST_RELEASE_LOCATION = /^https:\/\/github\.com\/reply-team\/jason-ai\/releases\/tag\/(v(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8}))$/;

const RELEASE_TAG = /^v(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})$/;

/**
 * The install page as the release of this tag published it: docs/INSTALL.md at the tag. A tag of any other shape names no
 * address. Its relative links are made whole against this same address, so they name that release's files.
 */
export function installPageAt(tag) {
  if (typeof tag !== 'string' || !RELEASE_TAG.test(tag)) throw new Error(`'${tag}' is not a release tag.`);
  return `https://raw.githubusercontent.com/reply-team/jason-ai/${tag}/docs/INSTALL.md`;
}

/**
 * The install page of main: read where the latest release's tag cannot be learnt or is not a release tag, or where the
 * page at that tag is not the guide, and the address a request is redirected to when neither read gives the guide. Main
 * moves when a release pull request is merged, which may come before or after that release is published.
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

/**
 * The header an answer of the install page names its source in: the release tag the guide was read at, or `main`. A
 * redirect carries none.
 */
export const INSTALL_SOURCE_HEADER = 'x-jason-install-source';
