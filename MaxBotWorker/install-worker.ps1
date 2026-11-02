#Requires -Version 5.1
param(
    [string]$WorkerPath = "",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$TaskName = "VKR MaxBot Worker"

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host ("Task removed: {0}" -f $TaskName)
    exit 0
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($WorkerPath)) {
    $WorkerPath = Join-Path $scriptDir "bin\\Release\\net8.0-windows\\MaxBotWorker.exe"
}

if (-not (Test-Path -LiteralPath $WorkerPath)) {
    throw "MaxBotWorker.exe not found: $WorkerPath. Run: dotnet build MaxBotWorker\\MaxBotWorker.csproj -c Release"
}

$WorkerPath = (Resolve-Path -LiteralPath $WorkerPath).Path
$workerDir = Split-Path -Parent $WorkerPath

$vkrSettings = Join-Path $workerDir "appsettings.json"
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$mainAppSettings = Join-Path $repoRoot "VKR\\bin\\Release\\net8.0-windows\\appsettings.json"

if (-not (Test-Path -LiteralPath $vkrSettings) -and (Test-Path -LiteralPath $mainAppSettings)) {
    Copy-Item -LiteralPath $mainAppSettings -Destination $vkrSettings -Force
    Write-Host "Copied appsettings.json from VKR output."
}

if (-not (Test-Path -LiteralPath $vkrSettings)) {
    Write-Warning "appsettings.json not found near worker. Copy it manually."
}

$action = New-ScheduledTaskAction -Execute $WorkerPath -WorkingDirectory $workerDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "VKR MAX bot background worker" -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host ""
Write-Host ("Installed and started: {0}" -f $TaskName)
Write-Host ("EXE: {0}" -f $WorkerPath)
Write-Host "Check: Get-ScheduledTask -TaskName \"VKR MaxBot Worker\""
Write-Host "Remove: .\\install-worker.ps1 -Uninstall"
