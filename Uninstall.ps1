# Удаление: останавливает программу, убирает автозагрузку и установленные файлы.
# Проверить без изменений: .\Uninstall.ps1 -WhatIf
# Если есть закреплённые окна — сначала выберите «Выход» в меню значка в трее: тогда закрепления снимутся.
[CmdletBinding(SupportsShouldProcess = $true)]
param()
$ErrorActionPreference = "Stop"

$targetDir = Join-Path $env:LOCALAPPDATA "AlwaysOnTop"
$lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\AlwaysOnTop.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Get-Process -Name "AlwaysOnTop" -ErrorAction SilentlyContinue | ForEach-Object {
    if ($PSCmdlet.ShouldProcess("процесс AlwaysOnTop (PID $($_.Id))", "Остановить")) { $_.Kill(); [void]$_.WaitForExit(3000) }
}

if (Test-Path -LiteralPath $lnk) {
    if ($PSCmdlet.ShouldProcess($lnk, "Удалить ярлык автозагрузки")) { Remove-Item -LiteralPath $lnk -Force }
}

if (Get-ItemProperty -Path $runKey -Name "AlwaysOnTop" -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess("$runKey\AlwaysOnTop", "Удалить ключ автозагрузки")) { Remove-ItemProperty -Path $runKey -Name "AlwaysOnTop" }
}

if (Test-Path -LiteralPath $targetDir) {
    if ($PSCmdlet.ShouldProcess($targetDir, "Удалить папку")) { Remove-Item -LiteralPath $targetDir -Recurse -Force }
}

if (-not $WhatIfPreference) { Write-Host "Удалено." -ForegroundColor Green }
