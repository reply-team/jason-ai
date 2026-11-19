#!/bin/bash
# The deb's postinst, run by dpkg as root after the package is unpacked: `configure`, with the version it replaces when it
# updates one. electron-builder writes it in place of its own template (app-builder-lib 26.18.0,
# templates/linux/after-install.tpl), whose four parts are kept as they were: the program's link, the sandbox helper's
# mode, the menu's databases and the AppArmor profile. electron-builder fills in the two names between ${ and } before
# the deb is made, so no other name of this script is written that way.
#
# Then what makes Jason one installation here, as comparable desktop applications do from their own package (Chrome,
# Visual Studio Code, Claude Desktop): jason in every terminal, /usr/bin/jason, a link to the jason this package
# carries; and Jason's apt repository, /etc/apt/sources.list.d/jason-desktop.sources, signed by the key this package
# carries as /usr/share/keyrings/jason-desktop.gpg, so the system's updater offers later updates, installed when the
# person says so. after-remove.sh takes back what this writes.

if type update-alternatives >/dev/null 2>&1; then
    # Remove previous link if it doesn't use update-alternatives
    if [ -L '/usr/bin/${executable}' -a -e '/usr/bin/${executable}' -a "`readlink '/usr/bin/${executable}'`" != '/etc/alternatives/${executable}' ]; then
        rm -f '/usr/bin/${executable}'
    fi
    update-alternatives --install '/usr/bin/${executable}' '${executable}' '/opt/${sanitizedProductName}/${executable}' 100 || ln -sf '/opt/${sanitizedProductName}/${executable}' '/usr/bin/${executable}'
else
    ln -sf '/opt/${sanitizedProductName}/${executable}' '/usr/bin/${executable}'
fi

# Check if user namespaces are supported by the kernel and working with a quick test:
if ! { [[ -L /proc/self/ns/user ]] && unshare --user true; }; then
    # Use SUID chrome-sandbox only on systems without user namespaces:
    chmod 4755 '/opt/${sanitizedProductName}/chrome-sandbox' || true
else
    chmod 0755 '/opt/${sanitizedProductName}/chrome-sandbox' || true
fi

if hash update-mime-database 2>/dev/null; then
    update-mime-database /usr/share/mime || true
fi

if hash update-desktop-database 2>/dev/null; then
    update-desktop-database /usr/share/applications || true
fi

# The AppArmor profile that lets the program make the user namespaces its sandbox uses (Ubuntu 24.04 and later), where
# this system's AppArmor takes its abi 4.0, as electron-builder's template installs it.
if apparmor_status --enabled > /dev/null 2>&1; then
  APPARMOR_PROFILE_SOURCE='/opt/${sanitizedProductName}/resources/apparmor-profile'
  APPARMOR_PROFILE_TARGET='/etc/apparmor.d/${executable}'
  if apparmor_parser --skip-kernel-load --debug "$APPARMOR_PROFILE_SOURCE" > /dev/null 2>&1; then
    cp -f "$APPARMOR_PROFILE_SOURCE" "$APPARMOR_PROFILE_TARGET"
    if ! { [ -x '/usr/bin/ischroot' ] && /usr/bin/ischroot; } && hash apparmor_parser 2>/dev/null; then
      apparmor_parser --replace --write-cache --skip-read-cache "$APPARMOR_PROFILE_TARGET"
    fi
  else
    echo "Skipping the installation of the AppArmor profile as this version of AppArmor does not seem to support the bundled profile"
  fi
fi

# jason in every terminal of every account. Only where the name is free or already this link: a /usr/bin/jason of
# another program is left as it is, and said.
JASON='/opt/${sanitizedProductName}/resources/bundle/jason'
if [ "$(readlink /usr/bin/jason 2>/dev/null)" = "$JASON" ] || { [ ! -e /usr/bin/jason ] && [ ! -L /usr/bin/jason ]; }; then
    ln -sfn "$JASON" /usr/bin/jason
else
    echo "/usr/bin/jason is another program's and is left as it is: Jason's jason is $JASON."
fi

# Jason's apt repository: the latest release's files, through the address's /apt/ path, read flat, and signed by the
# key this package carries alone.
cat > /etc/apt/sources.list.d/jason-desktop.sources <<'SOURCE'
Types: deb
URIs: https://get.jasonharness.ai/apt/
Suites: ./
Signed-By: /usr/share/keyrings/jason-desktop.gpg
SOURCE
chmod 644 /etc/apt/sources.list.d/jason-desktop.sources
