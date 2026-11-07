#Requires -Version 5.1
<#
.SYNOPSIS
    本地构建 PCL Symbio Edition，并按 CI 的方式把密钥编译进程序集。

.DESCRIPTION
    CI（.github/workflows/reusable-build.yml）通过环境变量注入密钥：只有当
    PCL_WRITE_SECRET 存在时，源生成器 EnvironmentInteropGenerator 才会把所有
    PCL_* 环境变量编译进程序集。

    直接在本地执行 dotnet publish 时若没有设置 PCL_WRITE_SECRET，产物中不含任何
    密钥，此时基岩版 GDK（.msixvc）解包会因缺少内容密钥（CIK）而失败。

    本脚本负责：
      1. 设置 PCL_WRITE_SECRET=1；
      2. 校验 PCL_BEDROCK_RELEASE_CIK / PCL_BEDROCK_PREVIEW_CIK（若提供）；
      3. 用与 CI 一致的参数执行 dotnet publish。

    CIK 需要自行提供（仓库 Secrets 中的 BEDROCK_RELEASE_CIK / BEDROCK_PREVIEW_CIK，
    或直接使用 CI 构建出的产物）。缺少 CIK 时脚本仍会构建，但会给出警告。

.PARAMETER Configuration
    Release（默认）或 Debug。

.PARAMETER Platform
    x64（默认）或 ARM64。

.PARAMETER OutputDir
    输出目录，默认为仓库根目录下的 artifact。

.PARAMETER RequireBedrockCik
    缺少 CIK 时直接报错退出，而不是仅警告。

.PARAMETER Force
    构建前清理两个项目的 obj/bin。更换密钥后需要清理，否则可能复用旧的生成代码。

.EXAMPLE
    $env:PCL_BEDROCK_RELEASE_CIK = '<96 位十六进制字符串>'
    ./build-local.ps1

.EXAMPLE
    ./build-local.ps1 -Configuration Debug -OutputDir ./artifact-debug -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [string]$OutputDir = './artifact',

    [switch]$RequireBedrockCik,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Test-CikValue {
    param([string]$Name, [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    if ($Value.Length -lt 96 -or $Value.Length % 2 -ne 0 -or $Value -notmatch '^[0-9a-fA-F]+$') {
        throw "环境变量 $Name 不是合法的 CIK：需要至少 96 位（48 字节）的十六进制字符串，当前长度 $($Value.Length)。"
    }
    return $true
}

# 可选的本地密钥文件（已在 .gitignore 中忽略），内容形如：
#   $env:PCL_BEDROCK_RELEASE_CIK = '<96 位十六进制>'
#   $env:PCL_BEDROCK_PREVIEW_CIK = '<96 位十六进制>'

$cikNames = @('PCL_BEDROCK_RELEASE_CIK', 'PCL_BEDROCK_PREVIEW_CIK')

# 本地密钥文件（未入库）：存在时自动加载，避免每次构建都手动设置环境变量
$localSecrets = Join-Path $root 'local-secrets.ps1'
if (Test-Path $localSecrets) { . $localSecrets }

$missingCik = @()
foreach ($name in $cikNames) {
    if (-not (Test-CikValue -Name $name -Value ([Environment]::GetEnvironmentVariable($name)))) {
        $missingCik += $name
    }
}

if ($missingCik.Count -gt 0) {
    $message = @"
未设置 $($missingCik -join '、')，构建产物将无法解包基岩版 GDK 安装包（.msixvc）。
请在 local-secrets.ps1 中填写（该文件已在 .gitignore 中，不会入库），或使用 CI 构建出的 artifact。
"@
    if ($RequireBedrockCik) { throw $message }
    Write-Warning $message
}

# 源生成器只读取 PCL_* 环境变量，必须先开启写入开关
$env:PCL_WRITE_SECRET = '1'

# 与 CI 保持一致：写入当前提交 SHA（缺失时留空）
if ([string]::IsNullOrWhiteSpace($env:PCL_GITHUB_SHA)) {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $git) {
        $sha = & $git.Source rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($sha)) {
            $env:PCL_GITHUB_SHA = $sha.Trim()
        }
    }
}

$dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    $fallback = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path $fallback)) { throw '未找到 dotnet，请先安装 .NET 10 SDK 或将其加入 PATH。' }
    $dotnetPath = $fallback
}

if ($Force) {
    foreach ($project in @('PCL.Core', 'Plain Craft Launcher 2')) {
        foreach ($dir in @('obj', 'bin')) {
            $path = Join-Path $root "$project\$dir"
            if (Test-Path $path) {
                Write-Host "清理 $project\$dir"
                Remove-Item $path -Recurse -Force
            }
        }
    }
}

$outPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $root $OutputDir }

Write-Host "构建配置：$Configuration / $Platform"
Write-Host "输出目录：$outPath"
$injectedSecrets = @(
    [Environment]::GetEnvironmentVariables().Keys |
        Where-Object { $_ -like 'PCL_*' -and $_ -ne 'PCL_WRITE_SECRET' -and -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) }
)
Write-Host "已注入密钥：$(if ($injectedSecrets.Count -gt 0) { $injectedSecrets -join '、' } else { '无' })"
if ($missingCik.Count -gt 0) { Write-Host "缺少密钥：$($missingCik -join '、')" }

& $dotnetPath publish 'Plain Craft Launcher 2/Plain Craft Launcher 2.csproj' `
    "-p:Configuration=$Configuration" `
    "-p:Platform=$Platform" `
    '-p:DeleteExistingFiles=true' `
    '-o' $outPath `
    '--no-self-contained'

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码 $LASTEXITCODE"
}

Write-Host "构建完成：$outPath"
