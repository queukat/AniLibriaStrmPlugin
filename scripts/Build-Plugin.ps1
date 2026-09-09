#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version,
    [string] $OutputDirectory = 'build/packages'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot -Parent
Push-Location $repository
try {
    [xml] $project = Get-Content 'AniLibertyStrmPlugin.csproj' -Raw
    $framework = [string] $project.Project.PropertyGroup.TargetFramework
    $abiMatch = [regex]::Match((Get-Content 'build.yaml' -Raw), '(?m)^targetAbi:\s*"([^"]+)"')
    if (-not $abiMatch.Success) { throw 'build.yaml must declare targetAbi.' }
    $abi = $abiMatch.Groups[1].Value
    if (($framework -eq 'net9.0' -and $abi -ne '10.11.0.0') -or
        ($framework -eq 'net10.0' -and $abi -ne '12.0.0.0') -or
        $framework -notin @('net9.0', 'net10.0')) {
        throw "Unsupported framework/ABI pair: $framework / $abi"
    }

    $destination = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $packageName = "AniLibertyStrmPlugin_${Version}_jellyfin-${abi}"
    $zipPath = Join-Path $destination "$packageName.zip"
    $receiptPath = Join-Path $destination "$packageName.json"
    $checksumPath = "$zipPath.sha256"
    foreach ($path in @($zipPath, $receiptPath, $checksumPath)) {
        if (Test-Path -LiteralPath $path) { throw "Output already exists; choose another output directory: $path" }
    }
    $workspace = Join-Path $repository ('build/package-' + [guid]::NewGuid().ToString('N'))
    $publish = Join-Path $workspace 'publish'
    $stage = Join-Path $workspace 'package'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    dotnet restore AniLibertyStrmPlugin.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    dotnet publish AniLibertyStrmPlugin.csproj --no-restore -c Release -o $publish `
        "-p:Version=$Version" "-p:AssemblyVersion=$Version" "-p:FileVersion=$Version" `
        "-p:InformationalVersion=$Version" '-p:IncludeSourceRevisionInInformationalVersion=false'
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    # Package only the declared plugin payload, never Jellyfin's own shared assemblies.
    $files = @('AniLibertyStrmPlugin.dll', 'Microsoft.Extensions.Http.Polly.dll',
        'Polly.dll', 'Polly.Core.dll', 'Polly.Extensions.Http.dll', 'icon.png')
    $hashes = [ordered]@{}
    foreach ($file in $files) {
        $inputPath = Join-Path $publish $file
        if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Missing artifact: $file" }
        Copy-Item -LiteralPath $inputPath -Destination (Join-Path $stage $file)
        $hashes[$file] = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
    }
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $stage $files[0])).Version.ToString()
    if ($assemblyVersion -ne $Version) { throw "Assembly version mismatch: $assemblyVersion" }
    $metadataPath = Join-Path $stage 'meta.json'
    [ordered]@{
        guid = 'cce0798d-c8b7-4265-b08c-dc9e7bd3fc0f'
        name = 'AniLiberty STRM Plugin'
        version = $Version
        targetAbi = $abi
        status = 'Active'
        autoUpdate = $true
    } | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8
    $files += 'meta.json'
    $hashes['meta.json'] = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash
    Compress-Archive -LiteralPath @($files | ForEach-Object { Join-Path $stage $_ }) -DestinationPath $zipPath
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    "$zipHash  $packageName.zip" | Set-Content -LiteralPath $checksumPath -Encoding utf8
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    $trackedChanges = git status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source state.' }
    [ordered]@{
        version = $Version
        targetAbi = $abi
        targetFramework = $framework
        sourceCommit = $commit.Trim()
        trackedSourceModified = [bool] $trackedChanges
        zip = "$packageName.zip"
        sha256 = $zipHash
        files = $hashes
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding utf8
    Write-Output "Package: $zipPath"
    Write-Output "Manifest: $receiptPath"
}
finally {
    Pop-Location
}
