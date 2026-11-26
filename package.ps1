<#
.SYNOPSIS
    主仓打包脚本：固定输出到 releases\latest，同名文件自动覆盖。

.DESCRIPTION
    与 CI（.github/workflows/reusable-build.yml）保持一致：Release / x64 / 单文件 / 框架依赖，
    只注入 CI 同款 4 项密钥（基岩版 CIK×2、微软 Client ID、CurseForge Key）。

    刻意不读 local-secrets.ps1 里的 PCL_GITHUB_TOKEN / PCL_CNB_TOKEN：
    源生成器会把**所有** `PCL_*` 环境变量编进程序集（混淆但不加密），
    把写权限令牌打进公开分发的 exe 等同泄露。

.PARAMETER Configuration
    Release（默认）或 Debug。

.PARAMETER Platform
    x64（默认）或 ARM64。

.PARAMETER NoZip
    只出 exe + sha256，不打 zip。

.EXAMPLE
    .\package.ps1
    .\package.ps1 -NoZip
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

# ---- 版本号 ----
$metadata = Get-Content (Join-Path $root 'Plain Craft Launcher 2\metadata.json') -Raw | ConvertFrom-Json
$version = $metadata.version.base
if ($metadata.version.suffix) { $version = "$version-$($metadata.version.suffix)" }

$name = "PCL-Symbio-Edition-$version-$($Platform.ToLowerInvariant())"
$outDir = Join-Path $root 'releases\latest'
$publishDir = Join-Path $outDir 'publish'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# ---- 密钥（仅 CI 同款 4 项，从 local-secrets.ps1 正则解析，不 dot-source）----
$keep = @('PCL_BEDROCK_RELEASE_CIK', 'PCL_BEDROCK_PREVIEW_CIK', 'PCL_MS_CLIENT_ID', 'PCL_CURSEFORGE_API_KEY')
$secretsFile = Join-Path $root 'local-secrets.ps1'
if (Test-Path $secretsFile) {
    $raw = Get-Content $secretsFile -Raw
    foreach ($match in [regex]::Matches($raw, '\$env:(PCL_[A-Za-z0-9_]+)\s*=\s*([''"])([^''"]*)\2')) {
        $key = $match.Groups[1].Value
        $value = $match.Groups[3].Value
        if ($keep -notcontains $key -or [string]::IsNullOrWhiteSpace($value)) { continue }
        Set-Item -Path ("env:" + $key) -Value $value
    }
}

$env:PCL_WRITE_SECRET = '1'
$env:PCL_GITHUB_SHA = (& git rev-parse HEAD)
$injected = $keep | Where-Object { [Environment]::GetEnvironmentVariable($_) }
Write-Host "版本    : $version"
Write-Host "注入密钥: $(if ($injected) { $injected -join '、' } else { '无' })"
Write-Host "提交    : $env:PCL_GITHUB_SHA"

# ---- 发布 ----
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($dotnet)) { throw '未找到 dotnet，请先安装 .NET 10 SDK。' }

& $dotnet publish 'Plain Craft Launcher 2\Plain Craft Launcher 2.csproj' `
    "-p:Configuration=$Configuration" `
    "-p:Platform=$Platform" `
    '-p:DeleteExistingFiles=true' `
    '-p:UseSharedCompilation=false' `
    '-o' $publishDir `
    '--no-self-contained' `
    -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

$built = Join-Path $publishDir 'Plain Craft Launcher 2.exe'
if (-not (Test-Path $built)) { throw "没有找到产物：$built" }

# ---- 固定文件名（覆盖）----
$exeTarget = Join-Path $outDir "$name.exe"
Copy-Item -LiteralPath $built -Destination $exeTarget -Force
$hash = (Get-FileHash -LiteralPath $exeTarget -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $outDir "$name.exe.sha256") -Value ("{0}  {1}" -f $hash, "$name.exe") -Encoding ascii

# ---- zip（覆盖）----
if (-not $NoZip) {
    $stage = Join-Path $outDir '_stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $exeTarget -Destination $stage
    Copy-Item -LiteralPath (Join-Path $outDir "$name.exe.sha256") -Destination $stage
    Copy-Item -LiteralPath (Join-Path $publishDir 'THIRD-PARTY') -Destination $stage -Recurse -ErrorAction SilentlyContinue
    @"
$name（$(Get-Date -Format 'yyyy-MM-dd HH:mm')）
提交 $env:PCL_GITHUB_SHA
SHA256 $hash
运行前请先关闭已有实例（单实例检测）。
"@ | Set-Content -LiteralPath (Join-Path $stage '打包说明.txt') -Encoding utf8
    $zip = Join-Path $outDir "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Remove-Item $stage -Recurse -Force
}

# 中间 publish 目录与最终产物重复（各 36 MB），打完包就删掉，避免 releases 目录越滚越大
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host ''
Write-Host "EXE   : $exeTarget"
Write-Host "SHA256: $hash"
if (-not $NoZip) { Write-Host "ZIP   : $(Join-Path $outDir "$name.zip")" }
Write-Host "大小  : $([math]::Round((Get-Item $exeTarget).Length / 1MB, 2)) MB"
