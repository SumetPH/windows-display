param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "artifacts\publish"),
    # Start the tray app after publishing even if it was not running before.
    [switch]$Run
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

# Windows locks DisplayMode.App.exe while it runs, so close the published copy first
# and start it again afterwards.
$trayExe = Join-Path ([IO.Path]::GetFullPath($OutputPath)) "DisplayMode.App.exe"
$running = @(Get-Process -Name "DisplayMode.App" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path -ieq $trayExe })
if ($running.Count -gt 0) {
    Write-Host "Closing running DisplayMode.App.exe..."
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

& $dotnet restore $displayProject -r win-x64 -p:SelfContained=true --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish $displayProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputPath --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet restore $trayProject -r win-x64 -p:SelfContained=true --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet publish $trayProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputPath --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published DisplayMode.exe, DisplayMode.App.exe, and profiles.example.json to:"
Write-Host $OutputPath

if ($Run -or $running.Count -gt 0) {
    Write-Host "Starting DisplayMode.App.exe..."
    Start-Process -FilePath $trayExe -WorkingDirectory $OutputPath
}
