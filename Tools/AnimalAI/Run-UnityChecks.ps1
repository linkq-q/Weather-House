param([ValidateSet('Contracts','Play','Glyph')][string]$Mode='Contracts')
$ErrorActionPreference='Stop'
$projectRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$validation=Join-Path $projectRoot 'Temp/AnimalAIValidation'
$running=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {$_.CommandLine -like '*AnimalAIValidation*'}
if($running){throw 'The isolated animal validation project is already running.'}
& (Join-Path $PSScriptRoot 'Stage-Validation.ps1')
$kind=$Mode.ToLowerInvariant()
$env:WEATHERHOUSE_ANIMAL_REPORT=Join-Path $validation ('checks-'+$kind+'.txt')
$entry=if($Mode -eq 'Play'){'WeatherHouse.Hillside.Editor.AnimalAutonomyPlayChecks.BatchPlay'}elseif($Mode -eq 'Glyph'){'WeatherHouse.Hillside.Editor.AnimalAutonomyChecks.BatchGlyph'}else{'WeatherHouse.Hillside.Editor.AnimalAutonomyChecks.Batch'}
$arguments=@('-batchmode','-force-d3d11','-projectPath',('"'+$validation+'"'),'-executeMethod',$entry,'-logFile',('"'+$validation+'/unity-'+$kind+'.log"'))
$process=Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3c1/Editor/Unity.exe' -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if(Test-Path -LiteralPath $env:WEATHERHOUSE_ANIMAL_REPORT){Get-Content -LiteralPath $env:WEATHERHOUSE_ANIMAL_REPORT}
exit $process.ExitCode
