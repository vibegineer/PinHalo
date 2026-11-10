# Установка: сборка + копирование + автозагрузка (Startup-ярлык + HKCU Run для надёжности)
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $dir "Build.ps1")

$targetDir = Join-Path $env:LOCALAPPDATA "AlwaysOnTop"
$targetExe = Join-Path $targetDir "AlwaysOnTop.exe"
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

# Запущенный exe заблокирован — закрываем старую копию, чтобы обновление не падало
Get-Process -Name "AlwaysOnTop" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); [void]$_.WaitForExit(3000) }
Copy-Item -LiteralPath (Join-Path $dir "AlwaysOnTop.exe") -Destination $targetExe -Force
Write-Host "Скопировано: $targetExe" -ForegroundColor Green

# 1) Ярлык в автозагрузке
$startup = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$lnk = Join-Path $startup "AlwaysOnTop.lnk"
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $targetExe
$sc.WorkingDirectory = $targetDir
$sc.Description = "Always on Top Ctrl+Win+T"
$sc.Save()
Write-Host "Ярлык: $lnk" -ForegroundColor Green

# 2) Резерв: ключ Run в реестре (переживёт чистки папки Startup)
New-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "AlwaysOnTop" -Value "`"$targetExe`""
Write-Host "Реестр Run: OK" -ForegroundColor Green

# Запустить сейчас (если ещё не запущено)
if (!(Get-Process -Name "AlwaysOnTop" -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $targetExe
    Write-Host "Запущено. Нажми Ctrl+Win+T на любом окне." -ForegroundColor Cyan
} else {
    Write-Host "Уже запущено." -ForegroundColor Cyan
}
