# Builds both halves of the mod and makes a zip that unpacks straight into the SPT game folder.
# Usage:  .\pack.ps1 [-TarkovDir F:\SPT\] [-Version 0.2.0]
param(
    [string]$TarkovDir = 'F:\SPT\',
    [string]$Version = '0.2.0'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $TarkovDir.EndsWith('\')) { $TarkovDir += '\' }

dotnet build "$root\TheTrueSPT\TheTrueSPT.csproj" -c Release -nologo -v q "-p:TarkovDir=$TarkovDir"
if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
dotnet build "$root\TheTrueSPT.Server\TheTrueSPT.Server.csproj" -c Release -nologo -v q "-p:TarkovDir=$TarkovDir"
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }

$stage = "$root\dist\TheTrueSPT-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$client = "$stage\BepInEx\plugins\TheTrueSPT"
$server = "$stage\SPT_Runtime\user\mods\TheTrueSPT"
New-Item -ItemType Directory -Force $client, $server | Out-Null

Copy-Item "$root\TheTrueSPT\bin\Release\TheTrueSPT.dll" $client
Copy-Item "$root\TheTrueSPT.Server\bin\Release\TheTrueSPT.Server.dll" $server
# The rooms are built in Unity, separately (see Unity\TraderRooms\README.md).
$rooms = "$root\Unity\TraderRooms\Build\traderrooms"
if (Test-Path $rooms) { Copy-Item $rooms $client } else { Write-Warning 'Rooms bundle not built; the zip will give plain rooms.' }
Copy-Item "$root\README.md" $stage

$zip = "$root\dist\TheTrueSPT-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Packed $zip"
