param(
    [string]$GamePath = "E:\SteamLibrary\steamapps\common\Warhammer 40,000 Rogue Trader",
    [string]$UnityModManagerDll = "$env:USERPROFILE\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\UnityModManager.dll",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = (Get-Content -Raw -Path (Join-Path $root "Info.json") | ConvertFrom-Json).Version
$managed = Join-Path $GamePath "WH40KRT_Data\Managed"
$source = Join-Path $root "Source\FerrumSanctumTamer.cs"
$bin = Join-Path $root "bin"
$dist = Join-Path $root "dist"
$release = Join-Path $root "release\FerrumSanctumTamer"
$outDll = Join-Path $bin "FerrumSanctumTamer.dll"
$zip = Join-Path $dist "FerrumSanctumTamer-$version.zip"
$rsp = Join-Path $bin "FerrumSanctumTamer.csc.rsp"

if (!(Test-Path -LiteralPath $managed)) {
    throw "Game managed assembly folder not found: $managed"
}

if (!(Test-Path -LiteralPath $UnityModManagerDll)) {
    throw "UnityModManager.dll not found: $UnityModManagerDll"
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetCandidates = @()
if ($dotnetCommand) {
    $dotnetCandidates += $dotnetCommand.Source
}
$dotnetCandidates += Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
if (${env:ProgramFiles(x86)}) {
    $dotnetCandidates += Join-Path ${env:ProgramFiles(x86)} "dotnet\dotnet.exe"
}
$dotnetCandidates = $dotnetCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

$dotnetExe = $null
$sdkRoot = $null
foreach ($candidate in $dotnetCandidates) {
    $sdks = & $candidate --list-sdks
    if ($sdks) {
        $sdkRoot = $sdks |
            ForEach-Object { ($_ -split " ")[0] } |
            Sort-Object {[version]$_} |
            Select-Object -Last 1
        $dotnetExe = $candidate
        break
    }
}

if (!$sdkRoot) {
    throw "No .NET SDK found. Install .NET SDK 8 or newer."
}

$dotnetRoot = Split-Path -Parent $dotnetExe
$csc = Join-Path $dotnetRoot "sdk\$sdkRoot\Roslyn\bincore\csc.dll"
if (!(Test-Path -LiteralPath $csc)) {
    throw "Roslyn compiler not found: $csc"
}

New-Item -ItemType Directory -Force -Path $bin, $dist, $release | Out-Null

$referenceFiles = @(
    "mscorlib.dll",
    "netstandard.dll",
    "System.dll",
    "System.Core.dll",
    "Utility.Rounds.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "0Harmony.dll",
    "UniRx.dll",
    "Owlcat.Runtime.UI.dll",
    "Owlcat.Runtime.Core.dll",
    "RogueTrader.GameCore.dll",
    "Code.dll"
) | ForEach-Object { Join-Path $managed $_ }

$missing = $referenceFiles | Where-Object { !(Test-Path -LiteralPath $_) }
if ($missing) {
    throw "Missing required game assemblies:`n$($missing -join "`n")"
}

$compilerArgs = @(
    "/target:library",
    "/nologo",
    "/nostdlib",
    "/langversion:latest",
    "/optimize+",
    "/out:`"$outDll`""
)
$compilerArgs += @($referenceFiles + $UnityModManagerDll) | ForEach-Object { "/reference:`"$_`"" }
$compilerArgs += "`"$source`""
$compilerArgs | Set-Content -Path $rsp -Encoding ASCII

& $dotnetExe $csc "@$rsp"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Remove-Item -LiteralPath $release -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $release | Out-Null
Copy-Item -LiteralPath $outDll, (Join-Path $root "Info.json"), (Join-Path $root "README.md"), (Join-Path $root "LICENSE") -Destination $release -Force

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Compress-Archive -Path (Join-Path $release "*") -DestinationPath $zip -Force
Write-Host "Built $outDll"
Write-Host "Packaged $zip"
