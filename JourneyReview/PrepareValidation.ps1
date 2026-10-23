param([string]$Source = 'C:\unity project\Weather House', [string]$Destination = '')
$ErrorActionPreference = 'Stop'
if (-not $Destination) { $Destination = Join-Path $env:TEMP ('WeatherHouseJourney-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$Source = [IO.Path]::GetFullPath($Source)
$Destination = [IO.Path]::GetFullPath($Destination)
if ($Destination -eq $Source) { throw 'Validation must use a separate project.' }
New-Item -ItemType Directory -Force -Path $Destination, (Join-Path $Destination 'Assets'), (Join-Path $Destination 'Packages') | Out-Null
Copy-Item -LiteralPath (Join-Path $Source 'ProjectSettings') -Destination $Destination -Recurse -Force
$dependencies = [ordered]@{}
$needed = @('com.unity.render-pipelines.universal','com.unity.render-pipelines.core','com.unity.render-pipelines.universal-config','com.unity.shadergraph','com.unity.searcher','com.unity.burst','com.unity.mathematics','com.unity.ugui')
foreach ($package in Get-ChildItem -LiteralPath (Join-Path $Source 'Library/PackageCache') -Directory) {
    $id = $package.Name.Split('@')[0]
    if ($needed -contains $id) { $dependencies[$id] = 'file:' + $package.FullName.Replace('\','/') }
}
$manifest = Get-Content -LiteralPath (Join-Path $Source 'Packages/manifest.json') -Raw | ConvertFrom-Json
foreach ($property in $manifest.dependencies.PSObject.Properties) {
    if ($property.Name.StartsWith('com.unity.modules.')) { $dependencies[$property.Name] = $property.Value }
}
@{dependencies=$dependencies} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination 'Packages/manifest.json') -Encoding utf8
$guidMap = @{}
Push-Location $Source
try {
    foreach ($line in (& rg '^guid: ' Assets -g '*.meta')) {
        if ($line -match '^(.*)\.meta:guid: ([a-f0-9]{32})$') { $guidMap[$Matches[2]] = $Matches[1] }
    }
    $queue = [Collections.Generic.Queue[string]]::new()
    $copied = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($p in @('Assets/Game/World/Hillside/Generated/sheep.prefab','Assets/Game/Resources/WeatherHouseGlyphs.json','Assets/Game/UI/AnimalGlyphGraphic.cs','Assets/Game/Scripts/Gameplay/LanguageNotebook.cs','Assets/Game/World/Wildlife/Journey/Animation/sheep_swim.fbx')) { $queue.Enqueue($p) }
    foreach ($file in Get-ChildItem -LiteralPath 'Assets/Game/World/Wildlife/Runtime' -File -Filter '*.cs') { $queue.Enqueue([IO.Path]::GetRelativePath($Source,$file.FullName)) }
    foreach ($file in Get-ChildItem -LiteralPath 'Assets/Game/World/Wildlife/Journey' -File -Recurse -Filter '*.cs') { $queue.Enqueue([IO.Path]::GetRelativePath($Source,$file.FullName)) }
    $water = 'Assets/Game/World/WorldWaterSurface.cs'
    if (Test-Path -LiteralPath $water) { $queue.Enqueue($water) }
    $layout = 'Assets/Game/World/WatersideV2/WatersideLayout.cs'
    if (Test-Path -LiteralPath $layout) { $queue.Enqueue($layout) }
    foreach ($settings in @('ProjectSettings/GraphicsSettings.asset','ProjectSettings/QualitySettings.asset')) {
        $text = Get-Content -LiteralPath $settings -Raw
        foreach ($match in [regex]::Matches($text,'guid: ([a-f0-9]{32})')) { if ($guidMap.ContainsKey($match.Groups[1].Value)) { $queue.Enqueue($guidMap[$match.Groups[1].Value]) } }
    }
    while ($queue.Count -gt 0) {
        $relative = $queue.Dequeue().Replace('\','/')
        if (-not $copied.Add($relative)) { continue }
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $relative -Destination $target -Force
        if (Test-Path -LiteralPath ($relative + '.meta')) { Copy-Item -LiteralPath ($relative + '.meta') -Destination ($target + '.meta') -Force }
        if ([IO.Path]::GetExtension($relative) -in @('.prefab','.mat','.asset','.controller','.overrideController','.unity','.shadergraph','.shadersubgraph')) {
            $text = Get-Content -LiteralPath $relative -Raw
            foreach ($match in [regex]::Matches($text,'(?:guid: |"guid"\s*:\s*")([a-f0-9]{32})')) { if ($guidMap.ContainsKey($match.Groups[1].Value)) { $queue.Enqueue($guidMap[$match.Groups[1].Value]) } }
        }
    }
    [pscustomobject]@{project=$Destination;files=$copied.Count;packages=$dependencies.Count} | ConvertTo-Json
}
finally { Pop-Location }
