param([string[]]$Architectures = @('x64','x86','arm64'))
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $workspace
try {
    $wix = Join-Path $PSScriptRoot 'wix4/wix.exe'
    if (-not (Test-Path -LiteralPath $wix)) {
        dotnet tool install wix --version 4.0.6 --tool-path (Join-Path $PSScriptRoot 'wix4')
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar compilador MSI.' }
    }
    $buildRoot = 'output/distribuicao/' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    foreach ($arch in $Architectures) {
        if ($arch -notin @('x64','x86','arm64')) { throw "Arquitetura inválida: $arch" }
        $rid = "win-$arch"
        dotnet publish Biblia.Web/Biblia.Web.csproj -c Release -r $rid --self-contained true `
            -p:PublishProfile=WindowsStandalone -p:UseSharedCompilation=false "-p:IntermediateOutputPath=obj/Distribution/$rid/" `
            -nodeReuse:false -m:1 -o "$buildRoot/$rid" -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Falha no publish $rid." }
        py -3 tools/Distribution/package.py "$buildRoot/$rid" $buildRoot/pacotes --arch $arch
        if ($LASTEXITCODE -ne 0) { throw "Falha no pacote $rid." }
        & $wix build "$buildRoot/pacotes/Biblia.Web-$arch.wxs" -arch $arch -o "$buildRoot/pacotes/Biblia.Web-Windows-$arch.msi"
        if ($LASTEXITCODE -ne 0) { throw "Falha no MSI $rid." }
    }
    Get-FileHash "$buildRoot/pacotes/*.msi","$buildRoot/pacotes/*.zip" |
        Select-Object @{Name='Arquivo';Expression={[IO.Path]::GetFileName($_.Path)}},Hash |
        Export-Csv $buildRoot/pacotes/SHA256.csv -NoTypeInformation -Encoding utf8
} finally { Pop-Location }
