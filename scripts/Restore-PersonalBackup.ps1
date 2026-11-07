<#
.SYNOPSIS
验证并恢复个人备份中的完整主题、经典素材及可选应用信息。
.DESCRIPTION
只从用户指定的本地备份读取文件。先核对所有源文件的 SHA-256，
再写入目标目录；目标同名文件先留存副本。不会联网或结束运行中的程序。
恢复个人信息时必须显式指定新电脑的数据目录。
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$BackupRoot,
    [Parameter(Mandatory = $true)][string]$ProgramDirectory,
    [switch]$RestorePersonalData,
    [string]$DataDirectory
)
$ErrorActionPreference = 'Stop'
$temporaryFiles = New-Object 'System.Collections.Generic.List[string]'
$splitIndex = @{}

function Clear-BackupTemporaryFiles {
    # 列表仅来自本脚本创建的随机临时文件，不递归删除目录。
    foreach ($temporaryFile in $temporaryFiles) {
        if ([IO.File]::Exists($temporaryFile)) { [IO.File]::Delete($temporaryFile) }
    }
}
trap { Clear-BackupTemporaryFiles; throw }

function Get-BackupSHA256 {
    param([string]$LiteralFile)
    # 使用系统.NET，兼容未安装Get-FileHash模块的Windows PowerShell。
    $stream = [IO.File]::OpenRead($LiteralFile)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Get-ContainedPath {
    param([string]$BaseDirectory, [string]$RelativeFile)
    if ([IO.Path]::IsPathRooted($RelativeFile)) { throw '备份条目必须使用相对路径。' }
    $baseFull = [IO.Path]::GetFullPath($BaseDirectory).TrimEnd('\', '/')
    $fileFull = [IO.Path]::GetFullPath((Join-Path $baseFull $RelativeFile))
    if (-not $fileFull.StartsWith($baseFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw '备份条目超出了指定目录。'
    }
    return $fileFull
}

function Resolve-BackupSource {
    param([string]$RelativeFile, [string]$ExpectedHash)
    $normalFile = Get-ContainedPath $backupFull $RelativeFile
    if (Test-Path -LiteralPath $normalFile -PathType Leaf) { return $normalFile }
    $entry = $splitIndex[$RelativeFile.Replace('\', '/')]
    if (-not $entry -or $entry.sha256 -ne $ExpectedHash -or $entry.bytes -gt 268435456) {
        throw ('缺少有效的完整文件或分片清单：' + $RelativeFile)
    }
    # 在系统临时文件中重组；目标程序目录在整批校验通过前保持不变。
    $assembled = [IO.Path]::GetTempFileName()
    $temporaryFiles.Add($assembled)
    $writer = [IO.File]::Open($assembled, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    $totalBytes = [long]0
    try {
        foreach ($part in @($entry.parts)) {
            $partFile = Get-ContainedPath $backupFull $part.path
            if (-not (Test-Path -LiteralPath $partFile -PathType Leaf)) { throw '备份缺少分片。' }
            if ((Get-Item -LiteralPath $partFile).Length -ne $part.bytes -or (Get-BackupSHA256 $partFile) -ne $part.sha256) {
                throw '备份分片校验失败，未开始恢复。'
            }
            $reader = [IO.File]::OpenRead($partFile)
            try { $reader.CopyTo($writer) } finally { $reader.Dispose() }
            $totalBytes += $part.bytes
        }
    }
    finally { $writer.Dispose() }
    if ($totalBytes -ne $entry.bytes -or (Get-BackupSHA256 $assembled) -ne $ExpectedHash) {
        throw '重组后完整文件校验失败，未开始恢复。'
    }
    return $assembled
}

$backupFull = [IO.Path]::GetFullPath($BackupRoot)
$programFull = [IO.Path]::GetFullPath($ProgramDirectory)
if (-not (Test-Path -LiteralPath $backupFull -PathType Container)) { throw '找不到备份目录。' }
if ($RestorePersonalData -and [string]::IsNullOrWhiteSpace($DataDirectory)) {
    throw '恢复个人信息时，请显式指定新电脑的 DataDirectory。'
}

# 只阻止正在使用目标安装目录的进程；不关闭其他安装位置的奶娃。
$targetExe = Join-Path $programFull 'NaiwaCompanion.exe'
foreach ($petProcess in @(Get-Process -Name NaiwaCompanion -ErrorAction SilentlyContinue)) {
    if ($petProcess.Path -and [string]::Equals($petProcess.Path, $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw '请先关闭目标目录中正在运行的奶娃，再进行恢复。'
    }
}

$themeInfo = Get-Content -LiteralPath (Join-Path $backupFull 'themes-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$backupInfo = Get-Content -LiteralPath (Join-Path $backupFull 'backup-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$splitManifest = Join-Path $backupFull 'large-files-manifest.json'
if (Test-Path -LiteralPath $splitManifest -PathType Leaf) {
    $splitInfo = Get-Content -LiteralPath $splitManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($splitInfo.version -ne 1) { throw '不支持此分片清单版本。' }
    foreach ($splitEntry in @($splitInfo.files)) { $splitIndex[$splitEntry.path] = $splitEntry }
}
$copyItems = New-Object 'System.Collections.Generic.List[object]'
foreach ($theme in @($themeInfo.themes)) {
    if ($theme.folder -notmatch '^[a-z0-9-]+$') { throw '主题目录名称不合法。' }
    $relative = 'themes/' + $theme.folder + '/theme.nct'
    $copyItems.Add([pscustomobject]@{
        Source = Resolve-BackupSource $relative ([string]$theme.sha256)
        Destination = Get-ContainedPath $programFull $relative
        Hash = [string]$theme.sha256
        Kind = 'theme'
    })
}
foreach ($entry in @($backupInfo.files | Where-Object { $_.path -like 'classic-assets/*' })) {
    $copyItems.Add([pscustomobject]@{
        Source = Resolve-BackupSource $entry.path ([string]$entry.sha256)
        Destination = Get-ContainedPath $programFull ([IO.Path]::GetFileName($entry.path))
        Hash = [string]$entry.sha256
        Kind = 'classic-asset'
    })
}
if ($RestorePersonalData) {
    $dataFull = [IO.Path]::GetFullPath($DataDirectory)
    foreach ($entry in @($backupInfo.files | Where-Object { $_.path -like 'user-data/current/*.json' })) {
        $copyItems.Add([pscustomobject]@{
            Source = Resolve-BackupSource $entry.path ([string]$entry.sha256)
            Destination = Get-ContainedPath $dataFull ([IO.Path]::GetFileName($entry.path))
            Hash = [string]$entry.sha256
            Kind = 'personal-data'
        })
    }
}

# 在复制任何文件前完成整批校验，避免使用损坏或不完整的备份。
foreach ($item in $copyItems) {
    if ($item.Hash -notmatch '^[a-fA-F0-9]{64}$') { throw '备份中的校验值格式不正确。' }
    if (-not (Test-Path -LiteralPath $item.Source -PathType Leaf)) { throw ('缺少备份文件：' + $item.Source) }
    $actualHash = Get-BackupSHA256 $item.Source
    if ($actualHash -ne $item.Hash) { throw ('文件校验失败，未开始恢复：' + $item.Source) }
}
if (-not $PSCmdlet.ShouldProcess($programFull, '恢复已经完整校验的主题及所选个人信息')) { Clear-BackupTemporaryFiles; return }

$restored = @()
foreach ($item in $copyItems) {
    $destinationParent = Split-Path -Parent $item.Destination
    if (-not (Test-Path -LiteralPath $destinationParent)) {
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    }
    if (Test-Path -LiteralPath $item.Destination -PathType Leaf) {
        $suffix = '.before-migration-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
        Copy-Item -LiteralPath $item.Destination -Destination ($item.Destination + $suffix)
    }
    Copy-Item -LiteralPath $item.Source -Destination $item.Destination -Force
    if ((Get-BackupSHA256 $item.Destination) -ne $item.Hash) {
        throw ('目标文件写入后校验失败：' + $item.Destination)
    }
    $restored += [pscustomobject]@{ Path = $item.Destination; Kind = $item.Kind; SHA256 = $item.Hash }
}
$result = [pscustomobject]@{ RestoredAtUtc = [DateTime]::UtcNow.ToString('o'); Files = $restored }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $programFull 'migration-restored.json') -Encoding UTF8
Clear-BackupTemporaryFiles
Write-Output $result
