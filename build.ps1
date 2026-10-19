# Собирает Снежный браузер и установщик в один файл:
#   .\build.ps1                  → dist\<версия>\SneznyjBrowser-Setup-<версия>.exe
#   .\build.ps1 -Install         → то же + тихо поставить на этот компьютер (%LOCALAPPDATA%\Programs\SneznyjBrowser)
#   .\build.ps1 -Version 0.1.90  → собрать другую версию, не меняя .csproj (для проверки обновлений)
# Браузер собирается self-contained в один файл: на компьютере не нужен .NET, нужен только WebView2 (есть в Windows 10/11).
# Установщик — installer\ (.NET Framework 4.6.2, встроен в Windows), браузер приклеивается к нему сжатым LZMS.
param([switch]$Install, [string]$Version)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Version) { $Version = (Select-Xml -Path (Join-Path $root 'SneznyjBrowser.csproj') -XPath '//Version').Node.InnerText }
$pub = Join-Path $root 'publish'
$dist = Join-Path $root "dist\$Version"
$setupExe = Join-Path $dist "SneznyjBrowser-Setup-$Version.exe"

Write-Output "Собираю Снежный браузер $Version..."
dotnet publish (Join-Path $root 'SneznyjBrowser.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -p:Version=$Version -o (Join-Path $pub 'app') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка браузера не удалась' }

dotnet build (Join-Path $root 'installer\Setup.csproj') -c Release -p:Version=$Version -o (Join-Path $pub 'setup') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка установщика не удалась' }

dotnet build (Join-Path $root 'tools\ReleaseTool\ReleaseTool.csproj') -c Release -o (Join-Path $pub 'tool') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка инструмента выпуска не удалась' }

Write-Output 'Упаковываю (сжатие LZMS, около 30 секунд)...'
& (Join-Path $pub 'tool\ReleaseTool.exe') pack --setup (Join-Path $pub 'setup\SneznyjBrowserSetup.exe') `
    --app (Join-Path $pub 'app\SneznyjBrowser.exe') --out $setupExe
if ($LASTEXITCODE -ne 0) { throw 'Упаковка не удалась' }
& (Join-Path $pub 'tool\ReleaseTool.exe') verify --installer $setupExe
if ($LASTEXITCODE -ne 0) { throw 'Установщик не прошёл проверку' }

if ($Install) {
    Write-Output 'Ставлю на этот компьютер (открытый браузер закроется, вкладки сохранятся)...'
    $p = Start-Process $setupExe -ArgumentList '--silent' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Установщик завершился с кодом $($p.ExitCode)" }
    Write-Output "Установлено: $env:LOCALAPPDATA\Programs\SneznyjBrowser"
}
Write-Output "Готово: $setupExe"
