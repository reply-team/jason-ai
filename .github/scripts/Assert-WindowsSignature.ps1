<#
.SYNOPSIS
Refuses a Windows executable whose Authenticode signature signtool does not verify, that Windows does not read as
valid and timestamped, or that a publisher other than -Publisher made. Read from the file that is about to be
archived, and again from the archive's own copy.

.DESCRIPTION
signtool verifies under the Authenticode policy (/pa), the one a downloaded program is held to, rather than the driver
policy it applies by default, and verbosely, so the log carries the chain and the timestamp. It is the runner's own,
the newest x64 signtool the Windows SDK installed, never the one the signing action brought with it. Windows' own
reading follows: the signature valid, and a timestamp, without which the signature would end with its certificate.
A valid, timestamped signature says only that some trusted publisher signed the file, so the signer's simple name, the
CN of its certificate's subject, must be -Publisher exactly: in full and in the same case, never a name that only
contains it.

What signed the file is printed: the signer, its issuer and thumbprint, and the timestamp's signer. -SignTool names
another signtool, which is how each refusal is shown working on its own.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Executable,
    [Parameter(Mandatory)] [string] $Publisher,
    [string] $SignTool
)
$ErrorActionPreference = 'Stop'

# A refusal is one plain line on standard error and exit 1: what a log, a person and a test all read the same way.
function Refuse([string] $Why) {
    [Console]::Error.WriteLine($Why)
    exit 1
}

$path = (Resolve-Path -LiteralPath $Executable).Path

if (-not $SignTool) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $SignTool = Get-ChildItem -LiteralPath $kits -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
        Sort-Object { [version] $_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $SignTool) { Refuse "No x64 signtool.exe of the Windows SDK under $kits, so the signature could not be verified." }
}
elseif (-not (Test-Path -LiteralPath $SignTool)) {
    Refuse "There is no signtool at $SignTool, so the signature could not be verified."
}

& $SignTool verify /pa /v $path
if ($LASTEXITCODE -ne 0) { Refuse "signtool verify /pa refused $path (exit $LASTEXITCODE)." }

$signature = Get-AuthenticodeSignature -LiteralPath $path
if ($signature.Status -ne 'Valid') { Refuse "$path does not carry a valid Authenticode signature: $($signature.Status)." }
if (-not $signature.TimeStamperCertificate) { Refuse "$path is signed without a timestamp, so its signature ends with its certificate." }
$signer = $signature.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
if ($signer -cne $Publisher) { Refuse "$path is signed by '$signer', not by '$Publisher', the publisher it must be signed by." }

Write-Host "$path is signed by $($signature.SignerCertificate.Subject)"
Write-Host "  issued by $($signature.SignerCertificate.Issuer), thumbprint $($signature.SignerCertificate.Thumbprint)"
Write-Host "  timestamped by $($signature.TimeStamperCertificate.Subject)"
