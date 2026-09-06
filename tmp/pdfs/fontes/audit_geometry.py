from pathlib import Path
import pdfplumber
from pypdf import PdfReader
for mode in ['padrao','minimo','maximo']:
    path=Path('output/pdf/fontes')/f'fontes-{mode}.pdf'
    reader=PdfReader(path)
    with pdfplumber.open(path) as pdf:
        entries=[]
        for page_number,p in enumerate(reader.pages):
            for a in p.get('/Annots',[]):
                a=a.get_object(); dest=reader.named_destinations[str(a['/A']['/D'])]
                target=reader.get_destination_page_number(dest)
                x0,y0,x1,y1=map(float,a['/Rect']); page=pdf.pages[page_number]
                chars=[c for c in page.chars if c['x0']>x1-36 and c['x1']<=x1+1 and c['y0']>=y0 and c['y1']<=y1]
                number=''.join(c['text'] for c in chars)
                assert number==str(target+1),(mode,number,target)
                heading=[c for c in pdf.pages[target].chars if abs(c['size']-16)<.01 and float(dest.top)-23<c['y1']<=float(dest.top)+1]
                assert heading,(mode,target)
                entries.append(target+1)
        assert len(entries)==26
        for n,page in enumerate(pdf.pages,1):
            left=58 if n%2 else 40
            assert page.chars
            for c in page.chars:
                assert left-2<=c['x0']<=c['x1']<=left+499,(mode,n,c)
                assert 12<=c['y0'] and c['y1']<=804,(mode,n,c)
    print(mode,'26 TOC destinations, numbers and actual headings; all page bounds OK', flush=True)
