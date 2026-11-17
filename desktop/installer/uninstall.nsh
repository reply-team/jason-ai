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

; The look itself fails closed: a query the system failed to answer, which a machine under load meets, is an error that
; stops the look rather than a list that came back empty. It is asked again, five times in all half a second apart, and
; where it never answered the look exits 2. Only exit 1 says nothing runs from the carried executable: every other exit,
; a failed start of PowerShell included, is read as a process found.
!define JASON_RUNTIME_FOUND `$$ErrorActionPreference = 'Stop'; for ($$look = 1; ; $$look++) { try { if (@(${JASON_OWN_RUNTIME}).Count -gt 0) { exit 0 } else { exit 1 } } catch { if ($$look -ge 5) { exit 2 }; Start-Sleep -Milliseconds 500 } }`

; The application is looked for the same way, failing closed: only exit 1 says it is not running. It is looked for again
; after it was closed, and a run where it is still found, or where that look failed, ends with error level 1 before a file
; is replaced.
!define JASON_APP_FOUND `$$ErrorActionPreference = 'Stop'; for ($$look = 1; ; $$look++) { try { if (@(${JASON_OWN_APP}).Count -gt 0) { exit 0 } else { exit 1 } } catch { if ($$look -ge 5) { exit 2 }; Start-Sleep -Milliseconds 500 } }`

; An update closes nothing it has not asked about: the application quits itself before an update, and the update waits
; for it, looking twice a second, until no process runs from its executable or a minute has passed - three times the
; twenty seconds late a slow quit is proven at, and well inside the ten minutes jason update apply gives the installer.
; The wait answers as the look does: exit 1 as soon as nothing is found, and at the bound 0 where the application is still
; found or 2 where the last look failed, both read as found. The old version's uninstaller the update then runs closes
; any application it finds, and refuses with 2 while a file of the folder is held, so it is never run while one is found.
!define JASON_APP_WAIT_SECONDS 60
!define JASON_APP_GONE `$$ErrorActionPreference = 'Stop'; $$clock = [Diagnostics.Stopwatch]::StartNew(); while ($$true) { try { if (@(${JASON_OWN_APP}).Count -eq 0) { exit 1 }; $$failed = $$false } catch { $$failed = $$true }; if ($$clock.Elapsed.TotalSeconds -ge ${JASON_APP_WAIT_SECONDS}) { if ($$failed) { exit 2 }; exit 0 }; Start-Sleep -Milliseconds 500 }`

; What a person is told: exit 0 is a process found; any other exit but 1 is a look that could not be made, said as such.
!define JASON_RUNTIME_ASK "A Jason runtime is running from this application's folder. Choose OK to stop it and go on."
!define JASON_LOOK_ASK "Jason could not look for its own processes, so it cannot tell whether a Jason runtime is running from this application's folder. Choose OK to stop it if it is, and go on."
!define JASON_APP_UNSURE "Jason could not look for its own processes, so it cannot tell whether Jason is open. Choose OK to close it if it is, and go on."
!define JASON_APP_STILL "Jason is still open, so nothing was changed. Close it, then run this again."
!define JASON_RUNTIME_STILL "A Jason process from this application's folder is still running, so nothing was changed. If it is a runtime that did not stop, stop it with $INSTDIR\resources\bundle\jason.exe runtime stop, with JASON_DATA_DIR set if it was started with another data directory; other Jason work from this folder ends by itself. Then run this again."
!define JASON_LOOK_FAILED "Jason could not look for its own processes, so it cannot tell whether Jason is running from this application's folder, and nothing was changed. Run this again."

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
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_RUNTIME_FOUND}"`
      Pop $R0
      ${if} $R0 != 1
        ${if} $R0 S== 0
          StrCpy $R1 "${JASON_RUNTIME_ASK}"
        ${else}
          StrCpy $R1 "${JASON_LOOK_ASK}"
        ${endIf}
        MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$R1" IDOK +3
        SetErrorLevel 1
        Quit
        StrCpy $JasonRuntimeAsked "asked"
      ${endIf}
    ${endIf}
    ${if} ${isUpdated}
      ; The application quits itself before an update; it is given the bound above to.
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_APP_GONE}"`
    ${else}
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_APP_FOUND}"`
    ${endIf}
    Pop $R0
    ${if} $R0 != 1
      ; A silent update closes nothing and shows nothing: it ends before a file is replaced, and the old version stays
      ; installed as it was. An update a person watches, the window's own install, asks them as an install does.
      ${if} ${isUpdated}
      ${AndIf} ${Silent}
        SetErrorLevel 1
        Quit
      ${endIf}
      ${if} $R0 S== 0
        StrCpy $R1 "$(appRunning)"
      ${else}
        StrCpy $R1 "${JASON_APP_UNSURE}"
      ${endIf}
      MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$R1" /SD IDOK IDOK +2
      Quit
      DetailPrint "$(appClosing)"
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_OWN_APP} | ForEach-Object { Stop-Process -Id $$_.ProcessId -Force }"`
      Pop $R0
      ; So that the files it held are let go of before they are replaced or removed.
      Sleep 1000
      ; The look decides, never the stop's exit code.
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_APP_FOUND}"`
      Pop $R0
      ${if} $R0 != 1
        ${ifNot} ${Silent}
          ${if} $R0 S== 0
            StrCpy $R1 "${JASON_APP_STILL}"
          ${else}
            StrCpy $R1 "${JASON_LOOK_FAILED}"
          ${endIf}
          MessageBox MB_OK|MB_ICONSTOP "$R1" /SD IDOK
        ${endIf}
        SetErrorLevel 1
        Quit
      ${endIf}
    ${endIf}
    nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_RUNTIME_FOUND}"`
    Pop $R0
    ${if} $R0 != 1
      ${ifNot} ${isUpdated}
      ${AndIfNot} ${Silent}
      ${AndIf} $JasonRuntimeAsked == ""
        ${if} $R0 S== 0
          StrCpy $R1 "${JASON_RUNTIME_ASK}"
        ${else}
          StrCpy $R1 "${JASON_LOOK_ASK}"
        ${endIf}
        MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "$R1" IDOK +3
        SetErrorLevel 1
        Quit
      ${endIf}
      ExecWait '"$INSTDIR\${APP_EXECUTABLE_FILENAME}" --stop-runtime'
      ; So that the process has gone from the list before it is looked for again.
      Sleep 1000
      nsExec::Exec `"$PowerShellPath" -NoProfile -NonInteractive -Command "${JASON_RUNTIME_FOUND}"`
      Pop $R0
      ${if} $R0 != 1
        ${ifNot} ${isUpdated}
        ${AndIfNot} ${Silent}
          ${if} $R0 S== 0
            StrCpy $R1 "${JASON_RUNTIME_STILL}"
          ${else}
            StrCpy $R1 "${JASON_LOOK_FAILED}"
          ${endIf}
          MessageBox MB_OK|MB_ICONSTOP "$R1" /SD IDOK
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

; What an update does with the old version's uninstaller's answer, which electron-builder hands to these two hooks, for
; the shell context and for the current user, in place of its own handling (installUtil.nsh, handleUninstallResult). Its
; own shows a box with no silent answer when the old uninstaller refuses - and one refuses with 2 while a file of the
; folder is held, a moment after it put back what it had moved - so a silent update would wait for a click no one gives,
; holding the installer's mutex against every later setup. Here it is the same: the same sentence to a person who
; watches, the same lines of details, the same error level 2 before a file is replaced, and the old version left
; installed as its uninstaller put it back; a silent run is shown nothing and only ends. An old uninstaller that could not
; be started at all is said, and the install goes on over its files, as electron-builder's own does.
!macro jasonOldUninstallerResult
  ${if} ${Errors}
    DetailPrint `Uninstall was not successful. Not able to launch uninstaller!`
  ${elseIf} $R0 != 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(uninstallFailed): $R0" /SD IDOK
    DetailPrint `Uninstall was not successful. Uninstaller error code: $R0.`
    SetErrorLevel 2
    Quit
  ${endIf}
!macroend

!macro customUnInstallCheck
  !insertmacro jasonOldUninstallerResult
!macroend

!macro customUnInstallCheckCurrentUser
  !insertmacro jasonOldUninstallerResult
!macroend

; The installer's own last step, once the files are in place, at every install and every update, silent or not: the
; machine set up for the jason this application carries, through that jason's own `jason install` with no flags. It puts
; jason in the terminal (this account's Path names the carried file's folder), gives the skills of every agent host found
; and retires an older command-line install; it writes nothing under the data directory, so a person's first start still
; opens on the Demo. nsExec runs it with no window and waits for it, and it starts no process that outlives it, so a script
; that waits for this installer's whole process tree is not held. Nothing here starts a runtime: a runtime started here
; would be this installer's child and outlive it, and such a script would wait for it for ever. Where a runtime answered
; before the install and none answers after it - the check above stopped the application's, or the routine one of a
; command-line install it retired - whoever ran this installer starts one again from the carried file: a person's run
; and the window's own update (--force-run) start the application once this step is done, and its start brings the
; runtime up as every start of it does; a run a terminal asked for - the install script's silent run, or that of
; jason update apply - is that command's, which starts one itself. None of them is relied on alone: where the routine
; stopped a command-line install's runtime it left the note that a start is owed, run/start-owed.json in the data
; directory, which stands until a runtime starts there - through a caller that quit or relaunched first, a start that
; opened in the Demo, or a routine that was killed - and the application, at every start of its own, Demo or Live,
; starts a runtime from the carried file when it finds the note standing and none answering; jason status names it
; meanwhile. What it did not get done never fails the
; install, whose files are in place by now: the install scripts run it again and read its report, and the application's
; start runs it where its last run was for an older version. A run a person watches says so in a box, since a one-click
; installer shows no details; a silent run answers the box itself and shows nothing.
!macro customInstall
  DetailPrint "Setting this machine up for Jason: the jason command in the terminal and the skills of every agent host found."
  nsExec::Exec '"$INSTDIR\resources\bundle\jason.exe" install'
  Pop $R0
  ${if} $R0 S!= 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "jason install did not set everything up (it answered $R0). Run $INSTDIR\resources\bundle\jason.exe install in a terminal to see what it says." /SD IDOK
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

