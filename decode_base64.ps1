$json = Get-Content "C:\Users\XouYa\AppData\Local\Temp\trae\toolcall-output\6fa0206b-bef9-48dc-acaf-f604e3b45ff0.txt" -Raw
$obj = $json | ConvertFrom-Json
$base64 = $obj.content
$bytes = [System.Convert]::FromBase64String($base64)
$decoded = [System.Text.Encoding]::UTF8.GetString($bytes)
Set-Content -Path "C:\Users\XouYa\OneDrive\Desktop\PCL-Symbio Edition\Plain Craft Launcher 2\FormMain.xaml.cs" -Value $decoded -Encoding UTF8
Write-Host "Done! File written successfully."
Write-Host ("File size: " + $decoded.Length + " characters")