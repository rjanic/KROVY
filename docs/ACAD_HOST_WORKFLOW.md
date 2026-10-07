# AutoCAD HOST workflow

The shared developer workflow is `scripts/acad-host-workflow.ps1`. It closes
only `acad.exe`, waits for the process to disappear, builds the AutoCAD adapter,
runs the Core and WPF tests, and then launches AutoCAD with the saved HOST DWG.

The default HOST drawing is:

```text
C:\Users\Roman\Documents\3d.dwg
```

Run it from the repository root:

```powershell
pwsh -NoProfile -File .\scripts\acad-host-workflow.ps1
```

Useful options are `-Configuration Release`, `-SkipTests`, `-NoLaunch`,
`-DwgPath <path>`, and `-AutoCadInstallDir <directory>`. The AutoCAD executable
is resolved from the configured AutoCAD 2027 directory first, then from
Autodesk installation discovery. The DWG must already exist; the workflow never
creates or edits a test drawing.

The user's existing AutoCAD startup/autoload remains authoritative. The helper
does not NETLOAD another copy and does not issue CAD edit commands. Once AutoCAD
is open, HOST interaction remains user-driven.
