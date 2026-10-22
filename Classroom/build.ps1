$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.downloads/cli-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.downloads/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = Join-Path $PSScriptRoot '.dotnet/dotnet.exe'
if (!(Test-Path $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
foreach ($role in @('Teacher', 'Student')) {
    & $sdk publish (Join-Path $PSScriptRoot "$role/$role.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $PSScriptRoot "releases/$role")
    if ($LASTEXITCODE -ne 0) { throw "Сборка $role завершилась ошибкой" }
    $folder = Join-Path $PSScriptRoot "releases/v2/$role"
    & $sdk publish (Join-Path $PSScriptRoot "$role/$role.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $folder
    if ($LASTEXITCODE -ne 0) { throw "Folder publish failed: $role" }
    Copy-Item -Path (Join-Path $PSScriptRoot 'Deployment/configure-firewall.*') -Destination $folder -Force
    if ($role -eq 'Student') { Copy-Item -Path (Join-Path $PSScriptRoot 'Deployment/Student/*') -Destination $folder -Force }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-v2.md') -Destination (Join-Path $folder 'README.txt') -Force
    Compress-Archive -Path (Join-Path $folder '*') -DestinationPath (Join-Path $PSScriptRoot "releases/v2/$role-x64.zip") -Force
}
