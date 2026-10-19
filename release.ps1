# Выпуск новой версии Снежного браузера (подробно — README, раздел «Как выпустить новую версию»).
#   .\release.ps1 -Version 0.2.1 -Notes "Что нового"            собрать, подписать update.json, положить установщик в «Claude Проекты»
#   .\release.ps1 -Version 0.2.1 -Notes "Что нового" -Publish   и выложить на GitHub Releases — у всех браузеров появится «Обновить»
#   .\release.ps1 -Version 0.1.91 -Test                         тестовая версия для своего сервера http://127.0.0.1:8765 (ничего не выкладывает)
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Notes = '',
    [switch]$Publish,
    [switch]$Test,
    [string]$BaseUrl,
    [string]$Repo = 'Sneznyj/sneznyj-browser'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$key = Join-Path $env:APPDATA 'Sneznyj\browser-update-key.pem'
$share = Join-Path $env:USERPROFILE 'OneDrive\Рабочий стол\Claude Проекты\Снежный браузер'

if (-not ($Version -match '^\d+\.\d+\.\d+$')) { throw 'Версия должна быть вида 0.2.1' }
if (-not (Test-Path $key)) { throw "Нет ключа подписи $key. Без него обновления не выпустить (см. README)." }
if ($Publish -and $Test) { throw 'Тестовую версию не выкладывают' }
if (-not $BaseUrl) {
    $BaseUrl = if ($Test) { 'http://127.0.0.1:8765' } else { "https://github.com/$Repo/releases/download/v$Version" }
}

if (-not $Test) {
    # Номер версии живёт в обоих проектах — его видно в «Приложениях» и в Ctrl+K → «Проверить обновления».
    foreach ($proj in 'SneznyjBrowser.csproj', 'installer\Setup.csproj') {
        $path = Join-Path $root $proj
        $text = [IO.File]::ReadAllText($path)
        $text = $text -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
        [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
    }
}

& (Join-Path $root 'build.ps1') -Version $Version
$dist = Join-Path $root "dist\$Version"
$setupName = "SneznyjBrowser-Setup-$Version.exe"
$setupExe = Join-Path $dist $setupName
$manifest = Join-Path $dist 'update.json'
$tool = Join-Path $root 'publish\tool\ReleaseTool.exe'

$notesFile = Join-Path $dist 'notes.txt'
[IO.File]::WriteAllText($notesFile, $Notes, [Text.UTF8Encoding]::new($false))
& $tool manifest --installer $setupExe --version $Version --url "$BaseUrl/$setupName" --key $key --notes-file $notesFile --out $manifest
if ($LASTEXITCODE -ne 0) { throw 'Не удалось подписать update.json' }
Remove-Item $notesFile

# Проверка тем ключом, что вшит в браузер: если ключи разошлись, обновление никто не получит.
$pubkey = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Core\Updater.cs')), '"(MFkw[^"]+)"').Groups[1].Value
& $tool verify --installer $setupExe --manifest $manifest --pubkey $pubkey
if ($LASTEXITCODE -ne 0) { throw 'update.json не проходит проверку браузера' }

if (-not $Test) {
    New-Item -ItemType Directory -Force $share | Out-Null
    Get-ChildItem $share -Filter 'SneznyjBrowser-Setup-*.exe' | Where-Object Name -ne $setupName | Remove-Item
    Copy-Item $setupExe $share -Force
    Write-Output "Установщик для Алины и друзей: $(Join-Path $share $setupName)"
}

if ($Publish) {
    gh release view "v$Version" --repo $Repo 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "Выпуск v$Version уже есть на GitHub" }
    $title = "Снежный браузер $Version"
    $body = if ($Notes) { $Notes } else { $title }
    gh release create "v$Version" $setupExe $manifest --repo $Repo --title $title --notes $body --latest
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось выложить выпуск на GitHub' }
    Write-Output "Выложено: https://github.com/$Repo/releases/tag/v$Version — браузеры увидят обновление в течение суток"
}
Write-Output "Готово: $setupExe"
Write-Output "update.json: $manifest"
