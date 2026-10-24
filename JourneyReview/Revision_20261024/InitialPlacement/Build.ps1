param(
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Data',
    [string]$RuntimeAssembly
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
if (-not $RuntimeAssembly) { $RuntimeAssembly = Join-Path $projectRoot 'Library/ScriptAssemblies/Assembly-CSharp.dll' }
$outputPath = Join-Path $PSScriptRoot 'WildlifeInitialPlacementAudit.dll'
$rspPath = Join-Path $PSScriptRoot 'compile.rsp'
$references = @((Join-Path $UnityData 'NetStandard/ref/2.1.0/netstandard.dll'), $RuntimeAssembly)
$references += Get-ChildItem -LiteralPath (Join-Path $UnityData 'Managed/UnityEngine') -Filter '*.dll' | ForEach-Object { $_.FullName }
$arguments = @('/nologo', '/target:library', '/langversion:9.0', '/nostdlib+', '/define:UNITY_EDITOR;UNITY_2022_3')
$arguments += $references | ForEach-Object { '/reference:"' + $_ + '"' }
$arguments += '/out:"' + $outputPath + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'WildlifeInitialPlacementAudit.cs') + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'WildlifeInitialPlacementSelfChecks.cs') + '"'
[IO.File]::WriteAllLines($rspPath, $arguments, (New-Object Text.UTF8Encoding($false)))
& (Join-Path $UnityData 'NetCoreRuntime/dotnet.exe') (Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll') ('@' + $rspPath) 2>&1 |
    Tee-Object -FilePath (Join-Path $PSScriptRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { throw "External audit compilation failed: $LASTEXITCODE" }
Get-FileHash -LiteralPath $outputPath -Algorithm SHA256
