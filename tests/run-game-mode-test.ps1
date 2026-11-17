# Runs the two-screen game mode test against the Debug build, with settings of its own (MORA_DATA). The installed
# Mora is stopped during the test and started again at the end. Takes the mouse for about 45 seconds.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$data = Join-Path $env:TEMP 'mora-game-mode-test'
New-Item -ItemType Directory -Force $data | Out-Null
Remove-Item (Join-Path $data '*') -Force -ErrorAction SilentlyContinue
# This test's window counts as a game through its own profile, with nothing allowed to pop up.
'{ "StartWithWindows": false, "PopVolume": false, "GameProfiles": { "GameModeTest": {} } }' | Set-Content (Join-Path $data 'settings.json') -Encoding utf8
# Any Mora, including one still starting up (the installed one, relaunched by a previous test run)
for ($i = 0; $i -lt 8; $i++) { Get-Process Mora -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 400 }
$code = 1
try {
    dotnet build (Join-Path $root 'src\Mora') -p:Platform=x64 -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Mora does not build' }
    dotnet build (Join-Path $root 'tests\GameModeTest') -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'GameModeTest does not build' }
    $exe = Join-Path $root 'src\Mora\bin\x64\Debug\net10.0-windows10.0.19041.0\Mora.exe'
    $env:MORA_DATA = $data; $env:MORA_EXE = $exe
    $mora = Start-Process $exe -PassThru
    Start-Sleep -Seconds 5
    & (Join-Path $root 'tests\GameModeTest\bin\Debug\net10.0-windows\GameModeTest.exe')
    $code = $LASTEXITCODE
    Stop-Process -Id $mora.Id -ErrorAction SilentlyContinue
}
finally {
    Remove-Item Env:MORA_DATA, Env:MORA_EXE -ErrorAction SilentlyContinue
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\Mora\Mora.exe'
    if (Test-Path $installed) { Start-Process explorer.exe $installed }
}
exit $code
