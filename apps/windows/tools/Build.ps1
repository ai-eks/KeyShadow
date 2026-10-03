param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\..\dist\windows'))
$ErrorActionPreference = 'Stop'
$app = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $app)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
if (!(Test-Path (Join-Path $app 'assets\app.ico'))) {
    & (Join-Path $PSScriptRoot 'GenerateIcon.ps1')
}
[void](New-Item -ItemType Directory -Force -Path $OutputDirectory)
$output = Join-Path $OutputDirectory '键影.exe'
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/langversion:5', '/optimize+', '/warnaserror', '/utf8output',
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:Accessibility.dll',
    ('/reference:"' + $framework + '\WPF\UIAutomationClient.dll"'),
    ('/reference:"' + $framework + '\WPF\UIAutomationTypes.dll"'),
    ('/reference:"' + $framework + '\WPF\WindowsBase.dll"'),
    ('/win32manifest:"' + $app + '\src\app.manifest"'),
    ('/win32icon:"' + $app + '\assets\app.ico"'),
    ('/out:"' + $output + '"'))
$version = (Get-Content (Join-Path $repo 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$' -or @($version.Split('.') | Where-Object { [int]$_ -gt 65534 }).Count -gt 0) {
    throw 'VERSION must contain three numbers between 0 and 65534.'
}
$generated = Join-Path $app '.build'
[void](New-Item -ItemType Directory -Force -Path $generated)
$assemblyInfo = (Get-Content (Join-Path $app 'src\AssemblyInfo.cs') -Raw -Encoding UTF8) -replace 'Assembly(File)?Version\("[^"]+"\)', ('Assembly$1Version("' + $version + '.0")')
$assemblyPath = Join-Path $generated 'AssemblyInfo.cs'
Set-Content $assemblyPath -Value $assemblyInfo -Encoding UTF8
$arguments += @(Get-ChildItem (Join-Path $app 'src') -Filter '*.cs' | Where-Object { $_.Name -ne 'AssemblyInfo.cs' } | ForEach-Object { '"' + $_.FullName + '"' })
$arguments += '"' + $assemblyPath + '"'
$process = Start-Process (Join-Path $framework 'csc.exe') -ArgumentList $arguments -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Build failed.' }
$guide = Get-Content (Join-Path $repo 'docs\windows\使用说明.md') -Raw -Encoding UTF8
$guide = $guide.Replace('](../双拼方案.md)', '](双拼方案.md)')
$guide = $guide.Replace('](../../README.md#通用操作)', '](通用操作.md)')
Set-Content (Join-Path $OutputDirectory '使用说明.md') -Value $guide -Encoding UTF8
$common = [regex]::Match((Get-Content (Join-Path $repo 'README.md') -Raw -Encoding UTF8), '(?ms)^## 通用操作\r?\n.*?(?=^## |\z)').Value.Trim()
$common = $common -replace '^## ', '# ' -replace '(?m)^### ', '## '
$common = $common.Replace('](docs/双拼方案.md)', '](双拼方案.md)')
Set-Content (Join-Path $OutputDirectory '通用操作.md') -Value $common -Encoding UTF8
Copy-Item (Join-Path $repo 'docs\双拼方案.md') $OutputDirectory -Force
Write-Host "Built: $output"
