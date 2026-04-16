param([string]$MSBuildPath)
$ErrorActionPreference = 'Stop'
if (-not $MSBuildPath) {
    $locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $locator)) { throw 'Specify -MSBuildPath or install Visual Studio Build Tools.' }
    $MSBuildPath = & $locator -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $MSBuildPath) { throw 'MSBuild was not found.' }
& $MSBuildPath (Join-Path $PSScriptRoot 'GNDriveV3.Core.Tests.csproj') /t:Rebuild /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Core test build failed.' }
& (Join-Path $PSScriptRoot 'bin\Release\GNDriveV3.Core.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& $MSBuildPath (Join-Path $PSScriptRoot 'GNDriveV3.Adapter.Tests.csproj') /t:Rebuild /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Resource adapter test build failed.' }
& (Join-Path $PSScriptRoot 'bin\Release\GNDriveV3.Adapter.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Resource adapter tests failed.' }
