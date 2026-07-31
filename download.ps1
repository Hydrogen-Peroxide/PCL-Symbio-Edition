$url = "https://raw.githubusercontent.com/PCL-Community/PCL-CE/dev/Plain%20Craft%20Launcher%202/FormMain.xaml.cs"
$out = "C:\Users\XouYa\OneDrive\Desktop\PCL-Symbio Edition\Plain Craft Launcher 2\FormMain.xaml.cs"
$wc = New-Object System.Net.WebClient
$wc.Encoding = [System.Text.Encoding]::UTF8
$content = $wc.DownloadString($url)
[System.IO.File]::WriteAllText($out, $content, [System.Text.Encoding]::UTF8)
Write-Host ("Done: " + $content.Length)