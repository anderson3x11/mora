# Builds Mora in Release and installs it for the current user in %LOCALAPPDATA%\Programs\Mora,
# then restarts it. Run again to update. Settings in %LOCALAPPDATA%\Mora are kept.
# The plain build output is installed: `dotnet publish` drops the compiled XAML resources (Mora.pri, *.xbf).
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Mora'
$out = Join-Path $PSScriptRoot 'src\Mora\bin\x64\Release\net10.0-windows10.0.19041.0'

dotnet build (Join-Path $PSScriptRoot 'src\Mora') -c Release -p:Platform=x64 -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Get-Process Mora, Atoll -ErrorAction SilentlyContinue | Stop-Process # Atoll: the app's former name
Start-Sleep -Milliseconds 500
robocopy $out $dest /MIR /XD win-x64 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy failed ($LASTEXITCODE)" }

# Leftovers of the install under the former name. Its settings folder is moved by Mora itself on first launch.
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\Atoll') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Atoll.lnk') -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name Atoll -ErrorAction SilentlyContinue

# Start menu shortcut, so Windows search finds the app.
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Mora.lnk'))
$shortcut.TargetPath = Join-Path $dest 'Mora.exe'
$shortcut.WorkingDirectory = $dest
$shortcut.Description = 'A Dynamic Island for Windows'
$shortcut.Save()

# Started through Explorer, like a Start menu launch: a process started from a packaged app (an IDE,
# an assistant) would otherwise inherit its AppData virtualization and miss the real settings.
Start-Process explorer.exe (Join-Path $dest 'Mora.exe')
"Installed in $dest"
