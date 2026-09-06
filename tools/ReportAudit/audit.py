import collections
import hashlib
import json
import pathlib
import sqlite3
import sys

root = pathlib.Path(sys.argv[1]).resolve()
source = root / 'original.db'
db = sqlite3.connect(source.as_uri() + '?mode=ro', uri=True)
db.row_factory = sqlite3.Row
out = {'database': str(source), 'integrity': [tuple(r) for r in db.execute('pragma integrity_check')],
       'foreign_keys': [tuple(r) for r in db.execute('pragma foreign_key_check')]}
tables = [r[0] for r in db.execute("select name from sqlite_schema where type='table' order by name")]
def rows(c, table):
    return sorted([tuple(r) for r in c.execute('select * from "' + table + '" NOT INDEXED')], key=repr)
before = {t: rows(db, t) for t in tables}
out['tables'] = {t: {'count': len(v), 'sha256': hashlib.sha256(repr(v).encode()).hexdigest()} for t, v in before.items()}
out['bibles'] = []
out['links'] = []
versions = {}
for v in db.execute('select * from BibleVersionCatalog'):
    p = pathlib.Path(v['InstalledPath'])
    c = sqlite3.connect(p.as_uri() + '?mode=ro', uri=True)
    versions[v['Id']] = (v['Code'], c)
    out['bibles'].append({'code': v['Code'], 'path': str(p), 'integrity': c.execute('pragma integrity_check').fetchall()})
for link in db.execute('select rt.ThemeId,rt.ReferenceId,rt.BibleVersionId,r.BookReferenceId,r.Chapter,r.VerseStart,r.VerseEnd from ReferenceTheme rt join SavedReference r on r.Id=rt.ReferenceId'):
    entry = dict(link)
    version = versions.get(link['BibleVersionId'])
    if version is None:
        entry['error'] = 'missing version'
    else:
        code, c = version
        verses = c.execute('select v.verse,v.text from verse v join book b on b.id=v.book_id where b.book_reference_id=? and v.chapter=? and v.verse between ? and ?', (link['BookReferenceId'], link['Chapter'], link['VerseStart'], link['VerseEnd'])).fetchall()
        counts = collections.Counter(v[0] for v in verses)
        entry.update(version=code, missing=[n for n in range(link['VerseStart'],link['VerseEnd']+1) if n not in counts], duplicates=[n for n,cnt in counts.items() if cnt>1], empty=[n for n,t in verses if not t or not t.strip()])
    out['links'].append(entry)
out['unlinked_references'] = [r[0] for r in db.execute('select Id from SavedReference where Id not in (select ReferenceId from ReferenceTheme)')]
for name, repair in [('before', False), ('after', True)]:
    dest = root / name
    dest.mkdir(exist_ok=True)
    target = dest / 'bibliatema.db'
    if target.exists():
        raise RuntimeError(f'Refusing overwrite: {target}')
    c = sqlite3.connect(target)
    db.backup(c)
    if repair:
        c.execute('REINDEX sqlite_autoindex_Setting_1')
        c.commit()
        out['repair'] = {'integrity': c.execute('pragma integrity_check').fetchall(), 'foreign_keys': c.execute('pragma foreign_key_check').fetchall(), 'all_table_rows_unchanged': all(rows(c,t)==before[t] for t in tables)}
    c.close()
(root / 'audit.json').write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k:v for k,v in out.items() if k not in ['links','tables','bibles']}, ensure_ascii=False, indent=2))
print('Link issues:', [r for r in out['links'] if r.get('error') or r.get('missing') or r.get('duplicates') or r.get('empty')])
