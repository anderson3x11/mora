# Runs the keystroke-safety test against the Debug build, with settings of its own (MORA_DATA),
# so the user's settings are never touched. The installed Mora is stopped during the test and
# started again at the end. Takes over mouse and keyboard for about a minute. -Theme picks the island's theme.
param([string]$Theme = 'Island')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$data = Join-Path $env:TEMP 'mora-keystroke-test'
New-Item -ItemType Directory -Force $data | Out-Null
Remove-Item (Join-Path $data '*') -Force -ErrorAction SilentlyContinue
# Not a first run: no setup window. The test's fullscreen window counts as a game through its own profile.
"{ `"StartWithWindows`": false, `"Theme`": `"$Theme`", `"GameProfiles`": { `"KeystrokeTest`": {} } }" | Set-Content (Join-Path $data 'settings.json') -Encoding utf8

Get-Process Mora -ErrorAction SilentlyContinue | Stop-Process # a running Mora locks the build output
Start-Sleep -Milliseconds 500
$code = 1
try {
    dotnet build (Join-Path $root 'src\Mora') -p:Platform=x64 -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Mora does not build' } # never test a stale binary
    dotnet build (Join-Path $root 'tests\KeystrokeTest') -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'KeystrokeTest does not build' }

    $env:MORA_DATA = $data
    $env:MORA_TEST_THEME = $Theme
    $mora = Start-Process (Join-Path $root 'src\Mora\bin\x64\Debug\net10.0-windows10.0.19041.0\Mora.exe') -PassThru
    Start-Sleep -Seconds 5
    & (Join-Path $root 'tests\KeystrokeTest\bin\Debug\net10.0-windows\KeystrokeTest.exe')
    $code = $LASTEXITCODE
    Stop-Process -Id $mora.Id -ErrorAction SilentlyContinue
}
finally {
    Remove-Item Env:MORA_DATA, Env:MORA_TEST_THEME -ErrorAction SilentlyContinue
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\Mora\Mora.exe'
    if (Test-Path $installed) { Start-Process explorer.exe $installed }
}
exit $code
