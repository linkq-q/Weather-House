param(
    [string]$Python = 'python',
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$stoneRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
Push-Location -LiteralPath $stoneRoot
try {
    & $Python -m unittest discover -s Tools/StoneEngraving -p test_build_maps.py
    if($LASTEXITCODE -ne 0){throw 'Base map tests failed.'}
    & $Python -m unittest discover -s Tools/StoneEngraving/BatchProduction -p test_prepare.py
    if($LASTEXITCODE -ne 0){throw 'Glyph and edge tests failed.'}
    & $Python Tools/StoneEngraving/BatchProduction/stage.py
    if($LASTEXITCODE -ne 0){throw 'Review staging failed.'}
    foreach($stoneStep in @('Measure','Build')) {
        if($stoneStep -eq 'Build') {
            & $Python Tools/StoneEngraving/BatchProduction/prepare.py
            if($LASTEXITCODE -ne 0){throw 'Map generation failed.'}
        }
        $stoneArgs='-batchmode -force-d3d11 -projectPath "{0}" -executeMethod StoneProductionBuilder.{1} -logFile "{2}"' -f
            (Join-Path $stoneRoot 'StoneEngravingReview/20261029/UnityProject'),$stoneStep,
            (Join-Path $stoneRoot "StoneEngravingReview/20261029/SevenStoneProduction-$stoneStep.log")
        $stoneProcess=Start-Process -FilePath $UnityEditor -ArgumentList $stoneArgs -WindowStyle Hidden -PassThru -Wait
        if($stoneProcess.ExitCode -ne 0){throw "Unity $stoneStep failed. Inspect its log."}
    }
    & $Python Tools/StoneEngraving/BatchProduction/package.py
    if($LASTEXITCODE -ne 0){throw 'Review packaging failed.'}
    Write-Output 'Seven stone assets rendered in isolated project. Main assets and scene were not overwritten.'
}
finally {Pop-Location}
