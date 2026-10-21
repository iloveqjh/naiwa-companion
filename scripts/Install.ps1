param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\NaiwaCompanion'),
    [switch]$OriginalAssets,
    [switch]$CreateShortcut,
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$manifest = Invoke-RestMethod -Uri 'https://raw.githubusercontent.com/iloveqjh/naiwa-companion/main/release.json'
if ($manifest.schemaVersion -ne 1 -or $manifest.downloadUrl -notmatch '^https://github\.com/iloveqjh/naiwa-companion/releases/download/v[0-9.]+/NaiwaCompanion-Windows-v[0-9.]+\.zip$' -or $manifest.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw '下载清单格式不正确，停止安装。' }
$target = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $target -Force | Out-Null
$zip = Join-Path $target ('download-' + [Guid]::NewGuid().ToString('N') + '.zip')
Invoke-WebRequest -UseBasicParsing -Uri $manifest.downloadUrl -OutFile $zip
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $manifest.sha256) { throw '程序包 SHA-256 校验失败，未安装。' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $archive.Entries) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $target $entry.FullName))
        if (!$resolved.StartsWith($target.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '压缩包中存在越界路径，停止安装。' }
    }
} finally { $archive.Dispose() }
Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
$exe = Join-Path $target 'NaiwaCompanion.exe'
if ($OriginalAssets) {
    $download = Start-Process -FilePath $exe -ArgumentList @('--download-assets', ('"' + $target + '"')) -Wait -PassThru
    if ($download.ExitCode -ne 0) { throw '原版素材下载失败，请查看安装目录内的 download-error.txt。' }
}
if ($CreateShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) '奶蛙陪伴.lnk'))
    $link.TargetPath = $exe; $link.WorkingDirectory = $target; $link.Description = '奶蛙陪伴：身外跟随，身上回正'; $link.Save()
}
Write-Output ('安装完成：' + $exe)
if ($Launch) { Start-Process -FilePath $exe -WorkingDirectory $target }
