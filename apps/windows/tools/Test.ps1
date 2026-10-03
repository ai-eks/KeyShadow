param([switch]$Desktop, [switch]$Ime, [switch]$Browser)
$ErrorActionPreference = 'Stop'
$app = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$build = Join-Path $app 'tests\.build'
[void](New-Item -ItemType Directory -Force -Path $build)
$tests = @('SchemeTests')
if ($Desktop) { $tests += @('StartupTests', 'SmokeTests', 'RenderingTests', 'TypingScenario', 'PasswordTests') }
if ($Ime) { $tests += 'ImeTests' }
if ($Browser) { $tests += 'BrowserPasswordTests' }
foreach ($test in $tests) {
    $exe = Join-Path $build ($test + '.exe')
    $arguments = @('/nologo', '/target:exe', '/langversion:5', '/warnaserror', '/utf8output',
        ('/main:' + $test), ('/out:"' + $exe + '"'),
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll', '/reference:Accessibility.dll',
        ('/reference:"' + $framework + '\WPF\UIAutomationClient.dll"'),
        ('/reference:"' + $framework + '\WPF\UIAutomationTypes.dll"'),
        ('/reference:"' + $framework + '\WPF\WindowsBase.dll"'),
        ('/win32manifest:"' + $app + '\src\app.manifest"'),
        ('"' + $app + '\tests\' + $test + '.cs"'))
    if ($test -eq 'PasswordTests') {
        $arguments += @(('/reference:"' + $framework + '\WPF\PresentationCore.dll"'),
            ('/reference:"' + $framework + '\WPF\PresentationFramework.dll"'), '/reference:System.Xaml.dll')
    }
    if ($test -eq 'BrowserPasswordTests') { $arguments += '/reference:System.Web.Extensions.dll' }
    $arguments += @(Get-ChildItem (Join-Path $app 'src') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' })
    $process = Start-Process (Join-Path $framework 'csc.exe') -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Compilation failed: $test" }
    $log = Join-Path $build ($test + '.log')
    $errorLog = Join-Path $build ($test + '.err')
    $process = Start-Process $exe -ArgumentList ('"' + $build + '"') -Wait -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError $errorLog
    Get-Content $log
    if ($process.ExitCode -ne 0) { Get-Content $errorLog; throw "Test failed: $test" }
}
