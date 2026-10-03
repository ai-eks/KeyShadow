$ErrorActionPreference = 'Stop'
$app = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $app)
$build = Join-Path $PSScriptRoot '.build'
[void](New-Item -ItemType Directory -Force -Path $build)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$generator = Join-Path $build 'GenerateIcon.exe'
$arguments = @(
    '/nologo', '/target:exe', '/warnaserror', '/r:System.Drawing.dll',
    ('/out:"' + $generator + '"'),
    ('"' + (Join-Path $app 'src\AppIcon.cs') + '"'),
    ('"' + (Join-Path $PSScriptRoot 'GenerateIcon.cs') + '"')
)
$process = Start-Process -FilePath $compiler -ArgumentList $arguments -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Icon generator compilation failed.' }
$process = Start-Process -FilePath $generator -ArgumentList ('"' + (Join-Path $app 'assets') + '" "' + (Join-Path $repo 'shared\branding') + '"') -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Icon generation failed.' }
