# Installs Desk Wheel from its installer, exactly as a person would (silently, for this user only),
# then runs "DeskWheel.exe --self-test" on the installed copy. Fails if any engine or tool doesn't work.
#   pwsh scripts/self-test-installer.ps1 dist\DeskWheel-0.1.0-win-x64-setup.exe
param([Parameter(Mandatory)][string]$Setup)
$ErrorActionPreference = 'Stop'
$base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$dir = Join-Path $base 'DeskWheel-installed'
$out = Join-Path $base 'DeskWheel-self-test'
Write-Host "Installing $Setup into $dir..."
# Not "Start-Process -Wait": that also waits for the copy of Desk Wheel a silent install starts, which keeps running.
$install = Start-Process (Resolve-Path $Setup).Path -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$dir" -PassThru
$null = $install.Handle   # keeps the exit code readable after WaitForExit
if (-not $install.WaitForExit(300000)) { throw "The installer didn't finish within 5 minutes." }
if ($install.ExitCode -ne 0) { throw "The installer failed (exit code $($install.ExitCode))." }
Start-Sleep -Seconds 3
Stop-Process -Name DeskWheel -Force -ErrorAction SilentlyContinue   # the copy the silent install started
Get-ChildItem $dir, (Join-Path $dir 'engines') | Format-Table Name, Length -AutoSize | Out-String | Write-Host
$test = Start-Process (Join-Path $dir 'DeskWheel.exe') -ArgumentList '--self-test', "`"$out`"" -PassThru
$null = $test.Handle
if (-not $test.WaitForExit(600000)) { Stop-Process $test -Force; throw "The self-test didn't finish within 10 minutes." }
Get-Content (Join-Path $out 'self-test.txt') | Write-Host
if ($test.ExitCode -ne 0) { throw "$($test.ExitCode) self-test check(s) failed: this build must not ship." }
