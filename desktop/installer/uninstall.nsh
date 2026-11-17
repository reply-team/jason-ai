; The uninstaller's own part, which electron-builder runs before it removes the installed files, so the application is
; still there to be started. A person's uninstall starts it with --uninstall, which runs the one uninstall, the jason the
; application carries as `jason uninstall --keep-application`: it stops the runtime and takes off what Jason and Jason
; Desktop placed outside this folder, keeps the data directory and the harness root, and leaves this folder to the
; uninstaller; the start says what it took and what it leaves. A silent uninstall (/S) shows nothing. `jason uninstall`
; run in a terminal has taken all of that off already when it runs this uninstaller, and says so with
; --uninstalled-by-jason: then the application is not started at all, so nothing of it is written into a data directory
; the terminal's uninstall may just have purged. The uninstall an update runs takes off none of it, because the new
; version is installed in the same place at once. A runtime running from the carried jason.exe is
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
; One process is passed over, and only while it says so: the carried jason that asked for this update with
; jason update apply. It moved its own file out of this folder and goes on running, waiting for the installer, and
; Windows still reports the path it was started from. Right before it starts the application it writes its process id
; and its start, in Windows file time, to jason-update.pid beside the carried executable, and takes the file away once
; the install has ended. The file is read only there, inside this installation's folder, and only by the update's own
; installer and the old version's uninstaller it runs (--updated): a person's install or uninstall reads none, finds that
; jason as any other process and refuses while it runs, so no folder is removed under an update in flight. A process is
; passed over only where both match a process that is running and its command line is that jason's update apply; a file
; left behind names a process that has ended, one written by anything else names no such process, and every other
; process found at that path is looked for as before.
!define JASON_OWN_RUNTIME `& { $$skip = -1; try { $$named = @((Get-Content -LiteralPath $$env:JASON_DESKTOP_UPDATING -TotalCount 1 -ErrorAction Stop) -split ' '); $$found = Get-Process -Id ([int]$$named[0]) -ErrorAction Stop; $$asked = Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId=' + $$found.Id); if ($$found.StartTime.ToFileTimeUtc() -eq [long]$$named[1] -and $$asked.CommandLine.Contains(' update apply')) { $$skip = $$found.Id } } catch { }; Get-CimInstance -ClassName Win32_Process | Where-Object { $$_.ExecutablePath -and [string]::Equals($$_.ExecutablePath, $$env:JASON_DESKTOP_RUNTIME, [StringComparison]::OrdinalIgnoreCase) -and $$_.ProcessId -ne $$skip } }`

!define JASON_RUNTIME_ASK "A Jason runtime is running from this application's folder. Choose OK to stop it and go on."
!define JASON_RUNTIME_STILL "A Jason process from this application's folder is still running, so nothing was changed. If it is a runtime that did not stop, stop it with $INSTDIR\resources\bundle\jason.exe runtime stop, with JASON_DATA_DIR set if it was started with another data directory; other Jason work from this folder ends by itself. Then run this again."

!macro customCheckAppRunning
  System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_APP", t "$INSTDIR\${APP_EXECUTABLE_FILENAME}")'
  System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_RUNTIME", t "$INSTDIR\resources\bundle\jason.exe")'
  ${if} ${isUpdated}
    System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_UPDATING", t "$INSTDIR\resources\bundle\jason-update.pid")'
  ${else}
    ; Nothing is passed over, a value this run was started with included.
    System::Call 'Kernel32::SetEnvironmentVariable(t "JASON_DESKTOP_UPDATING", i 0)'
  ${endIf}
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
    ${GetOptions} $R0 "--uninstalled-by-jason" $R1
    ${if} ${Errors}
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
  ${endIf}
!macroend
