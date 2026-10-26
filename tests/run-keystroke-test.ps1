# Runs the keystroke-safety test against the Debug build, with settings of its own (ATOLL_DATA),
# so the user's settings are never touched. The installed Atoll is stopped during the test and
# started again at the end. Takes over mouse and keyboard for about a minute.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$data = Join-Path $env:TEMP 'atoll-keystroke-test'
New-Item -ItemType Directory -Force $data | Out-Null
Remove-Item (Join-Path $data '*') -Force -ErrorAction SilentlyContinue
'{ "StartWithWindows": false }' | Set-Content (Join-Path $data 'settings.json') -Encoding utf8 # not a first run: no setup window

Get-Process Atoll -ErrorAction SilentlyContinue | Stop-Process # a running Atoll locks the build output
Start-Sleep -Milliseconds 500
$code = 1
try {
    dotnet build (Join-Path $root 'src\Atoll') -p:Platform=x64 -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Atoll does not build' } # never test a stale binary
    dotnet build (Join-Path $root 'tests\KeystrokeTest') -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'KeystrokeTest does not build' }

    $env:ATOLL_DATA = $data
    $atoll = Start-Process (Join-Path $root 'src\Atoll\bin\x64\Debug\net10.0-windows10.0.19041.0\Atoll.exe') -PassThru
    Start-Sleep -Seconds 5
    & (Join-Path $root 'tests\KeystrokeTest\bin\Debug\net10.0-windows\KeystrokeTest.exe')
    $code = $LASTEXITCODE
    Stop-Process -Id $atoll.Id -ErrorAction SilentlyContinue
}
finally {
    Remove-Item Env:ATOLL_DATA -ErrorAction SilentlyContinue
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\Atoll\Atoll.exe'
    if (Test-Path $installed) { Start-Process explorer.exe $installed }
}
exit $code
