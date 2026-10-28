param([switch]$Mutation, [string]$OutputFolder = 'LifeGameplayReview/20261026/photo-flow')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$runtimeDir = Join-Path $PSScriptRoot '.runtime'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
$dotnet = (Get-Command dotnet).Source
$dotnetRoot = Split-Path $dotnet
$sdk = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'sdk') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$framework = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared/Microsoft.NETCore.App') -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$referencePack = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
$referenceDir = Join-Path $referencePack.FullName ('ref/net' + ([version]$referencePack.Name).Major + '.0')
$compiler = Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
$shim = @'
using System;
using System.IO;
using System.Text.Json;
namespace UnityEngine {
 public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
 public static class JsonUtility {
  static JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields=true };
  public static T FromJson<T>(string value)=>JsonSerializer.Deserialize<T>(value,options);
  public static string ToJson(object value)=>JsonSerializer.Serialize(value,value.GetType(),options);
 }
 public class TextAsset { public string text; }
 public static class Resources { public static T Load<T>(string name) where T:class => new TextAsset { text=File.ReadAllText("Assets/Game/Resources/"+name+".json") } as T; }
}
namespace UnityEditor { public class MenuItem:Attribute { public MenuItem(string value){} } }
namespace WeatherHouse.Life {
 public sealed class LifeObjectState { public const float DryThreshold=.18f, WaterVisibleThreshold=.01f; }
 public sealed class LifeContextSnapshot {
  public string speakerId,listenerId,targetId,sequenceId,objectState,weatherState,speakerName,listenerName,locationName;
  public UnityEngine.Vector3 speakerPosition,listenerPosition,anchorPosition;
  public LifeContextSnapshot Copy()=>(LifeContextSnapshot)MemberwiseClone();
 }
}
class Program {
 static int Main(){try{
  var catalog=UnityEngine.JsonUtility.FromJson<WeatherHousePrototype.GlyphCatalog>(File.ReadAllText("Assets/Game/Resources/WeatherHouseGlyphs.json"));
  foreach(string line in WeatherHouse.Life.Editor.LifePhotoFlowChecks.CheckModel(catalog))Console.WriteLine(line);
  Console.WriteLine("Model-only executable uses .NET JSON shim; Unity serializer/rendering checks remain separate.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
'@
$shimPath = Join-Path $runtimeDir 'ModelHarness.cs'
$shim | Set-Content -LiteralPath $shimPath -Encoding utf8
$notebook = Join-Path $projectRoot 'Assets/Game/Scripts/Gameplay/LanguageNotebook.cs'
if ($Mutation) {
    $mutant = Join-Path $runtimeDir 'NotebookWithoutSnapshotCopy.cs'
    (Get-Content -LiteralPath $notebook -Raw).Replace('photoSnapshot = record.photoSnapshot?.Copy()', 'photoSnapshot = record.photoSnapshot') | Set-Content -LiteralPath $mutant -Encoding utf8
    $notebook = $mutant
}
$sources = @($shimPath, $notebook)
$sources += @('Assets/Game/Life/Photography/LifePhotoSnapshot.cs', 'Assets/Game/Life/Photography/LifePhotoRules.cs', 'Assets/Game/Life/Photography/LifePhotoTasks.cs', 'Assets/Game/Life/Editor/LifePhotoFlowChecks.cs') | ForEach-Object { Join-Path $projectRoot $_ }
$dll = Join-Path $runtimeDir 'ModelChecks.dll'
$response = @('/noconfig', '/nostdlib+', '/nologo', '/target:exe', '/langversion:9.0', ('/out:"' + $dll + '"'))
$response += Get-ChildItem -LiteralPath $referenceDir -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' }
$response += $sources | ForEach-Object { '"' + $_ + '"' }
$rsp = Join-Path $runtimeDir 'ModelChecks.rsp'
$response | Set-Content -LiteralPath $rsp -Encoding utf8
& $dotnet $compiler ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
@{runtimeOptions=@{tfm='net10.0';framework=@{name='Microsoft.NETCore.App';version=$framework.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runtimeDir 'ModelChecks.runtimeconfig.json') -Encoding utf8
Push-Location $projectRoot
try {
    $result = @(& $dotnet $dll 2>&1)
    $resultCode = $LASTEXITCODE
    $reportDirectory = Join-Path $projectRoot $OutputFolder
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    $report = Join-Path $reportDirectory $(if ($Mutation) {'mutation-model.txt'} else {'offline-model.txt'})
    $result | Set-Content -LiteralPath $report -Encoding utf8
    $result
    if ($Mutation) { if ($resultCode -eq 0) { throw 'Snapshot-copy mutation was not detected.' }; Write-Output 'PASS expected mutation was rejected.' }
    elseif ($resultCode -ne 0) { exit $resultCode }
} finally { Pop-Location }
