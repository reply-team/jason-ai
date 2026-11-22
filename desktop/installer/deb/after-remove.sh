#!/bin/bash
# The deb's postrm, run by dpkg as root: `remove` when the package is removed, `purge` when it is purged (after a
# remove), `upgrade` when a newer version replaces it, and the abort cases. electron-builder writes it in place of its own
# template (app-builder-lib 26.18.0, templates/linux/after-remove.tpl), whose two parts are kept as they were: the
# program's link and the AppArmor profile.
#
# Then, on a removal and never on an upgrade, what after-install.sh wrote and what the system wrote for it, so that a
# removed Jason leaves nothing of the system's behind: /usr/bin/jason where it is this package's link, Jason's apt
# source, and the AppArmor cache its profile was compiled into. The key is a file of the package and goes with it. On a
# purge as well, what apt kept of Jason's repository: its lists and the packages it downloaded.

# Delete the link to the binary
# update-alternatives --remove <name> <path>: 'path' must be the registered alternative binary,
# not the generic symlink — see https://man7.org/linux/man-pages/man1/update-alternatives.1.html
if type update-alternatives >/dev/null 2>&1; then
    update-alternatives --remove '${executable}' '/opt/${sanitizedProductName}/${executable}'
else
    rm -f '/usr/bin/${executable}'
fi

APPARMOR_PROFILE_DEST='/etc/apparmor.d/${executable}'

# Remove and unload apparmor profile.
if [ -f "$APPARMOR_PROFILE_DEST" ]; then
  if apparmor_status --enabled > /dev/null 2>&1; then
    if ! { [ -x '/usr/bin/ischroot' ] && /usr/bin/ischroot; } && hash apparmor_parser 2>/dev/null; then
      apparmor_parser --remove "$APPARMOR_PROFILE_DEST" || true
    fi
  fi
  rm -f "$APPARMOR_PROFILE_DEST"
fi

case "$1" in
    remove|purge)
        if [ "$(readlink /usr/bin/jason 2>/dev/null)" = '/opt/${sanitizedProductName}/resources/bundle/jason' ]; then
            rm -f /usr/bin/jason
        fi
        rm -f /etc/apt/sources.list.d/jason-desktop.sources
        rm -f /var/cache/apparmor/*/'${executable}'
        ;;
esac

if [ "$1" = purge ]; then
    rm -f /var/lib/apt/lists/get.jasonharness.ai_apt_* /var/lib/apt/lists/partial/get.jasonharness.ai_apt_*
    rm -f /var/cache/apt/archives/jason-desktop_*.deb /var/cache/apt/archives/partial/jason-desktop_*.deb
fi

exit 0
