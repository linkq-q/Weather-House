param([string]$Unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sample = Join-Path $PSScriptRoot 'UnityProject'
if ((Test-Path $sample) -and !(Test-Path (Join-Path $sample '.animal-toon-review'))) {
    throw 'The target exists without the isolated-review marker. No changes made.'
}
New-Item -ItemType Directory -Force -Path "$sample/Assets/Source", "$sample/Packages", "$sample/ProjectSettings" | Out-Null
Set-Content "$sample/.animal-toon-review" 'Isolated animal toon sample only' -Encoding utf8
Copy-Item "$repo/Assets/Experiments/AnimalToon" "$sample/Assets" -Recurse -Force
Copy-Item "$repo/Assets/Experiments/AnimalToon.meta" "$sample/Assets/AnimalToon.meta" -Force
Copy-Item "$repo/ProjectSettings/ProjectVersion.txt" "$sample/ProjectSettings/ProjectVersion.txt" -Force
Copy-Item "$repo/ProjectSettings/ProjectSettings.asset" "$sample/ProjectSettings/ProjectSettings.asset" -Force
$manifest = Get-Content "$repo/Packages/manifest.json" -Raw | ConvertFrom-Json
foreach ($name in @('com.unity.collab-proxy', 'com.unity.ide.rider', 'com.unity.ide.visualstudio', 'com.unity.ide.vscode')) {
    $manifest.dependencies.PSObject.Properties.Remove($name)
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content "$sample/Packages/manifest.json" -Encoding utf8
Copy-Item "$repo/Packages/packages-lock.json" "$sample/Packages/packages-lock.json" -Force
foreach ($name in @('idle.fbx', 'idle.fbx.meta', 'walk.fbx', 'walk.fbx.meta')) {
    Copy-Item "$repo/Assets/Experiments/QuadrupedMotion/EightAnimals/Models/sheep/$name" "$sample/Assets/Source/$name" -Force
}
foreach ($name in @('sheep.png', 'sheep.png.meta')) {
    Copy-Item "$repo/Assets/Experiments/QuadrupedMotion/EightAnimals/Textures/$name" "$sample/Assets/Source/$name" -Force
}
$arguments = @('-batchmode', '-projectPath', ('"' + $sample + '"'), '-executeMethod',
    'WeatherHouse.AnimalToon.Editor.AnimalToonReview.BuildAndReview', '-logFile', ('"' + $PSScriptRoot + '/unity.log"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Validation Unity PID: $($process.Id). Results: $PSScriptRoot/checks.txt"
