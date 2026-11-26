#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$shell = Join-Path $root 'Plain Craft Launcher 2/Shell'
$files = Get-ChildItem -LiteralPath $shell -Recurse -Filter '*.xaml' -File
$violations = foreach ($file in $files) {
    Select-String -LiteralPath $file.FullName -Pattern @(
        'MessageBox\.Show',
        '(?:Foreground|Background|BorderBrush|Fill|Stroke)="(?:#[0-9A-Fa-f]{3,8}|White|Black|Red|Blue|Green)"'
    )
}

if ($violations) {
    $details = $violations | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
    throw "Symbio UI guard failed:`n$($details -join [Environment]::NewLine)"
}

Write-Host "Symbio UI guard passed ($($files.Count) XAML files checked)."
