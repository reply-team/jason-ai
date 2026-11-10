; The uninstaller's own part, which electron-builder runs before it removes the installed files, so the application is
; still there to be started. A person's uninstall starts it with --uninstall: it takes off the PATH command's files that
; are its own, "Open Jason at login" and a logon registration of the runtime that starts this application's executable,
; and says what it took and what it leaves - a file another program wrote, the data directory, the harness root and the
; updater's cache under the profile. A silent uninstall (/S) shows nothing. The uninstall an update runs takes off none
; of it, because the new version is installed in the same place at once.

; Which running programs the installer and the uninstaller close before they replace or remove the files: the
; application's own executable, found by its exact path, and nothing else. electron-builder's own check, which this
; replaces, closes every program whose path begins with the installation folder - the runtime this application carries
; among them, with its work - and where PowerShell is not available every program named Jason.exe, which on Windows is
; every jason.exe, a runtime of another installation included. The path is handed to PowerShell in the environment, so
; no character of it is read as PowerShell's. Without PowerShell nothing is closed, and a person is asked to close Jason.
!define JASON_OWN_APP `Get-CimInstance -ClassName Win32_Process | Where-Object { $$_.ExecutablePath -and [string]::Equals($$_.ExecutablePath, $$env:JASON_DESKTOP_APP, [StringComparison]::OrdinalIgnoreCase) }`

!macro customCheckAppRunning
  System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_APP", t "$INSTDIR\${APP_EXECUTABLE_FILENAME}")'
  !insertmacro IS_POWERSHELL_AVAILABLE
  ${if} $IsPowerShellAvailable == 0
    ${if} ${isUpdated}
      ; The application quits itself before an update; this gives it the moment to.
      Sleep 300
    ${endIf}
    nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "if (@(${JASON_OWN_APP}).Count -gt 0) { exit 0 } else { exit 1 }"`
    Pop $R0
    ${if} $R0 == 0
      ${ifNot} ${isUpdated}
        MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$(appRunning)" /SD IDOK IDOK +2
        Quit
      ${endIf}
      DetailPrint "$(appClosing)"
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_OWN_APP} | ForEach-Object { Stop-Process -Id $$_.ProcessId -Force }"`
      Pop $R0
      ; So that the files it held are let go of before they are replaced or removed.
      Sleep 1000
    ${endIf}
  ${else}
    ${ifNot} ${isUpdated}
      MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "Close Jason if it is open, then choose OK." /SD IDOK IDOK +2
      Quit
    ${endIf}
  ${endIf}
!macroend

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
