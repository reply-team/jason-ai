<#
.SYNOPSIS
Refuses a packaged Windows executable whose application manifest lacks consoleAllocationPolicy "detached" or
asInvoker. Read from the file that is about to be archived, not from the project that built it.

.DESCRIPTION
The runtime a logon task starts has no console only because the executable declares consoleAllocationPolicy
"detached"; without it, Windows puts a console window on the desktop at every logon. The SDK copies the manifest
into the executable at publish, so the file is read the way Windows reads it (LoadLibraryEx as a data file, then
resource type 24, id 1) rather than trusting the project that asked for it.

Every value is read exactly, case included, and in the namespace this product's manifest writes it in. That is
stricter than Windows where Windows accepts more (it also reads the execution level under asm.v2), and never looser.
-ManifestFile checks a manifest that is already a file, which is how each refusal is shown working on its own.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, ParameterSetName = 'Executable')] [string] $Executable,
    [Parameter(Mandatory, ParameterSetName = 'ManifestFile')] [string] $ManifestFile
)
$ErrorActionPreference = 'Stop'

# A refusal is one plain line on standard error and exit 1: what a log, a person and a test all read the same way.
function Refuse([string] $Why) {
    [Console]::Error.WriteLine($Why)
    exit 1
}

# Compiled only where an executable is read: a manifest that is already a file needs no Windows to check.
$reader = @'
using System;
using System.Runtime.InteropServices;
public static class JasonManifestReader
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    public static string Read(string path)
    {
        var module = LoadLibraryExW(path, IntPtr.Zero, 0x2 | 0x20); // LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE
        if (module == IntPtr.Zero) throw new InvalidOperationException("could not open " + path + " (" + Marshal.GetLastWin32Error() + ")");
        try
        {
            var resource = FindResourceW(module, (IntPtr)1, (IntPtr)24);
            if (resource == IntPtr.Zero) return null;
            var size = (int)SizeofResource(module, resource);
            var bytes = new byte[size];
            Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, size);
            return System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }
        finally { FreeLibrary(module); }
    }
}
'@

if ($PSCmdlet.ParameterSetName -eq 'ManifestFile') {
    $path = (Resolve-Path -LiteralPath $ManifestFile).Path
    $text = [IO.File]::ReadAllText($path).TrimStart([char]0xFEFF)
}
else {
    Add-Type -TypeDefinition $reader
    $path = (Resolve-Path -LiteralPath $Executable).Path
    $text = [JasonManifestReader]::Read($path)
}

if (-not $text) { Refuse "$path carries no application manifest, so it does not declare consoleAllocationPolicy detached: a runtime started at logon would open a console window." }
[xml] $manifest = $text
$policy = $manifest.SelectSingleNode("//*[local-name()='consoleAllocationPolicy' and namespace-uri()='http://schemas.microsoft.com/SMI/2024/WindowsSettings']")
$level = $manifest.SelectSingleNode("//*[local-name()='requestedExecutionLevel' and namespace-uri()='urn:schemas-microsoft-com:asm.v3']")

# -cne: PowerShell compares strings without regard to case unless told otherwise, and Windows does not.
if (-not $policy -or $policy.InnerText -cne 'detached') { Refuse "$path does not declare consoleAllocationPolicy detached: a runtime started at logon would open a console window." }
if (-not $level -or $level.GetAttribute('level') -cne 'asInvoker' -or $level.GetAttribute('uiAccess') -cnotin @('', 'false')) {
    Refuse "$path does not run asInvoker without uiAccess, the rights of whoever started it and no more."
}
Write-Host "$path declares consoleAllocationPolicy detached and runs asInvoker."
