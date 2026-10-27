param([Parameter(Mandatory = $true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '../../Plain Craft Launcher 2/Modules/Base/ModBase.cs'
$source = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $sourcePath))
$start = $source.IndexOf('    #region CheckedProcessExecution', [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Checked process region is missing.' }
$end = $source.IndexOf('    #endregion', $start, [StringComparison]::Ordinal)
if ($end -lt 0) { throw 'Checked process region is unterminated.' }
$body = $source.Substring($start, $end - $start)
$body = $body.Substring($body.IndexOf([char]10) + 1)
$generated = "using System.Diagnostics;`nusing System.Text;`nnamespace PCL;`npublic static partial class ModBase {`n" + $body + "`n}"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
[IO.File]::WriteAllText($OutputPath, $generated)
