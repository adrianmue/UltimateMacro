param([switch]$Verify, [switch]$Package)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:APPDATA = Join-Path $PSScriptRoot '.build-home\AppData'
$env:LOCALAPPDATA = Join-Path $PSScriptRoot '.build-home\LocalAppData'
dotnet publish UltimateMacro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetAudit=false -p:RestoreConfigFile=NuGet.Config -p:RestorePackagesPath=.packages -o dist
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'dist\README.md') -Force
if ($Verify) {
    $verification = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\UltimateMacro.exe') -ArgumentList '--self-test' -WindowStyle Hidden -PassThru
    $verification.WaitForExit()
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dist\self-test-results.txt')
    if ($verification.ExitCode -ne 0) { throw 'Verification failed.' }
}
if ($Package) {
    $releaseDirectory = Join-Path $PSScriptRoot 'release'
    $null = New-Item -Path $releaseDirectory -ItemType Directory -Force
    $archivePath = Join-Path $releaseDirectory 'UltimateMacro-Windows-x64.zip'
    $packageFiles = @('dist\UltimateMacro.exe', 'dist\README.md', 'dist\Examples')
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $archivePath -Force
    Write-Host 'Ready: release\UltimateMacro-Windows-x64.zip'
}
Write-Host 'Ready: dist\UltimateMacro.exe'
