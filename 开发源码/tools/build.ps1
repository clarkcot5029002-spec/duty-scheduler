#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent $source
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'

foreach ($application in @(
    @{ Project = 'Duty.Scheduler'; Folder = '排班系统' },
    @{ Project = 'Duty.Intake'; Folder = '意愿填写系统' }
)) {
    $project = Join-Path $source "src\$($application.Project)\$($application.Project).csproj"
    $destination = Join-Path $root $application.Folder
    & dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($application.Project)" }
    & dotnet run --project (Join-Path $PSScriptRoot 'Duty.PortableCheck\Duty.PortableCheck.csproj') -c Release -- $destination
    if ($LASTEXITCODE -ne 0) { throw "Dependency check failed: $($application.Project)" }
    $licenseDirectory = Join-Path $destination 'licenses'
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $source 'assets\licenses\common\*') -Destination $licenseDirectory -Recurse -Force
    if ($application.Project -eq 'Duty.Scheduler') {
        Copy-Item -Path (Join-Path $source 'assets\licenses\scheduler\*') -Destination $licenseDirectory -Recurse -Force
    }
}
