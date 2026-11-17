param(
    [Parameter(Mandatory)]
    [ValidateSet('Install', 'Reinstall')]
    [string] $Mode,
    [string] $GameDirectory = $env:usage_game,
    [string] $PackageDirectory = $env:usage_package
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Install-Bsipa {
    Assert-GameDirectory
    if ($Mode -eq 'Install') {
        Assert-InstallPackage
    } else {
        Assert-InstalledBsipa
    }

    $loader = Build-Loader
    Assert-GameStopped
    if ($Mode -eq 'Install') {
        Install-CompletePackage $loader
    } else {
        Update-InstalledLoader $loader
    }

    Assert-InstalledLoader $loader
    Write-Host "BSIPA $Mode completed: $GameDirectory"
}

function Install-CompletePackage([string] $Loader) {
    Get-ChildItem -LiteralPath $PackageDirectory | Copy-Item -Destination $GameDirectory -Recurse -Force
    Copy-Item -LiteralPath $Loader -Destination (Join-Path $GameDirectory 'IPA/Data/Managed/IPA.Loader.dll') -Force

    Invoke-Installer
}

function Invoke-Installer {
    Push-Location -LiteralPath $GameDirectory
    try {
        & (Join-Path $GameDirectory 'IPA.exe') (Join-Path $GameDirectory 'Beat Saber.exe') --nowait
        if ($LASTEXITCODE -ne 0) { throw "BSIPA installer failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
}

function Update-InstalledLoader([string] $Loader) {
    foreach ($relativePath in @('IPA/Data/Managed/IPA.Loader.dll', 'Beat Saber_Data/Managed/IPA.Loader.dll')) {
        Copy-Item -LiteralPath $Loader -Destination (Join-Path $GameDirectory $relativePath) -Force
    }
}

function Assert-GameDirectory {
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) { throw 'Specify --game or set GameDirectory in mise.local.toml.' }
    foreach ($relativePath in @('Beat Saber.exe', 'Beat Saber_Data/Managed/UnityEngine.CoreModule.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory $relativePath) -PathType Leaf)) {
            throw "Not a Beat Saber installation: missing $relativePath."
        }
    }
    $script:GameDirectory = (Resolve-Path -LiteralPath $GameDirectory).Path
    Assert-GameStopped
}

function Assert-GameStopped {
    if (Get-Process 'Beat Saber' -ErrorAction SilentlyContinue) { throw 'Close Beat Saber before installing BSIPA.' }
}

function Assert-InstallPackage {
    if (Test-Path -LiteralPath (Join-Path $GameDirectory 'winhttp.dll')) {
        throw 'This game already has a proxy installed. Use reinstall for an existing BSIPA installation.'
    }
    if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { throw 'Specify --package or set BSIPA_PACKAGE_DIR in mise.local.toml.' }
    foreach ($relativePath in @('IPA.exe', 'IPA/winhttp.dll', 'IPA/Data/Managed/IPA.Injector.dll', 'IPA/Data/Managed/IPA.Loader.dll', 'IPA/Libs/Mono.Cecil.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory $relativePath) -PathType Leaf)) {
            throw "Incomplete BSIPA package: missing $relativePath."
        }
    }
}

function Assert-InstalledBsipa {
    foreach ($relativePath in @('winhttp.dll', 'Beat Saber_Data/Managed/IPA.Injector.dll', 'IPA/Data/Managed/IPA.Loader.dll', 'Beat Saber_Data/Managed/IPA.Loader.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory $relativePath) -PathType Leaf)) {
            throw "BSIPA is not installed: missing $relativePath. Run install first."
        }
    }
}

function Build-Loader {
    $repository = Split-Path $PSScriptRoot -Parent
    $buildArgs = @('build', (Join-Path $repository 'IPA.Loader/IPA.Loader.csproj'), '-c', 'Release', '--nologo')
    $localTargets = Join-Path $repository 'InstalledLibraries.local.targets'
    if (Test-Path -LiteralPath $localTargets) { $buildArgs += "-p:DirectoryBuildTargetsPath=$localTargets" }
    & dotnet @buildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Loader build failed with exit code $LASTEXITCODE." }
    return Join-Path $repository 'IPA.Loader/bin/Release/net472/IPA.Loader.dll'
}

function Assert-InstalledLoader([string] $Loader) {
    $expected = (Get-FileHash -LiteralPath $Loader).Hash
    foreach ($relativePath in @('IPA/Data/Managed/IPA.Loader.dll', 'Beat Saber_Data/Managed/IPA.Loader.dll')) {
        $installed = Join-Path $GameDirectory $relativePath
        if ((Get-FileHash -LiteralPath $installed).Hash -ne $expected) { throw "Installed loader differs from the build: $relativePath." }
    }
}

Install-Bsipa
