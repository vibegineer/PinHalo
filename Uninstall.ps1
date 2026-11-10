# Удаление: останавливает программу, убирает автозагрузку и установленные файлы (вместе с settings.ini).
# Uninstall: stops the program, removes autostart entries and installed files (including settings.ini).
# Проверить без изменений / dry run: .\Uninstall.ps1 -WhatIf
# Если есть закреплённые окна — сначала выберите «Выход» в меню значка в трее: тогда закрепления снимутся.
# If windows are still pinned, choose Exit in the tray menu first so they get unpinned.
[CmdletBinding(SupportsShouldProcess = $true)]
param()
$ErrorActionPreference = "Stop"
# Язык интерфейса Windows — как у самой программы (Get-UICulture в PowerShell 5.1 берёт первый язык ввода и может отличаться)
if (-not ('AlwaysOnTopSetup.Lang' -as [type])) { Add-Type -Namespace AlwaysOnTopSetup -Name Lang -MemberDefinition '[DllImport("kernel32.dll")] public static extern ushort GetUserDefaultUILanguage();' }
$ru = [Globalization.CultureInfo]::GetCultureInfo([int][AlwaysOnTopSetup.Lang]::GetUserDefaultUILanguage()).TwoLetterISOLanguageName -eq "ru"
function Msg($en, $ruText) { if ($ru) { $ruText } else { $en } }

$targetDir = Join-Path $env:LOCALAPPDATA "AlwaysOnTop"
$lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\AlwaysOnTop.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Get-Process -Name "AlwaysOnTop" -ErrorAction SilentlyContinue | ForEach-Object {
    if ($PSCmdlet.ShouldProcess("AlwaysOnTop (PID $($_.Id))", (Msg "Stop process" "Остановить процесс"))) { $_.Kill(); [void]$_.WaitForExit(3000) }
}

if (Test-Path -LiteralPath $lnk) {
    if ($PSCmdlet.ShouldProcess($lnk, (Msg "Remove startup shortcut" "Удалить ярлык автозагрузки"))) { Remove-Item -LiteralPath $lnk -Force }
}

if (Get-ItemProperty -Path $runKey -Name "AlwaysOnTop" -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess("$runKey\AlwaysOnTop", (Msg "Remove autostart registry value" "Удалить ключ автозагрузки"))) { Remove-ItemProperty -Path $runKey -Name "AlwaysOnTop" }
}

if (Test-Path -LiteralPath $targetDir) {
    if ($PSCmdlet.ShouldProcess($targetDir, (Msg "Remove folder" "Удалить папку"))) { Remove-Item -LiteralPath $targetDir -Recurse -Force }
}

if (-not $WhatIfPreference) { Write-Host (Msg "Uninstalled." "Удалено.") -ForegroundColor Green }
