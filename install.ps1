# Builds the installer (release.ps1) and runs it silently: the same install as a release, uninstaller included.
# Run again to update; settings are kept. Needs the .NET 10 SDK and Inno Setup 6.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'release.ps1')
$version = ([xml](Get-Content (Join-Path $PSScriptRoot 'src\Mora\Mora.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Start-Process (Join-Path $PSScriptRoot "artifacts\Mora-Setup-$version.exe") -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait

# Started through Explorer, like a Start menu launch: a process started from a packaged app (an IDE,
# an assistant) would otherwise inherit its AppData virtualization and miss the real settings.
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Mora\Mora.exe'
Start-Process explorer.exe $exe
"Installed Mora $version in $(Split-Path $exe)"
