from pathlib import Path
from PIL import Image, ImageDraw
from pypdf import PdfReader
import json,re
root=Path('output/pdf/fontes'); out=Path('tmp/pdfs/fontes')
summary=[]
for mode in ['padrao','minimo','maximo']:
    pdf=PdfReader(root/f'fontes-{mode}.pdf'); text='\n'.join(p.extract_text() for p in pdf.pages)
    assert 'OBSERVAÇÃO DO VÍNCULO' not in text and 'OBSERVAÇÃO:' in text
    assert len(re.findall(r'QA\d{2}REF\d{2}',text))==96
    assert len(pdf.outline)==26
    for page in pdf.pages:
        fonts=page['/Resources']['/Font'].get_object()
        for f in fonts.values():
            f=f.get_object(); f=f['/DescendantFonts'][0].get_object() if '/DescendantFonts' in f else f
            assert '/FontFile2' in f['/FontDescriptor'].get_object()
    summary.append({'mode':mode,'pages':len(pdf.pages),'references':96,'toc_pages':sum(bool(p.get('/Annots')) for p in pdf.pages),'embedded_fonts':True})
    images=sorted(out.glob(f'{mode}-*.png'))
    for start in range(0,len(images),9):
        sheet=Image.new('RGB',(1500,2190),'#b0b0b0'); draw=ImageDraw.Draw(sheet)
        for i,path in enumerate(images[start:start+9]):
            im=Image.open(path);im.thumbnail((490,700)); x=(i%3)*500;y=(i//3)*730
            sheet.paste(im,(x,y+25));draw.text((x+5,y+5),path.stem,fill='black')
        sheet.save(out/f'contact-{mode}-{start//9+1:02}.jpg')
(root/'qa-summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8');print(summary)
