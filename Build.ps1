# Сборка AlwaysOnTop.exe штатным компилятором .NET Framework (ничего ставить не нужно)
# Builds AlwaysOnTop.exe with the C# compiler that ships with .NET Framework (nothing to install)
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
# Язык интерфейса Windows — как у самой программы (Get-UICulture в PowerShell 5.1 берёт первый язык ввода и может отличаться)
if (-not ('AlwaysOnTopSetup.Lang' -as [type])) { Add-Type -Namespace AlwaysOnTopSetup -Name Lang -MemberDefinition '[DllImport("kernel32.dll")] public static extern ushort GetUserDefaultUILanguage();' }
$ru = [Globalization.CultureInfo]::GetCultureInfo([int][AlwaysOnTopSetup.Lang]::GetUserDefaultUILanguage()).TwoLetterISOLanguageName -eq "ru"
function Msg($en, $ruText) { if ($ru) { $ruText } else { $en } }

# 64-битный компилятор, а на 32-битной Windows — обычный
$csc = @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe", "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe") |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$csc) { throw (Msg "csc.exe (.NET Framework 4.x) not found" "csc.exe (.NET Framework 4.x) не найден") }

$out = Join-Path $dir "AlwaysOnTop.exe"
$ico = Join-Path $dir "AlwaysOnTop.ico"
$sources = Get-ChildItem -LiteralPath $dir -Filter *.cs | ForEach-Object { $_.FullName }

$cscArgs = @("/nologo", "/t:winexe", "/optimize+", "/codepage:65001", "/out:$out",
    "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll") + $sources
# Иконка: в ресурсы файла (проводник) и отдельным ресурсом (иконка в трее нужного размера)
if (Test-Path -LiteralPath $ico) { $cscArgs += "/win32icon:$ico"; $cscArgs += "/resource:$ico,AlwaysOnTop.ico" }
# Переводы вшиваются в exe: lang\ru.lang -> ресурс Lang.ru.lang
Get-ChildItem -LiteralPath (Join-Path $dir "lang") -Filter *.lang | ForEach-Object { $cscArgs += "/resource:$($_.FullName),Lang.$($_.BaseName).lang" }

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw ((Msg "Build failed (csc exit {0})" "Сборка не удалась (csc exit {0})") -f $LASTEXITCODE) }
Write-Host "OK: $out" -ForegroundColor Green
Get-Item $out | Select-Object Name,Length,LastWriteTime
