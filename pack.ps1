# Builds both halves of the mod and makes a zip that unpacks straight into the SPT game folder.
# Usage:  .\pack.ps1 [-GameDir F:\SPT\] [-Version 1.2.4]
param(
    [string]$GameDir = 'F:\SPT\',
    [string]$Version = '1.2.4'
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
