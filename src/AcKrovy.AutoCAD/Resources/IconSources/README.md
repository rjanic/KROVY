# Red roof icons

`roof-icons-red.png` is the approved design adapted with the built-in ImageGen tool to a transparent production atlas. Quadrants: hip, half-hip, gable, mono-pitch. The original red concept was approved by the user.

Generation prompt: preserve the approved red roof geometries, camera views, dark outlines and off-white walls; remove labels and ground shadows; arrange the four icons in a regular 2x2 grid on genuinely transparent RGBA background. The final background-extraction pass removes the incorrectly painted checkerboard while preserving the four objects.

Run `scripts/export-roof-icons.ps1` from PowerShell to crop each quadrant to its alpha bounds and export the existing 16px and 32px PNG names with one pixel of horizontal padding. The roof menu reuses the gable icon. Sources are retained here; only `Resources/Icons/*.png` are copied to the application output by the project.

`roof-purlins.png` is the transparent source for the automatic-purlin Ribbon icon. It is cropped to its alpha bounds and exported as `ak_roof_purlins_16.png` and `ak_roof_purlins_32.png`.
