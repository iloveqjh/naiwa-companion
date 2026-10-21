param([string]$Destination = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$target = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $target -Force | Out-Null
$revision = '553afb87508d3b05cda3af935616ed00e94dd3e4'
$assets = @(
    @{ Name = '素材.nwp'; SHA256 = 'A9C7795974D860DA0C82B45237456698640EE8D4252B5DB37503383D23FA46AE' },
    @{ Name = '原版笑声.wav'; SHA256 = 'E808F6F932ABFB81E7104EB1C586FE6C828BDA5DB4EF62E52E0638A64A01FDFA' }
)
Write-Output '正在从原作者 yugh6/Naiwa 下载原版素材。素材权利与使用条件见该项目说明。'
foreach ($asset in $assets) {
    $file = Join-Path $target $asset.Name
    if ((Test-Path -LiteralPath $file) -and (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -eq $asset.SHA256) {
        Write-Output ('已经校验：' + $asset.Name); continue
    }
    $temp = $file + '.' + [Guid]::NewGuid().ToString('N') + '.download'
    $url = 'https://raw.githubusercontent.com/yugh6/Naiwa/' + $revision + '/' + [Uri]::EscapeDataString($asset.Name)
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $temp
    if ((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $asset.SHA256) { throw ('文件校验失败，未替换已有素材：' + $asset.Name) }
    Move-Item -LiteralPath $temp -Destination $file -Force
    Write-Output ('下载并校验完成：' + $asset.Name)
}
Write-Output ('素材已就绪：' + $target + '。现在可以双击 NaiwaCompanion.exe。')
