"""Independent QA of the 50-theme fixture, including printed numbers and internal links.
Usage: python tools/ThemeReportQa/verify_toc.py input.pdf output.csv
"""
import csv
import re
import sys
from pathlib import Path
import pdfplumber
from pypdf import PdfReader

source, output = map(Path, sys.argv[1:3])
reader = PdfReader(source)
named = reader.named_destinations
assert len(named) == 50
assert set(named) == {f'theme-{i}-section-{i-1}' for i in range(1, 51)}
assert len(reader.outline) == 50
rows = []
with pdfplumber.open(source) as visual:
    for page_number, (page, rendered) in enumerate(zip(reader.pages, visual.pages), 1):
        text = rendered.extract_text() or ''
        assert f'Página {page_number} de {len(reader.pages)}' in text
        left = 58 if page_number % 2 else 40
        assert all(left-2 <= c['x0'] and c['x1'] <= left+499 for c in rendered.chars)
        for reference in page.get('/Annots', []):
            annotation = reference.get_object()
            assert annotation['/Subtype'] == '/Link'
            key = annotation.get('/Dest') or annotation['/A']['/D']
            destination = named[key]
            target_number = reader.get_destination_page_number(destination) + 1
            rect = list(map(float, annotation['/Rect']))
            top, bottom = float(page.mediabox.height)-rect[3], float(page.mediabox.height)-rect[1]
            number_chars = [c for c in rendered.chars if c['x0'] > rect[2]-36 and c['top'] >= top and c['bottom'] <= bottom]
            printed = ''.join(c['text'] for c in number_chars)
            assert printed == str(target_number), (key, printed, target_number)
            index = int(re.search(r'theme-(\d+)', key)[1])
            heading_top = float(reader.pages[target_number-1].mediabox.height) - float(destination.top)
            chars = [c for c in visual.pages[target_number-1].chars if abs(c['size']-16) < .1 and heading_top <= c['top'] < heading_top+22]
            assert ''.join(c['text'] for c in chars).startswith(f'Tema {index:02}'), key
            assert reader.get_destination_page_number(reader.outline[index-1])+1 == target_number
            rows.append((key, page_number, printed, target_number, heading_top))
    all_text = '\n'.join(p.extract_text() or '' for p in visual.pages)
    markers = re.findall(r'QA\d{2}REF\d{2}', all_text)
    assert len(markers) == len(set(markers)) == 213
    for page in reader.pages:
        for font in page['/Resources']['/Font'].values():
            font = font.get_object()
            assert '/FontFile2' in font['/DescendantFonts'][0].get_object()['/FontDescriptor']
assert len(rows) == 50
assert [r[0] for r in rows] == [f'theme-{i}-section-{i-1}' for i in range(1, 51)]
with output.open('w', newline='', encoding='utf-8-sig') as stream:
    writer = csv.writer(stream)
    writer.writerow(['marcador', 'pagina_sumario', 'numero_impresso', 'pagina_real', 'topo_cabecalho_pt'])
    writer.writerows(rows)
print(f'OK: {len(reader.pages)} páginas, 50 entradas/links/favoritos, 213 referências únicas, fontes incorporadas; sumário nas páginas {sorted({r[1] for r in rows})}.')
