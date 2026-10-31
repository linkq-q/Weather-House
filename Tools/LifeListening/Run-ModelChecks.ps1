param([string]$ReportName = 'offline-model.txt')
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
using System.Text.Json;
namespace UnityEngine {
 public class MonoBehaviour {}
 public class DisallowMultipleComponent:Attribute {}
 public struct Vector3 { public float x,y,z; }
 public static class Mathf { public static int Clamp(int x,int min,int max)=>Math.Min(max,Math.Max(min,x)); }
 public static class JsonUtility {
  static JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields=true };
  public static T FromJson<T>(string value)=>JsonSerializer.Deserialize<T>(value,options);
  public static string ToJson(object value)=>JsonSerializer.Serialize(value,value.GetType(),options);
 }
 public static class Debug { public static void Log(object value)=>Console.WriteLine(value); }
}
namespace UnityEditor { public class MenuItem:Attribute { public MenuItem(string value){} } }
namespace WeatherHousePrototype {
 public class AnimalActor {}
 public class JudgementResult { public bool success,understood; }
}
class Program {
 static int Main(){try{
  foreach(string line in WeatherHouse.Life.Editor.LifeListeningChecks.CheckModel())Console.WriteLine(line);
  Console.WriteLine("Model-only executable with .NET JSON shim; Unity rendering and Play behavior remain separate.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
'@
$shimPath = Join-Path $runtimeDir 'ModelHarness.cs'
$shim | Set-Content -LiteralPath $shimPath -Encoding utf8
# Compile the production interaction selector without its unrelated scene implementation.
$gathering = Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/Game/Life/World/LifeWorld.Gathering.cs') -Raw
$selector = $gathering.Substring(0, $gathering.IndexOf('    public sealed partial class LifeWorld')) + '}'
$selectorPath = Join-Path $runtimeDir 'LifeInteractionRules.cs'
$selector | Set-Content -LiteralPath $selectorPath -Encoding utf8
$sources = @($shimPath, $selectorPath)
$sources += @('Assets/Game/Life/Core/LifeState.cs', 'Assets/Game/Life/Content/LifeContent.cs', 'Assets/Game/Life/Editor/LifeListeningChecks.cs') | ForEach-Object { Join-Path $projectRoot $_ }
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
    $reportDirectory = Join-Path $projectRoot 'LifeGameplayReview/20261028/feedback/listening'
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    $result | Set-Content -LiteralPath (Join-Path $reportDirectory $ReportName) -Encoding utf8
    $result
    exit $resultCode
} finally { Pop-Location }
