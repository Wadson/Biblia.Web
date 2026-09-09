"""Read-only audit of a Windows self-contained distribution and its native imports."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).parent / 'python'))
import pefile

parser = argparse.ArgumentParser()
parser.add_argument('publish', type=Path)
parser.add_argument('--arch', choices=['x64', 'x86', 'arm64'], required=True)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
root = args.publish.resolve()
expected = {'x64': 0x8664, 'x86': 0x14c, 'arm64': 0xaa64}[args.arch]
manifest = json.loads((root / 'manifesto-distribuicao.json').read_text(encoding='utf-8'))
for file in manifest['files']:
    path = root / file['path']
    assert path.stat().st_size == file['size'], file['path']
    assert hashlib.sha256(path.read_bytes()).hexdigest() == file['sha256'], file['path']
native = []
for path in sorted(root.glob('*')):
    if path.suffix.lower() not in ['.dll', '.exe']:
        continue
    pe = pefile.PE(str(path), fast_load=True)
    if pe.OPTIONAL_HEADER.DATA_DIRECTORY[14].VirtualAddress:
        pe.close()
        continue
    assert pe.FILE_HEADER.Machine == expected, f'Wrong architecture: {path}'
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT']])
    imports = [entry.dll.decode() for entry in getattr(pe, 'DIRECTORY_ENTRY_IMPORT', [])]
    # CRT beyond Windows UCRT must be present app-locally.
    for name in imports:
        if name.lower().startswith(('vcruntime', 'msvcp', 'concrt')):
            assert (root / name).exists(), f'Missing redistributable: {name}'
    native.append({'file': path.name, 'machine': hex(pe.FILE_HEADER.Machine), 'imports': imports})
    pe.close()
report = {'architecture': args.arch, 'filesVerified': len(manifest['files']), 'native': native,
          'runtimeOptions': manifest['runtime'], 'personalDatabasePresent': bool(list(root.rglob('bibliatema.db')))}
args.report.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(f"PASS {args.arch}: {len(manifest['files'])} hashes; {len(native)} native binaries")
