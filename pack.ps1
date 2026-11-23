# Builds both halves of the mod and makes a zip that unpacks straight into the SPT game folder.
# Usage:  .\pack.ps1 [-GameDir F:\SPT\] [-Version 2.1.2]
param(
    [string]$GameDir = 'F:\SPT\',
    [string]$Version = '2.1.2'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $GameDir.EndsWith('\')) { $GameDir += '\' }

dotnet build "$root\ZeroMenuTime\ZeroMenuTime.csproj" -c Release -nologo -v q "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
dotnet build "$root\ZeroMenuTime.Server\ZeroMenuTime.Server.csproj" -c Release -nologo -v q "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }

$stage = "$root\dist\ZeroMenuTime-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$client = "$stage\BepInEx\plugins\ZeroMenuTime"
$server = "$stage\SPT_Runtime\user\mods\ZeroMenuTime"
New-Item -ItemType Directory -Force $client, $server | Out-Null

Copy-Item "$root\ZeroMenuTime\bin\Release\ZeroMenuTime.dll" $client
Copy-Item "$root\ZeroMenuTime.Server\bin\Release\ZeroMenuTime.Server.dll" $server
# The rooms are built in Unity, separately (see Unity\TraderRooms\README.md).
$rooms = "$root\Unity\TraderRooms\Build\traderrooms"
if (Test-Path $rooms) { Copy-Item $rooms $client } else { Write-Warning 'Rooms bundle not built; the zip will give plain rooms.' }
# The guide's pictures: short loops of the mod in use, shown over the main menu.
if (Test-Path "$root\guide") { Copy-Item "$root\guide" $client -Recurse } else { Write-Warning 'No guide pictures; the guide will show words only.' }
Copy-Item "$root\README.md", "$root\LICENSE" $stage

$zip = "$root\dist\ZeroMenuTime-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Entries use forward slashes, which every extractor and mod manager reads (Compress-Archive writes backslashes).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $name = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, 'Optimal') | Out-Null
    }
} finally { $archive.Dispose() }
Write-Host "Packed $zip"

# The co-op add-on goes out on its own: it needs Fika, and BepInEx reports a plugin whose
# requirement is missing as an error. Built only where Fika's DLL is at hand.
$addonProject = "$root\ZeroMenuTime.Fika\ZeroMenuTime.Fika.csproj"
$fika = 'F:\SPT-Fika\host\BepInEx\plugins\Fika\Fika.Core.dll'
if ((Test-Path $addonProject) -and (Test-Path $fika)) {
    dotnet build $addonProject -c Release -nologo -v q "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw 'Co-op add-on build failed' }
    $addonStage = "$root\dist\ZeroMenuTime-Fika-$Version"
    if (Test-Path $addonStage) { Remove-Item $addonStage -Recurse -Force }
    $addonDir = "$addonStage\BepInEx\plugins\ZeroMenuTime"
    New-Item -ItemType Directory -Force $addonDir | Out-Null
    Copy-Item "$root\ZeroMenuTime.Fika\bin\Release\ZeroMenuTime.Fika.dll" $addonDir
    if (Test-Path "$root\docs\coop.md") { Copy-Item "$root\docs\coop.md" "$addonStage\README-coop.md" }
    $addonZip = "$root\dist\ZeroMenuTime-Fika-$Version.zip"
    if (Test-Path $addonZip) { Remove-Item $addonZip -Force }
    $addonArchive = [IO.Compression.ZipFile]::Open($addonZip, 'Create')
    try {
        Get-ChildItem $addonStage -Recurse -File | ForEach-Object {
            $name = $_.FullName.Substring($addonStage.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($addonArchive, $_.FullName, $name, 'Optimal') | Out-Null
        }
    } finally { $addonArchive.Dispose() }
    Write-Host "Packed $addonZip"
} else {
    Write-Host 'Co-op add-on not packed (no Fika.Core.dll to build against).'
}
