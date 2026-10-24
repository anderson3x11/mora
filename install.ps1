# Builds Atoll in Release and installs it for the current user in %LOCALAPPDATA%\Programs\Atoll,
# then restarts it. Run again to update. Settings in %LOCALAPPDATA%\Atoll are kept.
# The plain build output is installed: `dotnet publish` drops the compiled XAML resources (Atoll.pri, *.xbf).
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Atoll'
$out = Join-Path $PSScriptRoot 'src\Atoll\bin\x64\Release\net10.0-windows10.0.19041.0'

dotnet build (Join-Path $PSScriptRoot 'src\Atoll') -c Release -p:Platform=x64 -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Get-Process Atoll -ErrorAction SilentlyContinue | Stop-Process
Start-Sleep -Milliseconds 500
robocopy $out $dest /MIR /XD win-x64 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy failed ($LASTEXITCODE)" }

Start-Process (Join-Path $dest 'Atoll.exe')
"Installed in $dest"
