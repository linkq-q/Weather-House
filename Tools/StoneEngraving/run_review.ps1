param(
    [string]$Python = 'python',
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$stoneRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
Push-Location -LiteralPath $stoneRoot
try {
    & $Python -m unittest discover -s Tools/StoneEngraving -p 'test_*.py'
    if ($LASTEXITCODE -ne 0) { throw 'Map tests failed.' }
    & $Python Tools/StoneEngraving/select_source.py
    if ($LASTEXITCODE -ne 0) { throw 'Source selection failed.' }
    & $Python Tools/StoneEngraving/build_maps.py --spec Tools/StoneEngraving/rain_water.json
    if ($LASTEXITCODE -ne 0) { throw 'Map generation failed.' }
    & $Python Tools/StoneEngraving/prepare_review.py
    if ($LASTEXITCODE -ne 0) { throw 'Review staging failed.' }
    $stoneProject = Join-Path $stoneRoot 'StoneEngravingReview/20261029/UnityProject'
    $stoneLog = Join-Path $stoneRoot 'StoneEngravingReview/20261029/unity-editor.log'
    $stoneArgs = '-batchmode -force-d3d11 -projectPath "{0}" -executeMethod StoneEngravingReviewBuilder.BuildBatch -logFile "{1}"' -f $stoneProject,$stoneLog
    $stoneProcess = Start-Process -FilePath $UnityEditor -ArgumentList $stoneArgs -WindowStyle Hidden -PassThru -Wait
    if ($stoneProcess.ExitCode -ne 0) { throw "Unity review failed. See $stoneLog" }
    Write-Output 'Review ready: StoneEngravingReview/20261029/Captures'
}
finally { Pop-Location }
