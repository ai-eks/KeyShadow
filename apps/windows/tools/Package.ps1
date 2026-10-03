param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$staging = Join-Path $repo 'dist\windows'
$output = Join-Path $repo 'dist\packages'
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $staging
$version = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
[void](New-Item -ItemType Directory -Force -Path $output)
$name = "KeyShadow-$version-Windows.exe"
$exe = Join-Path $output $name
Copy-Item -LiteralPath (Join-Path $staging '键影.exe') -Destination $exe -Force
if ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") {
    throw 'Packaged executable version does not match VERSION.'
}
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$exe.sha256" -Value "$hash  $name" -Encoding ASCII
Write-Host "Packaged: $exe"
