; The uninstaller's own part, which electron-builder runs before it removes the installed files, so the application is
; still there to be started. A person's uninstall starts it with --uninstall: it takes off the PATH command's files that
; are its own, "Open Jason at login" and a logon registration of the runtime that starts this application's executable,
; and says what it took and what it leaves - a file another program wrote, the data directory, the harness root and the
; updater's cache under the profile. A silent uninstall (/S) shows nothing. The uninstall an update runs takes off none
; of it, because the new version is installed in the same place at once. A runtime running from the carried jason.exe is
; stopped before any of this, by the check below, which the installer and the uninstaller both run first; the
; --uninstall start stops the runtime of its own data directory again, in case one was started in between.

; Which running programs the installer and the uninstaller close before they replace or remove the files: the
; application's own executable, found by its exact path, and nothing else. electron-builder's own check, which this
; replaces, closes every program whose path begins with the installation folder - the runtime this application carries
; among them, with its work - and where PowerShell is not available every program named Jason.exe, which on Windows is
; every jason.exe, a runtime of another installation included. The path is handed to PowerShell in the environment, so
; no character of it is read as PowerShell's. Without PowerShell nothing is closed, and a person is asked to close Jason.
!define JASON_OWN_APP `Get-CimInstance -ClassName Win32_Process | Where-Object { $$_.ExecutablePath -and [string]::Equals($$_.ExecutablePath, $$env:JASON_DESKTOP_APP, [StringComparison]::OrdinalIgnoreCase) }`

; A runtime started from the jason executable this application carries, by whichever data directory or run file, is
; looked for by that executable's exact path in the same way, and never closed: it may hold work. It is stopped through
; the application's own start, --stop-runtime, which runs jason runtime stop for the data directory this run was given
; and exits; the look after it decides, never the start's exit code. At worst that start waits out jason runtime status
; twice, 30 seconds each, and jason runtime stop once, 60 seconds. A person is asked first, before the application is
; closed, so a Cancel leaves Jason as it was; an update and a silent run are not asked. A process still found then - a
; runtime that did not stop, one started with another data directory say, or other Jason work from this folder - ends
; the run before a file is replaced, with error level 1, and a person is told to stop a runtime with the carried
; executable by its full path, since the PATH command may set a data directory of its own. Without PowerShell nothing
; can be looked for: a person is asked to stop it, and an update goes on without a look.
!define JASON_OWN_RUNTIME `Get-CimInstance -ClassName Win32_Process | Where-Object { $$_.ExecutablePath -and [string]::Equals($$_.ExecutablePath, $$env:JASON_DESKTOP_RUNTIME, [StringComparison]::OrdinalIgnoreCase) }`

!define JASON_RUNTIME_ASK "A Jason runtime is running from this application's folder. Choose OK to stop it and go on."
!define JASON_RUNTIME_STILL "A Jason process from this application's folder is still running, so nothing was changed. If it is a runtime that did not stop, stop it with $INSTDIR\resources\bundle\jason.exe runtime stop, with JASON_DATA_DIR set if it was started with another data directory; other Jason work from this folder ends by itself. Then run this again."

!macro customCheckAppRunning
  System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_APP", t "$INSTDIR\${APP_EXECUTABLE_FILENAME}")'
  System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_RUNTIME", t "$INSTDIR\resources\bundle\jason.exe")'
  !insertmacro IS_POWERSHELL_AVAILABLE
  ${if} $IsPowerShellAvailable == 0
    Var /GLOBAL JasonRuntimeAsked
    StrCpy $JasonRuntimeAsked ""
    ${ifNot} ${isUpdated}
    ${AndIfNot} ${Silent}
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "if (@(${JASON_OWN_RUNTIME}).Count -gt 0) { exit 0 } else { exit 1 }"`
      Pop $R0
      ${if} $R0 == 0
        MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "${JASON_RUNTIME_ASK}" IDOK +3
        SetErrorLevel 1
        Quit
        StrCpy $JasonRuntimeAsked "asked"
      ${endIf}
    ${endIf}
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
    nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "if (@(${JASON_OWN_RUNTIME}).Count -gt 0) { exit 0 } else { exit 1 }"`
    Pop $R0
    ${if} $R0 == 0
      ${ifNot} ${isUpdated}
      ${AndIfNot} ${Silent}
      ${AndIf} $JasonRuntimeAsked == ""
        MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "${JASON_RUNTIME_ASK}" IDOK +3
        SetErrorLevel 1
        Quit
      ${endIf}
      ExecWait '"$INSTDIR\${APP_EXECUTABLE_FILENAME}" --stop-runtime'
      ; So that the process has gone from the list before it is looked for again.
      Sleep 1000
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "if (@(${JASON_OWN_RUNTIME}).Count -gt 0) { exit 0 } else { exit 1 }"`
      Pop $R0
      ${if} $R0 == 0
        ${ifNot} ${isUpdated}
        ${AndIfNot} ${Silent}
          MessageBox MB_OK|MB_ICONSTOP "${JASON_RUNTIME_STILL}"
        ${endIf}
        SetErrorLevel 1
        Quit
      ${endIf}
    ${endIf}
  ${else}
    ${ifNot} ${isUpdated}
      MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "Close Jason if it is open, and stop its runtime with jason runtime stop if it runs, then choose OK." /SD IDOK IDOK +2
      Quit
    ${endIf}
  ${endIf}
!macroend

; The start's exit code says whether to go on: 1 when the runtime this application carries did not stop, or a runtime
; did not answer, which the start has already said unless the uninstall is silent. The uninstaller then quits before it
; removes a file, so no folder is left half removed. Any other code - a start that crashed, or one that could not be
; made at all - never blocks the uninstall: it goes on, and a person is told that what the application placed outside
; its folder may be left.
!macro customUnInstall
  ${ifNot} ${isUpdated}
    ${GetParameters} $R0
    ClearErrors
    ${GetOptions} $R0 "/S" $R1
    ${if} ${Errors}
      StrCpy $R3 ""
      StrCpy $R2 "0"
      ClearErrors
      ExecWait '"$INSTDIR\${APP_EXECUTABLE_FILENAME}" --uninstall' $R2
    ${else}
      StrCpy $R3 "quiet"
      StrCpy $R2 "0"
      ClearErrors
      ExecWait '"$INSTDIR\${APP_EXECUTABLE_FILENAME}" --uninstall --quiet' $R2
    ${endIf}
    ${if} ${Errors}
      StrCpy $R2 "none"
    ${endIf}
    ${if} $R2 == "1"
      SetErrorLevel 1
      Quit
    ${endIf}
    ${if} $R2 != "0"
    ${andIf} $R3 == ""
      MessageBox MB_OK|MB_ICONEXCLAMATION "Jason could not take off what it placed outside its folder, so the jason command on the PATH, Open Jason at login and the runtime's start at logon may be left. The uninstall goes on."
    ${endIf}
  ${endIf}
!macroend
