"""Package an existing self-contained publish; never reads the user's data directory."""
import argparse
import hashlib
import json
from pathlib import Path
import uuid
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('publish', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--arch', choices=['x64', 'x86', 'arm64'], required=True)
args = parser.parse_args()
root = args.publish.resolve()
out = args.output.resolve()
out.mkdir(parents=True, exist_ok=True)
runtime = json.loads((root / 'Biblia.Web.runtimeconfig.json').read_text())['runtimeOptions']
assert 'frameworks' not in runtime and 'framework' not in runtime, 'Publish is not self-contained'
for required in ['coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
                 'Microsoft.AspNetCore.Components.Server.dll', 'e_sqlite3.dll', 'libSkiaSharp.dll']:
    assert (root / required).is_file(), required
assert len(list((root / 'Content/Bibles').glob('*.sqlite'))) == 7
assert not list(root.rglob('bibliatema.db')), 'Personal database must not be packaged'

readme = f'''BIBLIA.WEB - WINDOWS {args.arch.upper()} - AUTOSSUFICIENTE

INSTALAR: execute o MSI correspondente ao processador do computador. O instalador
inclui .NET, ASP.NET Core, SQLite, PDF, fontes, imagens locais e sete Biblias.
Abra Biblia.Web pelo menu Iniciar ou atalho da area de trabalho.

SEM INSTALAR: extraia TODO o ZIP e execute Biblia.Web.exe.
Nao mova apenas o EXE. Os arquivos desta pasta fazem parte do aplicativo.
Nao e necessario instalar .NET, Visual Studio, IIS ou SQL Server.
O aplicativo abre no navegador padrao em um endereco local 127.0.0.1.
Requer Windows 10/11 atualizado e navegador moderno. Windows 7/8/8.1 nao suportados.
O suporte oficial depende da edicao/ciclo de vida do Windows e do .NET 10.
x64: Intel/AMD 64 bits. x86: Windows 32 bits. arm64: Windows ARM64.

Os estudos sao criados separadamente em %LOCALAPPDATA%\\BibliaTema.
Atualizar ou desinstalar o programa nao deve apagar essa pasta.
Antes de atualizar, feche o aplicativo e crie um backup nas Configuracoes.
Os pacotes nao incluem temas, bancos pessoais ou versoes importadas de usuarios.

Leitura das Biblias incluidas, temas, backup e PDF funcionam offline.
Pesquisa/download de fotografias externas precisa de internet.
O navegador e o proprio Windows continuam sendo requisitos do sistema.
O pacote nao possui assinatura digital comercial.
'''
(root / 'LEIA-ME.txt').write_text(readme, encoding='utf-8-sig')
files = sorted(p for p in root.rglob('*') if p.is_file() and p.name != 'manifesto-distribuicao.json')
manifest = {'architecture': args.arch, 'selfContained': True, 'runtime': runtime,
            'files': [{'path': p.relative_to(root).as_posix(), 'size': p.stat().st_size,
                       'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in files]}
(root / 'manifesto-distribuicao.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
files.append(root / 'manifesto-distribuicao.json')
zip_path = out / f'Biblia.Web-Windows-{args.arch}.zip'
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for p in files:
        z.write(p, Path('Biblia.Web') / p.relative_to(root))

ns = 'http://wixtoolset.org/schemas/v4/wxs'
ET.register_namespace('', ns)
def element(parent, tag, **attrs):
    return ET.SubElement(parent, f'{{{ns}}}{tag}', attrs)
def stable(prefix, value):
    return prefix + hashlib.sha256(value.encode()).hexdigest()[:24]
wix = ET.Element(f'{{{ns}}}Wix')
package = element(wix, 'Package', Name=f'Biblia.Web ({args.arch})', Manufacturer='WRSoft',
                  Version='1.26.908', Language='1046', Scope='perMachine',
                  UpgradeCode=str(uuid.uuid5(uuid.NAMESPACE_URL, f'https://wrsoft.local/Biblia.Web/{args.arch}')))
element(package, 'MajorUpgrade', DowngradeErrorMessage='Uma versao mais recente ja esta instalada.')
element(package, 'MediaTemplate', EmbedCab='yes', CompressionLevel='high')
property_os = element(package, 'Property', Id='WINDOWSBUILD')
element(property_os, 'RegistrySearch', Id='WindowsBuild', Root='HKLM',
        Key=r'SOFTWARE\Microsoft\Windows NT\CurrentVersion', Name='CurrentBuildNumber', Type='raw')
element(package, 'Launch', Condition='Installed OR WINDOWSBUILD >= 14393',
        Message='Este aplicativo requer Windows 10/11 atualizado. Windows 7/8/8.1 nao sao suportados.')
element(package, 'Icon', Id='AppIcon', SourceFile=str(root / 'appico.ico'))
element(package, 'Property', Id='ARPPRODUCTICON', Value='AppIcon')
standard = element(package, 'StandardDirectory', Id='ProgramFilesFolder' if args.arch == 'x86' else 'ProgramFiles64Folder')
install = element(standard, 'Directory', Id='INSTALLFOLDER', Name=f'Biblia.Web-{args.arch}')
directories = {'.': install}
for p in sorted({str(f.relative_to(root).parent) for f in files}, key=lambda x: (len(Path(x).parts), x)):
    path = Path(p)
    if p == '.':
        continue
    # Recursively create intermediate directories, including those without files.
    for part_index in range(1, len(path.parts) + 1):
        sub = Path(*path.parts[:part_index])
        key = str(sub)
        if key not in directories:
            directories[key] = element(directories[str(sub.parent)], 'Directory',
                                       Id=stable('D', key), Name=sub.name)
feature = element(package, 'Feature', Id='Application', Title='Biblia.Web', Level='1')
element(package, 'StandardDirectory', Id='DesktopFolder')
menu = element(package, 'StandardDirectory', Id='ProgramMenuFolder')
element(menu, 'Directory', Id='AppMenuFolder', Name=f'Biblia.Web ({args.arch})')
for p in files:
    relative = str(p.relative_to(root))
    component_id = stable('C', relative)
    component = element(directories[str(p.relative_to(root).parent)], 'Component', Id=component_id,
                        Guid=str(uuid.uuid5(uuid.NAMESPACE_URL, f'Biblia.Web/{args.arch}/{relative}')))
    file = element(component, 'File', Id=stable('F', relative), Source=str(p), KeyPath='yes')
    if p.name == 'Biblia.Web.exe':
        for directory, suffix in [('DesktopFolder', 'Desktop'), ('AppMenuFolder', 'Menu')]:
            element(file, 'Shortcut', Id=f'Shortcut{suffix}', Directory=directory,
                    Name=f'Biblia.Web ({args.arch})', Advertise='yes', WorkingDirectory='INSTALLFOLDER',
                    Icon='AppIcon', IconIndex='0')
        element(component, 'RemoveFolder', Id='RemoveAppMenu', Directory='AppMenuFolder', On='uninstall')
    element(feature, 'ComponentRef', Id=component_id)
ET.indent(wix)
ET.ElementTree(wix).write(out / f'Biblia.Web-{args.arch}.wxs', encoding='utf-8', xml_declaration=True)
print(f'{args.arch}: {len(files)} files; ZIP {zip_path.stat().st_size / 1024**2:.1f} MiB')
