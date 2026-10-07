[CmdletBinding()]
param(
    [string]$DwgPath = (Join-Path $env:USERPROFILE "Documents\3d.dwg"),
    [string]$AutoCadInstallDir = "C:\Program Files\Autodesk\AutoCAD 2027",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipTests,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$adapterProject = Join-Path $repoRoot "src\AcKrovy.AutoCAD\AcKrovy.AutoCAD.csproj"
$coreTestsProject = Join-Path $repoRoot "src\AcKrovy.Core.Tests\AcKrovy.Core.Tests.csproj"
$wpfTestsProject = Join-Path $repoRoot "src\AcKrovy.Wpf.Tests\AcKrovy.Wpf.Tests.csproj"

function Write-Step([string]$message) {
    Write-Host "[ACAD HOST] $message"
}

function Invoke-Checked([string]$filePath, [string[]]$arguments) {
    & $filePath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $filePath $($arguments -join ' ')"
    }
}

function Get-AcadProcesses {
    $targetPath = [System.IO.Path]::GetFullPath($acadExe)
    @(Get-Process -Name "acad" -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([System.StringComparer]::OrdinalIgnoreCase.Equals(
                [System.IO.Path]::GetFullPath($_.Path), $targetPath))
        }
        catch {
            $false
        }
    })
}

function Close-AcadForBuild {
    $processes = Get-AcadProcesses
    if ($processes.Count -eq 0) {
        Write-Step "No acad.exe process is running."
        return
    }

    Write-Step "Requesting normal close for $($processes.Count) acad.exe process(es)."
    foreach ($process in $processes) {
        try {
            if ($process.MainWindowHandle -ne 0) {
                [void]$process.CloseMainWindow()
            }
        }
        catch {
            Write-Step "Normal close request failed for PID $($process.Id); forced close will follow."
        }
    }

    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 250
        $remaining = Get-AcadProcesses
    } while ($remaining.Count -gt 0 -and (Get-Date) -lt $deadline)

    if ($remaining.Count -gt 0) {
        Write-Step "Forcing close of remaining acad.exe process(es)."
        foreach ($process in $remaining) {
            Stop-Process -Id $process.Id -Force
        }
    }

    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $remaining = Get-AcadProcesses
    } while ($remaining.Count -gt 0 -and (Get-Date) -lt $deadline)

    if ($remaining.Count -gt 0) {
        throw "acad.exe did not exit before the build."
    }

    Write-Step "acad.exe is closed and its plugin files are unlocked."
}

function Resolve-AcadExecutable {
    $configured = Join-Path $AutoCadInstallDir "acad.exe"
    if (Test-Path -LiteralPath $configured -PathType Leaf) {
        return (Resolve-Path -LiteralPath $configured).Path
    }

    $candidates = @(
        (Get-ChildItem -Path "C:\Program Files\Autodesk" -Filter "acad.exe" -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "AutoCAD 2027" } |
            Select-Object -ExpandProperty FullName)
    )
    $resolved = $candidates | Select-Object -First 1
    if ($null -ne $resolved) {
        return $resolved
    }

    throw "AutoCAD 2027 acad.exe was not found. Checked '$configured' and Autodesk installation discovery."
}

if (-not (Test-Path -LiteralPath $DwgPath -PathType Leaf)) {
    throw "HOST DWG was not found: $DwgPath"
}

$acadExe = Resolve-AcadExecutable
Write-Step "Using AutoCAD executable: $acadExe"
Write-Step "Using saved HOST DWG: $DwgPath"

Close-AcadForBuild

Write-Step "Building AcKrovy.AutoCAD ($Configuration, x64)."
Invoke-Checked "dotnet" @(
    "build", $adapterProject, "--no-restore", "-c", $Configuration,
    "-p:Platform=x64", "-warnaserror", "-m:1", "-nr:false"
)

if (-not $SkipTests) {
    Write-Step "Running Core tests."
    Invoke-Checked "dotnet" @(
        "test", $coreTestsProject, "--no-restore", "-warnaserror", "-m:1", "-nr:false"
    )

    Write-Step "Running WPF tests."
    Invoke-Checked "dotnet" @(
        "test", $wpfTestsProject, "--no-restore", "-p:Platform=x64", "-warnaserror", "-m:1", "-nr:false"
    )
}

if ($NoLaunch) {
    Write-Step "Build/test completed; AutoCAD launch was disabled with -NoLaunch."
    exit 0
}

Write-Step "Launching AutoCAD with the saved HOST DWG. Existing startup/autoload remains authoritative."
$quotedDwgPath = '"{0}"' -f $DwgPath
$launched = Start-Process -FilePath $acadExe -ArgumentList @($quotedDwgPath) -WorkingDirectory (Split-Path -Parent $acadExe) -PassThru
Start-Sleep -Seconds 3
if ($launched.HasExited) {
    throw "AutoCAD exited immediately with code $($launched.ExitCode)."
}
Write-Step "AutoCAD started (PID $($launched.Id)); AK_RUNTIME_BUILD and AK_ROOF_3D_TRACE are provided by existing startup/autoload."
