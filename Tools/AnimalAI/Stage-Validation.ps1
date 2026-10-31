$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$validation=Join-Path $projectRoot 'Temp/AnimalAIValidation'
foreach($folder in @('Assets','Packages','ProjectSettings')){New-Item -ItemType Directory -Force -Path (Join-Path $validation $folder)|Out-Null}
$sources=@(Get-ChildItem (Join-Path $projectRoot 'Assets') -Filter '*.cs' -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/]Editor[\\/]' -and $_.FullName -notmatch '[\\/][^\\/]*~[\\/]'
} | ForEach-Object {$_.FullName})
$sources+=@('Assets/Game/World/Wildlife/Editor/AnimalAutonomyChecks.cs',
    'Assets/Game/World/Wildlife/Editor/AnimalAutonomyPlayChecks.cs',
    'Assets/Game/World/Wildlife/Editor/WildlifeChecks.cs',
    'Assets/Game/World/Wildlife/Journey/Editor/JourneyNavigationChecks.cs',
    'Assets/Game/World/Wildlife/Journey/Editor/JourneyCrowdPathChecks.cs')|ForEach-Object {Join-Path $projectRoot $_}
foreach($source in $sources){
    $relative=$source.Substring($projectRoot.Length+1)
    $destination=Join-Path $validation $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination)|Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json'),(Join-Path $projectRoot 'Packages/packages-lock.json') -Destination (Join-Path $validation 'Packages')
Copy-Item -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $validation 'ProjectSettings')
Write-Output ('Staged '+$sources.Count+' scripts; no scenes, saves, credentials or project assets copied.')
