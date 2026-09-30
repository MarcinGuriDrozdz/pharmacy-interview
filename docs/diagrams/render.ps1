# Renders every SVG in this folder to a 2x PNG with headless Edge (used by the design-doc build).
$edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
Get-ChildItem $PSScriptRoot -Filter *.svg | ForEach-Object {
    [xml]$svg = Get-Content $_.FullName -Raw
    $w = $svg.svg.width; $h = $svg.svg.height
    $png = [IO.Path]::ChangeExtension($_.FullName, ".png")
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 `
        "--window-size=$w,$h" "--screenshot=$png" ([Uri]$_.FullName).AbsoluteUri 2>$null | Out-Null
    Write-Output "$($_.Name) -> $([IO.Path]::GetFileName($png))"
}
