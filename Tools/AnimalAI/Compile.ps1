$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output=Join-Path $projectRoot 'Temp/AnimalAICompile'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$compiler='C:/Program Files/dotnet/sdk/10.0.300/Roslyn/bincore/csc.dll'
[xml]$project=Get-Content (Join-Path $projectRoot 'Assembly-CSharp.csproj')
$defines=@($project.Project.PropertyGroup.DefineConstants | Where-Object {$_})[0]
$references=@($project.Project.ItemGroup.Reference.HintPath | Where-Object {$_} | ForEach-Object {
    if([IO.Path]::IsPathRooted($_)){$_}else{Join-Path $projectRoot $_}
})
$sources=@(Get-ChildItem (Join-Path $projectRoot 'Assets') -Filter '*.cs' -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/]Editor[\\/]' -and $_.FullName -notmatch '[\\/][^\\/]*~[\\/]'
} | ForEach-Object {$_.FullName})
$response=@('/noconfig','/nostdlib+','/nologo','/target:library','/langversion:9.0',('/define:'+$defines),('/out:"'+$output+'/Assembly-CSharp.dll"'))
$response+=$references | ForEach-Object {'/reference:"'+$_+'"'}
$response+=$sources | ForEach-Object {'"'+$_+'"'}
$rsp=Join-Path $output 'runtime.rsp';$response | Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet $compiler ('@'+$rsp)
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$response=@('/noconfig','/nostdlib+','/nologo','/target:library','/langversion:9.0','/define:UNITY_EDITOR',('/out:"'+$output+'/AnimalAIChecks.dll"'),('/reference:"'+$output+'/Assembly-CSharp.dll"'))
$response+=$references | ForEach-Object {'/reference:"'+$_+'"'}
$response+=@('Assets/Game/World/Wildlife/Editor/AnimalAutonomyChecks.cs',
    'Assets/Game/World/Wildlife/Editor/AnimalAutonomyPlayChecks.cs',
    'Assets/Game/World/Wildlife/Editor/WildlifeChecks.cs',
    'Assets/Game/World/Wildlife/Journey/Editor/JourneyNavigationChecks.cs',
    'Assets/Game/World/Wildlife/Journey/Editor/JourneyCrowdPathChecks.cs')|ForEach-Object {'"'+$projectRoot+'/'+$_+'"'}
$rsp=Join-Path $output 'checks.rsp';$response | Set-Content -LiteralPath $rsp -Encoding utf8
& dotnet $compiler ('@'+$rsp)
exit $LASTEXITCODE
