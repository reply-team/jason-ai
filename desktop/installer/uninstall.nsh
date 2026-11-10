; The uninstaller's own part, which electron-builder runs before it removes the installed files, so the application is
; still there to be started. A person's uninstall starts it with --uninstall: it takes off the PATH command's files that
; are its own, "Open Jason at login" and a logon registration of the runtime that starts this application's executable,
; and says what it took and what it leaves - a file another program wrote, the data directory, the harness root and the
; updater's cache under the profile. A silent uninstall (/S) shows nothing. The uninstall an update runs takes off none
; of it, because the new version is installed in the same place at once.

!macro customUnInstall
  ${ifNot} ${isUpdated}
    ${GetParameters} $R0
    ClearErrors
    ${GetOptions} $R0 "/S" $R1
    ${if} ${Errors}
      ExecWait '"$INSTDIR\${APP_EXECUTABLE_FILENAME}" --uninstall'
    ${else}
      ExecWait '"$INSTDIR\${APP_EXECUTABLE_FILENAME}" --uninstall --quiet'
    ${endIf}
  ${endIf}
!macroend
