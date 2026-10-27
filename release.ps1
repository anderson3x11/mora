# Builds a release: Mora self-contained (no .NET install needed on the user's PC) into artifacts\Mora,
# then the installer artifacts\Mora-Setup-VERSION.exe with Inno Setup 6. The version comes from Mora.csproj.
# The plain build output is packaged: `dotnet publish` drops the compiled XAML resources (Mora.pri, *.xbf).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'src\Mora\Mora.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $root 'artifacts\Mora'
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup)' }

Remove-Item (Join-Path $root 'artifacts') -Recurse -Force -ErrorAction SilentlyContinue
dotnet build (Join-Path $root 'src\Mora') -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -o "$out\" -v q -nologo # trailing \: the XAML compiler appends file names to it as is
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Get-ChildItem $out -Filter *.pdb -Recurse | Remove-Item

& $iscc "/DAppVersion=$version" /Q (Join-Path $root 'installer\Mora.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
"Built artifacts\Mora-Setup-$version.exe"
