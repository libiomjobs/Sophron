# Builds Engine/ into Engine/build/Release/sophron_engine.dll with the CMake that comes with Visual
# Studio's C++ tools, then runs the engine's own tests. Sophron.csproj runs this whenever the engine's
# sources change; without the C++ tools it says so and lets the rest of Sophron build without sound.
$ErrorActionPreference = 'Stop'

$engine = Join-Path $PSScriptRoot 'Engine'
$build = Join-Path $engine 'build'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
if (-not $vs) {
    Write-Host 'Sophron: Visual Studio has no C++ tools, so the audio engine was not built.'
    exit 0
}

$cmake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ctest = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe'
if (-not (Test-Path $cmake)) {
    Write-Host 'Sophron: Visual Studio has no CMake (the "C++ CMake tools" component), so the audio engine was not built.'
    exit 0
}

if (-not (Test-Path (Join-Path $build 'CMakeCache.txt'))) {
    & $cmake -S $engine -B $build -A x64
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

& $cmake --build $build --config Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }

& $ctest --test-dir $build -C Release --output-on-failure
exit $LASTEXITCODE
