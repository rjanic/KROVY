param(
    [switch]$Portable,
    [switch]$Full,
    [string]$AutoCadInstallDir = "C:\Program Files\Autodesk\AutoCAD 2027"
)

$ErrorActionPreference = "Stop"

if ($Portable -and $Full) {
    Write-Host "[FAIL] Use either -Portable or -Full, not both."
    exit 2
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$coreProject = Join-Path $repoRoot "src/AcKrovy.Core/AcKrovy.Core.csproj"
$abstractionsProject = Join-Path $repoRoot "src/AcKrovy.Cad.Abstractions/AcKrovy.Cad.Abstractions.csproj"
$localizationProject = Join-Path $repoRoot "src/AcKrovy.Localization/AcKrovy.Localization.csproj"
$testsProject = Join-Path $repoRoot "src/AcKrovy.Core.Tests/AcKrovy.Core.Tests.csproj"
$solution = Join-Path $repoRoot "AcKrovy.sln"
$buildMetadata = Join-Path $repoRoot "Directory.Build.props"
$packageManifest = Join-Path $repoRoot "deploy/AcKrovy.bundle/PackageContents.xml"

$forbiddenPortableReferences = @(
    "Autodesk.AutoCAD",
    "AcMgd",
    "AcDbMgd",
    "AcCoreMgd",
    "AcKrovy.AutoCAD",
    "AcKrovy.Localization"
)

$forbiddenCadReferences = @(
    "Autodesk.AutoCAD",
    "AcMgd",
    "AcDbMgd",
    "AcCoreMgd",
    "AcKrovy.AutoCAD"
)

$forbiddenPortableSourcePatterns = @(
    "Autodesk\.AutoCAD",
    "\bAcMgd\b",
    "\bAcDbMgd\b",
    "\bAcCoreMgd\b",
    "AcKrovy\.AutoCAD",
    "AcKrovy\.Localization"
)

$forbiddenCadSourcePatterns = @(
    "Autodesk\.AutoCAD",
    "\bAcMgd\b",
    "\bAcDbMgd\b",
    "\bAcCoreMgd\b",
    "AcKrovy\.AutoCAD"
)

function Write-GateHeader {
    Write-Host ""
    Write-Host "ACAD KROVY COMPATIBILITY GATE"
    Write-Host ""
}

function Pass-Step([string]$message) {
    Write-Host "[PASS] $message"
}

function Fail-Step([string]$message) {
    Write-Host "[FAIL] $message"
    Write-Host ""
    Write-Host "COMPATIBILITY GATE: BLOCKED"
    exit 1
}

function Invoke-CheckedCommand([string]$stepName, [string]$filePath, [string[]]$arguments) {
    & $filePath @arguments
    if ($LASTEXITCODE -ne 0) {
        Fail-Step $stepName
    }

    Pass-Step $stepName
}

function Get-ProjectReferenceValues([string]$projectPath, [string]$itemName, [string]$attributeName) {
    [xml]$project = Get-Content -LiteralPath $projectPath
    $values = New-Object System.Collections.Generic.List[string]

    foreach ($group in $project.Project.ItemGroup) {
        if ($null -eq $group) {
            continue
        }

        foreach ($item in $group.ChildNodes) {
            if ($item.Name -ne $itemName) {
                continue
            }

            $value = $item.GetAttribute($attributeName)
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $values.Add($value)
            }
        }
    }

    return $values
}

function Assert-NoForbiddenProjectReferences(
    [string]$projectPath,
    [string]$projectName,
    [string[]]$forbiddenReferences = $forbiddenPortableReferences) {
    $items = @()
    $items += Get-ProjectReferenceValues $projectPath "ProjectReference" "Include"
    $items += Get-ProjectReferenceValues $projectPath "Reference" "Include"
    $items += Get-ProjectReferenceValues $projectPath "PackageReference" "Include"

    foreach ($item in $items) {
        foreach ($forbidden in $forbiddenReferences) {
            if ($item -like "*$forbidden*") {
                Fail-Step "$projectName contains forbidden project/reference dependency: $item"
            }
        }
    }
}

function Assert-NoForbiddenSourceDependencies(
    [string]$sourceRoot,
    [string]$projectName,
    [string[]]$patterns = $forbiddenPortableSourcePatterns) {
    $files = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Include *.cs,*.csproj
    foreach ($file in $files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($pattern in $patterns) {
            if ($content -match $pattern) {
                $relative = Resolve-Path -LiteralPath $file.FullName -Relative
                Fail-Step "$projectName contains forbidden dependency in $relative"
            }
        }
    }
}

function Test-AutoCadAssembliesAvailable {
    $required = @("AcMgd.dll", "AcDbMgd.dll", "AcCoreMgd.dll", "AdWindows.dll")
    foreach ($assembly in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $AutoCadInstallDir $assembly))) {
            return $false
        }
    }

    return $true
}

function Invoke-ArchitectureChecks {
    Assert-NoForbiddenProjectReferences $coreProject "AcKrovy.Core"
    Assert-NoForbiddenProjectReferences $abstractionsProject "AcKrovy.Cad.Abstractions"
    Assert-NoForbiddenProjectReferences $localizationProject "AcKrovy.Localization" $forbiddenCadReferences
    Assert-NoForbiddenSourceDependencies (Join-Path $repoRoot "src/AcKrovy.Core") "AcKrovy.Core"
    Assert-NoForbiddenSourceDependencies (Join-Path $repoRoot "src/AcKrovy.Cad.Abstractions") "AcKrovy.Cad.Abstractions"
    Assert-NoForbiddenSourceDependencies (Join-Path $repoRoot "src/AcKrovy.Localization") "AcKrovy.Localization" $forbiddenCadSourcePatterns
    Pass-Step "Architecture dependency rules"
}

function Assert-PackageVersionMatchesBuildMetadata {
    [xml]$metadata = Get-Content -LiteralPath $buildMetadata
    [xml]$manifest = Get-Content -LiteralPath $packageManifest

    $centralVersion = [string]$metadata.Project.PropertyGroup.AcKrovyVersion
    $packageVersion = [string]$manifest.ApplicationPackage.AppVersion

    if ([string]::IsNullOrWhiteSpace($centralVersion)) {
        Fail-Step "Directory.Build.props does not define AcKrovyVersion"
    }

    if ($packageVersion -ne $centralVersion) {
        Fail-Step "Package AppVersion '$packageVersion' does not match central version '$centralVersion'"
    }

    Pass-Step "Central version and package manifest are consistent ($centralVersion)"
}

function Invoke-PortableGate {
    Invoke-ArchitectureChecks
    Assert-PackageVersionMatchesBuildMetadata

    Invoke-CheckedCommand "AcKrovy.Core restore" "dotnet" @("restore", $coreProject)
    Invoke-CheckedCommand "AcKrovy.Core build warnings-as-errors" "dotnet" @("build", $coreProject, "--no-restore", "-warnaserror")

    Invoke-CheckedCommand "AcKrovy.Cad.Abstractions restore" "dotnet" @("restore", $abstractionsProject)
    Invoke-CheckedCommand "AcKrovy.Cad.Abstractions build warnings-as-errors" "dotnet" @("build", $abstractionsProject, "--no-restore", "-warnaserror")

    Invoke-CheckedCommand "AcKrovy.Localization restore" "dotnet" @("restore", $localizationProject)
    Invoke-CheckedCommand "AcKrovy.Localization build warnings-as-errors" "dotnet" @("build", $localizationProject, "--no-restore", "-warnaserror")

    Invoke-CheckedCommand "AcKrovy.Core.Tests restore" "dotnet" @("restore", $testsProject)
    Invoke-CheckedCommand "Automated tests" "dotnet" @("test", $testsProject, "--no-restore", "-warnaserror")
}

function Invoke-FullGate {
    if (-not (Test-AutoCadAssembliesAvailable)) {
        Fail-Step "AutoCAD adapter build requires AutoCAD API assemblies in $AutoCadInstallDir"
    }

    Invoke-PortableGate
    Invoke-CheckedCommand "Solution restore" "dotnet" @("restore", $solution)
    Invoke-CheckedCommand "AutoCAD adapter build warnings-as-errors" "dotnet" @("build", $solution, "--no-restore", "-warnaserror")
    Invoke-CheckedCommand "Solution tests" "dotnet" @("test", $solution, "--no-build")
}

Write-GateHeader

$autoCadAvailable = Test-AutoCadAssembliesAvailable
if ($Full) {
    Write-Host "Mode: FULL"
    Invoke-FullGate
}
elseif ($Portable) {
    Write-Host "Mode: PORTABLE"
    Invoke-PortableGate
    if (-not $autoCadAvailable) {
        Write-Host "[INFO] AutoCAD adapter build skipped: AutoCAD API assemblies not found in $AutoCadInstallDir"
    }
}
elseif ($autoCadAvailable) {
    Write-Host "Mode: FULL (AutoCAD API assemblies detected)"
    Invoke-FullGate
}
else {
    Write-Host "Mode: PORTABLE (AutoCAD API assemblies not found)"
    Invoke-PortableGate
    Write-Host "[INFO] AutoCAD adapter build skipped: AutoCAD API assemblies not found in $AutoCadInstallDir"
}

Write-Host ""
Write-Host "COMPATIBILITY GATE: PASSED"
exit 0
