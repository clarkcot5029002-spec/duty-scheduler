#requires -Version 7.0
param([string]$RunLabel = '')

$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent $source
$relocation = Join-Path $source 'qa\迁移验证'
if ($RunLabel) {
    if ($RunLabel -notmatch '^[A-Za-z0-9_-]+$') { throw 'RunLabel must contain only ASCII letters, digits, underscores or hyphens.' }
    $relocation = Join-Path $relocation $RunLabel
}
New-Item -ItemType Directory -Path $relocation -Force | Out-Null

function Manifest([string]$directory) {
    $full = [IO.Path]::GetFullPath($directory)
    return @(
        Get-ChildItem -LiteralPath $full -File -Recurse | ForEach-Object {
            ($_.FullName.Substring($full.Length)) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        } | Sort-Object
    )
}

foreach ($application in @(
    @{ Name = '意愿填写系统'; TestFolder = '意愿 填写 便携测试' },
    @{ Name = '排班系统'; TestFolder = '排班 便携 测试' }
)) {
    $original = Join-Path $root $application.Name
    $copy = Join-Path $relocation $application.TestFolder
    if (Test-Path -LiteralPath $copy) { throw "Relocation directory already exists; inspect it before retrying: $copy" }
    New-Item -ItemType Directory -Path $copy | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $original -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $copy -Recurse
    }
    $before = Manifest $copy
    $evidenceRoot = Join-Path $source 'qa\便携验收'
    if ($RunLabel) { $evidenceRoot = Join-Path $evidenceRoot $RunLabel }
    $evidence = Join-Path $evidenceRoot $application.Name
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $copy ($application.Name + '.exe')
    $start.WorkingDirectory = $env:SystemRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $start.ArgumentList.Add('--verify-portable')
    $start.ArgumentList.Add($evidence)
    $start.Environment['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
    $start.Environment['DOTNET_ROOT'] = Join-Path $relocation '没有系统运行时'
    $start.Environment['DOTNET_ROOT_X64'] = $start.Environment['DOTNET_ROOT']
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.Environment.Remove('DOTNET_ADDITIONAL_DEPS') | Out-Null
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(120000)) {
        $process.Kill($true)
        throw "Portable test timed out: $($application.Name)"
    }
    $out = $stdout.GetAwaiter().GetResult()
    $err = $stderr.GetAwaiter().GetResult()
    Write-Output $out
    if ($err) { Write-Output $err }
    if ($process.ExitCode -ne 0 -or -not $out.Contains('PORTABLE_RUNTIME_CHECK=PASS')) {
        throw "Portable executable test failed: $($application.Name), exit $($process.ExitCode)"
    }
    $after = Manifest $copy
    if (@(Compare-Object $before $after).Count -ne 0) {
        throw "Application files changed during verification: $copy"
    }
    Write-Output ("RELOCATED_APP_PASS=" + $application.Name)
    Write-Output ("APP_DIRECTORY_UNCHANGED=" + $before.Count + ' files')
}
