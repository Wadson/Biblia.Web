param(
    [Parameter(Mandatory)][string]$PublishRoot,
    [Parameter(Mandatory)][string]$TestRoot,
    [int]$Port = 52620,
    [int]$StartupTimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishRoot).Path
$test = [IO.Path]::GetFullPath($TestRoot)
New-Item -ItemType Directory -Force -Path $test | Out-Null
$config = Get-Content -LiteralPath (Join-Path $publish 'Biblia.Web.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.frameworks -or $config.runtimeOptions.framework) { throw 'Publicação depende de runtime global.' }
$process = Start-Process -FilePath (Join-Path $publish 'Biblia.Web.exe') -WorkingDirectory $test -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $test 'stdout.log') -RedirectStandardError (Join-Path $test 'stderr.log') `
    -Environment @{
        BIBLIATEMA_DATA_DIR = (Join-Path $test 'dados')
        LocalHost__Port = "$Port"
        LocalHost__OpenBrowserOnStart = 'false'
        ASPNETCORE_ENVIRONMENT = 'Testing'
        # Kestrel's content root must be the delivered folder, not the caller's directory.
        ASPNETCORE_CONTENTROOT = $publish
        PATH = "$env:SystemRoot\System32"
        DOTNET_ROOT = (Join-Path $test 'sem-dotnet')
        DOTNET_ROOT_X64 = (Join-Path $test 'sem-dotnet')
        DOTNET_ROOT_X86 = (Join-Path $test 'sem-dotnet')
        DOTNET_MULTILEVEL_LOOKUP = '0'
        DOTNET_HOST_TRACE = '1'
        DOTNET_HOST_TRACEFILE = (Join-Path $test 'host-trace.log')
    }
try {
    $ready = $false
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    do {
        if ($process.HasExited) { throw "Aplicativo encerrou: $($process.ExitCode)" }
        try { $response = Invoke-WebRequest "http://127.0.0.1:$Port/" -TimeoutSec 3; $ready = $response.StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 300 }
    } until ($ready -or (Get-Date) -gt $deadline)
    if (-not $ready) { throw 'Aplicativo não respondeu.' }
    $routes = @('/', '/versoes', '/biblia', '/temas', '/temas/vincular-versiculos', '/relatorios', '/configuracoes', '/app.css')
    $checks = foreach ($route in $routes) {
        $response = Invoke-WebRequest "http://127.0.0.1:$Port$route" -TimeoutSec 15
        if ($response.StatusCode -ne 200) { throw "Falha na rota $route" }
        @{route = $route; status = $response.StatusCode; bytes = $response.RawContentLength}
    }
    $modules = @((Get-Process -Id $process.Id).Modules | Where-Object { $_.ModuleName -in @('coreclr.dll','hostfxr.dll','hostpolicy.dll','e_sqlite3.dll') } | Select-Object ModuleName,FileName)
    foreach ($module in $modules) {
        if (-not $module.FileName.StartsWith($publish + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Dependência externa: $($module.FileName)" }
    }
    if (-not ($modules | Where-Object ModuleName -eq 'coreclr.dll')) { throw 'Runtime carregado não identificado.' }
    Invoke-WebRequest "http://127.0.0.1:$Port/download/backup" -OutFile (Join-Path $test 'backup.zip') -TimeoutSec 90
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $test 'backup.zip'))
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry('manifest.json').Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($manifest.BackupFormatVersion -ne 2 -or $manifest.Bibles.Count -ne 7) { throw 'Backup não contém as sete Bíblias iniciais.' }
    } finally { $zip.Dispose() }
    @{result='PASS'; publish=$publish; runtime=$config.runtimeOptions.includedFrameworks; routes=$checks; modules=$modules; bibles=$manifest.Bibles.Count} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $test 'resultado.json')
    "PASS: runtime local, SQLite, 8 rotas e backup com 7 Bíblias. $test"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
