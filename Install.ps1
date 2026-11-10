# Установка: сборка + копирование + автозагрузка (ярлык в Startup + ключ HKCU Run для надёжности)
# Install: build + copy + autostart (Startup shortcut + HKCU Run key as a backup)
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Язык интерфейса Windows — как у самой программы (Get-UICulture в PowerShell 5.1 берёт первый язык ввода и может отличаться)
if (-not ('AlwaysOnTopSetup.Lang' -as [type])) { Add-Type -Namespace AlwaysOnTopSetup -Name Lang -MemberDefinition '[DllImport("kernel32.dll")] public static extern ushort GetUserDefaultUILanguage();' }
$ru = [Globalization.CultureInfo]::GetCultureInfo([int][AlwaysOnTopSetup.Lang]::GetUserDefaultUILanguage()).TwoLetterISOLanguageName -eq "ru"
function Msg($en, $ruText) { if ($ru) { $ruText } else { $en } }

& (Join-Path $dir "Build.ps1")

$targetDir = Join-Path $env:LOCALAPPDATA "AlwaysOnTop"
$targetExe = Join-Path $targetDir "AlwaysOnTop.exe"
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# Запущенный exe заблокирован — закрываем старую копию, чтобы обновление не падало (settings.ini остаётся)
Get-Process -Name "AlwaysOnTop" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); [void]$_.WaitForExit(3000) }
Copy-Item -LiteralPath (Join-Path $dir "AlwaysOnTop.exe") -Destination $targetExe -Force
Write-Host ((Msg "Copied: {0}" "Скопировано: {0}") -f $targetExe) -ForegroundColor Green

# --autostart: второй путь автозагрузки застаёт программу уже запущенной и тихо выходит, а не открывает настройки
# 1) Ярлык в автозагрузке
$startup = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$lnk = Join-Path $startup "AlwaysOnTop.lnk"
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $targetExe
$sc.Arguments = "--autostart"
$sc.WorkingDirectory = $targetDir
$sc.Description = "Always on Top"
$sc.Save()
Write-Host ((Msg "Startup shortcut: {0}" "Ярлык автозагрузки: {0}") -f $lnk) -ForegroundColor Green

# 2) Резерв: ключ Run в реестре (переживёт чистки папки Startup)
New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "AlwaysOnTop" -Value "`"$targetExe`" --autostart"
Write-Host (Msg "Registry Run key: OK" "Ключ Run в реестре: OK") -ForegroundColor Green

Start-Process -FilePath $targetExe -ArgumentList "--autostart"
Write-Host (Msg "Started. Press Ctrl+Win+T (default) on any window. To change the hotkey or language, double-click the tray icon or run AlwaysOnTop.exe again." `
               "Запущено. Нажмите Ctrl+Win+T (по умолчанию) на любом окне. Сменить сочетание или язык — двойной щелчок по значку в трее или повторный запуск AlwaysOnTop.exe.") -ForegroundColor Cyan
