$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$nativeSource = Join-Path $root 'native'
$nativeBuild = Join-Path $nativeSource 'build'
$output = Join-Path $root 'publish-neo'
$cliProject = Join-Path $root 'src\NetworkDoctor.Cli\NetworkDoctor.Cli.csproj'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($cmake)) {
    $cmake = 'C:\Program Files\CMake\bin\cmake.exe'
}
if (-not (Test-Path -LiteralPath $cmake)) {
    throw 'CMake was not found.'
}
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer was not found.'
}

$installation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($installation)) {
    throw 'Visual Studio C++ tools were not found.'
}
$devCmd = Join-Path $installation 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $devCmd)) {
    throw "VsDevCmd.bat was not found: $devCmd"
}

New-Item -ItemType Directory -Path $nativeBuild -Force | Out-Null
New-Item -ItemType Directory -Path $output -Force | Out-Null

$configureCommand = 'call "' + $devCmd + '" -arch=x64 && "' + $cmake + '" -S "' + $nativeSource + '" -B "' + $nativeBuild + '" -G "Visual Studio 17 2022" -A x64 -DEUI_ENABLE_TRAY=OFF'
& cmd.exe /d /s /c $configureCommand
if ($LASTEXITCODE -ne 0) {
    throw "EUI-NEO configure failed with exit code $LASTEXITCODE"
}

$buildCommand = 'call "' + $devCmd + '" -arch=x64 && "' + $cmake + '" --build "' + $nativeBuild + '" --config Release --parallel'
& cmd.exe /d /s /c $buildCommand
if ($LASTEXITCODE -ne 0) {
    throw "EUI-NEO build failed with exit code $LASTEXITCODE"
}

& dotnet publish $cliProject -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) {
    throw "CLI publish failed with exit code $LASTEXITCODE"
}

$release = Join-Path $nativeBuild 'Release'
Get-ChildItem -LiteralPath $release -Force | Copy-Item -Destination $output -Force -Recurse
$nativeExecutable = Join-Path $output 'NetworkDoctorNeo.exe'
if (-not (Test-Path -LiteralPath $nativeExecutable)) {
    throw "Native executable was not found: $nativeExecutable"
}
$cliExecutable = Join-Path $output 'NetworkDoctor.Cli.exe'
if (-not (Test-Path -LiteralPath $cliExecutable)) {
    throw "CLI executable was not found: $cliExecutable"
}

Write-Output "EUI-NEO publish completed: $nativeExecutable"
