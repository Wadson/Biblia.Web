$ErrorActionPreference = 'Stop'
$source = 'D:\Publicacao\Biblia.Web'
$target = 'C:\Program Files\Biblia.Web'
$result = 'D:\Projetos\Biblia.Web\output\blocos-dinamicos\atualizacao-instalacao.txt'
try {
    $files = @(Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object {
        $_.Name -notlike 'appsettings*.json' -and $_.FullName -notmatch '\\(LogFiles|AppData|Cache|backups)\\' -and $_.Extension -notin @('.db','.db-wal','.db-shm')
    })
    $changed = @($files | Where-Object {
        $dest = Join-Path $target ([IO.Path]::GetRelativePath($source, $_.FullName))
        -not (Test-Path -LiteralPath $dest) -or (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $dest).Hash
    })
    Get-Process -Name Biblia.Web.Web -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -eq (Join-Path $target 'Biblia.Web.Web.exe')
    } | Stop-Process
    foreach ($file in $changed) {
        $dest = Join-Path $target ([IO.Path]::GetRelativePath($source, $file.FullName))
        if (-not $dest.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destino inválido.' }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $dest).Hash) { throw "Hash divergente: $dest" }
    }
    "SUCESSO: $($changed.Count) arquivos atualizados. Dados e appsettings preservados." | Set-Content -LiteralPath $result
} catch {
    "FALHA: $($_.Exception.Message)" | Set-Content -LiteralPath $result
    exit 1
}
