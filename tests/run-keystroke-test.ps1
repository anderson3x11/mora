# Runs the keystroke-safety test against the Debug build, with settings of its own (ATOLL_DATA),
# so the user's settings are never touched. The installed Atoll is stopped during the test and
# started again at the end. Takes over mouse and keyboard for about a minute.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$data = Join-Path $env:TEMP 'atoll-keystroke-test'
New-Item -ItemType Directory -Force $data | Out-Null
Remove-Item (Join-Path $data '*') -Force -ErrorAction SilentlyContinue
'{ "StartWithWindows": false }' | Set-Content (Join-Path $data 'settings.json') -Encoding utf8 # not a first run: no setup window

dotnet build (Join-Path $root 'src\Atoll') -p:Platform=x64 -v q -nologo | Out-Null
dotnet build (Join-Path $root 'tests\KeystrokeTest') -v q -nologo | Out-Null

Get-Process Atoll -ErrorAction SilentlyContinue | Stop-Process
Start-Sleep -Milliseconds 500
$env:ATOLL_DATA = $data
$atoll = Start-Process (Join-Path $root 'src\Atoll\bin\x64\Debug\net10.0-windows10.0.19041.0\Atoll.exe') -PassThru
Start-Sleep -Seconds 5
try {
    & (Join-Path $root 'tests\KeystrokeTest\bin\Debug\net10.0-windows\KeystrokeTest.exe')
    $code = $LASTEXITCODE
}
finally {
    Stop-Process -Id $atoll.Id -ErrorAction SilentlyContinue
    Remove-Item Env:ATOLL_DATA
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\Atoll\Atoll.exe'
    if (Test-Path $installed) { Start-Process explorer.exe $installed }
}
exit $code
