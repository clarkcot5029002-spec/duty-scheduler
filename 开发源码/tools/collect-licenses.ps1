#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $source 'assets\licenses'
$common = Join-Path $assets 'common'
$scheduler = Join-Path $assets 'scheduler'
New-Item -ItemType Directory -Path $common, $scheduler -Force | Out-Null
$packages = Join-Path $env:USERPROFILE '.nuget\packages'
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64\10.0.8\LICENSE.TXT') -Destination (Join-Path $common 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64\10.0.8\THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $common 'DOTNET-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $packages 'microsoft.windowsdesktop.app.runtime.win-x64\10.0.8\LICENSE') -Destination (Join-Path $common 'WPF-LICENSE.txt')

$entries = @(
    @{ Repo = 'google/or-tools'; Ref = 'v9.15'; Path = 'LICENSE'; File = 'OR-TOOLS-LICENSE.txt' },
    @{ Repo = 'protocolbuffers/protobuf'; Ref = 'v33.1'; Path = 'LICENSE'; File = 'PROTOBUF-LICENSE.txt' },
    @{ Repo = 'protocolbuffers/protobuf'; Ref = 'v33.1'; Path = 'third_party/utf8_range/LICENSE'; File = 'UTF8-RANGE-LICENSE.txt' },
    @{ Repo = 'abseil/abseil-cpp'; Ref = '20250814.1'; Path = 'LICENSE'; File = 'ABSEIL-LICENSE.txt' },
    @{ Repo = 'google/re2'; Ref = '2025-08-12'; Path = 'LICENSE'; File = 'RE2-LICENSE.txt' },
    @{ Repo = 'ERGO-Code/HiGHS'; Ref = 'v1.12.0'; Path = 'LICENSE.txt'; File = 'HIGHS-LICENSE.txt' },
    @{ Repo = 'scipopt/scip'; Ref = 'v10.0.0'; Path = 'LICENSE'; File = 'SCIP-LICENSE.txt' },
    @{ Repo = 'scipopt/soplex'; Ref = 'v8.0.0'; Path = 'LICENSE'; File = 'SOPLEX-LICENSE.txt' },
    @{ Repo = 'madler/zlib'; Ref = 'v1.3.1'; Path = 'LICENSE'; File = 'ZLIB-LICENSE.txt' },
    @{ Repo = 'boostorg/boost'; Ref = 'boost-1.87.0'; Path = 'LICENSE_1_0.txt'; File = 'BOOST-LICENSE.txt' },
    @{ Repo = 'google/or-tools'; Ref = 'v9.15'; Path = 'cmake/dependencies/CMakeLists.txt'; File = 'OR-TOOLS-DEPENDENCY-SOURCES.txt' }
)
$manifest = foreach ($entry in $entries) {
    $url = "https://api.github.com/repos/$($entry.Repo)/contents/$($entry.Path)?ref=$($entry.Ref)"
    $json = & curl.exe --ssl-revoke-best-effort --fail --silent --show-error --location --max-time 30 $url
    if ($LASTEXITCODE -ne 0) { throw "Unable to collect upstream license: $url" }
    $response = $json | ConvertFrom-Json
    if ($response.encoding -ne 'base64') { throw "Unexpected GitHub content encoding: $url" }
    $destination = Join-Path $scheduler $entry.File
    $bytes = [Convert]::FromBase64String($response.content)
    [IO.File]::WriteAllBytes($destination, $bytes)
    [pscustomobject]@{
        File = $entry.File
        Source = $response.html_url
        GitBlob = $response.sha
        SHA256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    }
    Write-Host ("COLLECTED=" + $entry.File)
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $scheduler 'upstream-sources.json') -Encoding utf8

$archive = Join-Path $source 'qa\bzip2-1.0.6.tar.gz'
& curl.exe --ssl-revoke-best-effort --fail --silent --show-error --location --max-time 120 'https://sourceware.org/pub/bzip2/bzip2-1.0.6.tar.gz' --output $archive
if ($LASTEXITCODE -ne 0) { throw 'Unable to download bzip2 license source.' }
$license = & tar.exe -xOf $archive 'bzip2-1.0.6/LICENSE'
if ($LASTEXITCODE -ne 0) { throw 'Unable to extract bzip2 license.' }
$license | Set-Content -LiteralPath (Join-Path $scheduler 'BZIP2-LICENSE.txt') -Encoding utf8
