param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "DisplayMode.sln"
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

& $dotnet restore $solution --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build $solution -c $Configuration --no-restore
exit $LASTEXITCODE
