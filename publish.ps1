param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "artifacts\publish")
)

$ErrorActionPreference = "Stop"
$displayProject = Join-Path $PSScriptRoot "DisplayMode\DisplayMode.csproj"
$trayProject = Join-Path $PSScriptRoot "DisplayMode.Tray\DisplayMode.Tray.csproj"
$localDotnet = Join-Path $PSScriptRoot ".dotnet-sdk\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$nugetConfig = Join-Path $PSScriptRoot "NuGet.Config"
$buildState = Join-Path $PSScriptRoot ".build-state"
$env:DOTNET_CLI_HOME = Join-Path $buildState "dotnet-cli"
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot ".nuget-packages"
$env:APPDATA = Join-Path $buildState "appdata"
$env:LOCALAPPDATA = Join-Path $buildState "localappdata"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"

& $dotnet restore $displayProject -r win-x64 -p:SelfContained=true --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish $displayProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputPath --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet restore $trayProject -r win-x64 -p:SelfContained=true --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish $trayProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputPath --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published DisplayMode.exe, DisplayMode.App.exe, and profiles.json to:"
Write-Host $OutputPath
