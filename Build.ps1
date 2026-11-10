# Сборка AlwaysOnTop.exe штатным компилятором .NET Framework (без установки чего-либо)
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path -LiteralPath $csc)) { throw "csc.exe не найден: $csc" }
$src = Join-Path $dir "AlwaysOnTop.cs"
$out = Join-Path $dir "AlwaysOnTop.exe"
$ico = Join-Path $dir "AlwaysOnTop.ico"

$cscArgs = @("/nologo", "/t:winexe", "/optimize+", "/codepage:65001", "/out:$out", $src,
    "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll")
# Иконка: в ресурсы файла (проводник) и отдельным ресурсом (иконка в трее нужного размера)
if (Test-Path -LiteralPath $ico) { $cscArgs += "/win32icon:$ico"; $cscArgs += "/resource:$ico,AlwaysOnTop.ico" }

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась (csc exit $LASTEXITCODE)" }
Write-Host "OK: $out" -ForegroundColor Green
Get-Item $out | Select-Object Name,Length,LastWriteTime
