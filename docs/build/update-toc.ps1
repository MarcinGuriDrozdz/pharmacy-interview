# Refreshes the table of contents and page numbers in Word (docx-js cannot compute page numbers).
# Optional: -Pdf <path> also exports a PDF for review.
param([string]$Pdf)
$doc = Join-Path $PSScriptRoot "..\DesignDocument-QCommerce-Apteki.docx" | Resolve-Path
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $d = $word.Documents.Open($doc.Path)
    foreach ($t in $d.TablesOfContents) { $t.Update() }
    $d.Save()
    if ($Pdf) { $d.ExportAsFixedFormat($Pdf, 17) }
    Write-Output "Pages: $($d.ComputeStatistics(2))"
    $d.Close()
} finally {
    $word.Quit()
}
