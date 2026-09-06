"""Targeted repair after audit; requires the application to be stopped first."""
import datetime
import json
import pathlib
import sqlite3
import sys

path = pathlib.Path(sys.argv[1]).resolve()
if not path.is_file():
    raise FileNotFoundError(path)
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
backup = path.parent / 'backups' / ('indice-setting-' + stamp + '.db')
backup.parent.mkdir(exist_ok=True)
db = sqlite3.connect(path, timeout=10)
with sqlite3.connect(backup) as target:
    db.backup(target)
tables = [r[0] for r in db.execute("select name from sqlite_schema where type='table' order by name")]
def snapshot():
    return {t: sorted(db.execute('select * from "' + t.replace('"', '""') + '" NOT INDEXED').fetchall(), key=repr) for t in tables}
before = snapshot()
issues = db.execute('pragma integrity_check').fetchall()
if issues != [('wrong # of entries in index sqlite_autoindex_Setting_1',)]:
    raise RuntimeError(f'Unexpected diagnosis; no repair performed: {issues}')
try:
    db.execute('begin immediate')
    db.execute('REINDEX sqlite_autoindex_Setting_1')
    assert db.execute('pragma integrity_check').fetchall() == [('ok',)]
    assert db.execute('pragma foreign_key_check').fetchall() == []
    assert snapshot() == before, 'Table data changed; rollback required'
    assert db.execute('select Value from Setting where Key=?', ('PdfReportTypography',)).fetchall() == []
    db.commit()
except BaseException:
    db.rollback()
    raise
result = {'backup': str(backup), 'database': str(path), 'integrity': db.execute('pragma integrity_check').fetchall(),
          'all_table_rows_unchanged': True, 'counts': {t:len(v) for t,v in before.items()}, 'operation':'REINDEX sqlite_autoindex_Setting_1'}
db.close()
print(json.dumps(result, ensure_ascii=False, indent=2))
