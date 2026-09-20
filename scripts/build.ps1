param(
    [string]$DotnetPath = 'dotnet',
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskApp = Join-Path $taskRoot 'src/BattleNetRegionSwitcher.App/BattleNetRegionSwitcher.App.csproj'
$taskTests = Join-Path $taskRoot 'tests/BattleNetRegionSwitcher.CoreTests/BattleNetRegionSwitcher.CoreTests.csproj'
Push-Location $taskRoot
try {
    & $DotnetPath build $taskApp -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $DotnetPath run --project $taskTests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Publish) {
        $taskOutput = Join-Path $taskRoot 'artifacts/win-x64'
        & $DotnetPath publish $taskApp -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $taskOutput --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $taskOutput '使用说明.md') -Force
        Write-Output "Published to $taskOutput"
    }
} finally { Pop-Location }
