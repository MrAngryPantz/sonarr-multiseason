param(
    [switch]$SkipUi
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $SkipUi) {
        & yarn.cmd install --frozen-lockfile
        if ($LASTEXITCODE -ne 0) { throw 'Installing frontend dependencies failed.' }
        & yarn.cmd build
        if ($LASTEXITCODE -ne 0) { throw 'Building the frontend failed.' }
    }

    if (-not (Test-Path '_output/UI/index.html')) {
        throw 'Build the frontend before using -SkipUi.'
    }

    & dotnet msbuild src/Sonarr.sln -restore -p:SelfContained=true -p:Configuration=Release -p:Platform=Windows -p:RuntimeIdentifiers=win-x64 -p:EnableWindowsTargeting=true -t:PublishAllRids -m:4 -nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Building the Windows application failed.' }

    $package = Join-Path $root '_artifacts/windows/Sonarr'
    if (Test-Path $package) { Remove-Item $package -Recurse -Force }
    New-Item $package -ItemType Directory -Force | Out-Null
    Copy-Item '_output/net10.0/win-x64/publish/*' $package -Recurse -Force
    Copy-Item '_output/net10.0-windows/win-x64/publish/*' $package -Recurse -Force
    Copy-Item '_output/Sonarr.Update/net10.0/win-x64/publish' (Join-Path $package 'Sonarr.Update') -Recurse
    Copy-Item '_output/UI' $package -Recurse
    Copy-Item 'LICENSE.md' $package
    Copy-Item (Join-Path $package 'Sonarr.Windows.*') (Join-Path $package 'Sonarr.Update') -Force
    foreach ($pattern in @('Sonarr.Mono.*', 'Openur.Mono.Unix.*', 'libMono.Unix.*')) {
        Get-ChildItem (Join-Path $package $pattern) -ErrorAction SilentlyContinue | Remove-Item -Force
    }

    foreach ($file in @('Sonarr.exe', 'Sonarr.Console.exe', 'ffprobe.exe', 'UI/index.html')) {
        if (-not (Test-Path (Join-Path $package $file))) { throw "Package is missing $file" }
    }

    $archive = Join-Path $root '_artifacts/sonarr-multiseason-win-x64.zip'
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path $archive) { Remove-Item $archive -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($archive, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $package -File -Recurse | ForEach-Object {
            $entry = ('Sonarr/' + $_.FullName.Substring($package.Length + 1)).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }
    Write-Output "Windows package: $archive"
} finally {
    Pop-Location
}
