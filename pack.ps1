# Builds both halves of the mod and makes a zip that unpacks straight into the SPT game folder.
# Usage:  .\pack.ps1 [-TarkovDir F:\SPT\] [-Version 0.1.0]
param(
    [string]$TarkovDir = 'F:\SPT\',
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $TarkovDir.EndsWith('\')) { $TarkovDir += '\' }

dotnet build "$root\TradersInRaid\TradersInRaid.csproj" -c Release -nologo -v q "-p:TarkovDir=$TarkovDir"
if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
dotnet build "$root\TradersInRaid.Server\TradersInRaid.Server.csproj" -c Release -nologo -v q "-p:TarkovDir=$TarkovDir"
if ($LASTEXITCODE -ne 0) { throw 'Server build failed' }

$stage = "$root\dist\TradersInRaid-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$client = "$stage\BepInEx\plugins\TradersInRaid"
$server = "$stage\SPT_Runtime\user\mods\TradersInRaid"
New-Item -ItemType Directory -Force $client, $server | Out-Null

Copy-Item "$root\TradersInRaid\bin\Release\TradersInRaid.dll" $client
Copy-Item "$root\TradersInRaid.Server\bin\Release\TradersInRaid.Server.dll" $server
Copy-Item "$root\README.md" $stage

$zip = "$root\dist\TradersInRaid-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Packed $zip"
