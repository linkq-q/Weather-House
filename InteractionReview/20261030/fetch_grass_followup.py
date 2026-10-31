from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import subprocess, re, html, json, hashlib

OUT = Path(r'C:/Users/31483/Desktop/天气屋/草地脚步试听_20261030')
RAW = OUT / '原始素材'
RAW.mkdir(parents=True, exist_ok=True)
SOURCES = [
 ('owl1','OwlStorm','https://freesound.org/people/OwlStorm/sounds/151230/'),
 ('owl2','OwlStorm','https://freesound.org/people/OwlStorm/sounds/151229/'),
 ('owl3','OwlStorm','https://freesound.org/people/OwlStorm/sounds/151228/'),
 ('owl4','OwlStorm','https://freesound.org/people/OwlStorm/sounds/151235/'),
 ('ali','Ali_6868','https://freesound.org/people/Ali_6868/sounds/384860/'),
 ('silent','SilentStrikeZ','https://freesound.org/people/SilentStrikeZ/sounds/389625/'),
]


def fetch(item):
    ident, author, page = item
    result = subprocess.run(['curl.exe', '-L', '--fail', '--max-time', '40', '--silent', '--show-error', page], capture_output=True, check=True)
    source = html.unescape(result.stdout.decode('utf-8'))
    assert 'creativecommons.org/publicdomain/zero/1.0' in source, 'CC0 evidence missing: ' + ident
    if 'freesound.org' in page:
        urls = sorted(set(re.findall(r'https://cdn\.freesound\.org/previews/[^\s"<>]+-hq\.mp3', source)))
        sound = re.search(r'/sounds/(\d+)/', page)[1]
        urls = [u for u in urls if '/' + sound + '_' in u]
    else:
        urls = sorted(set(re.findall(r'https://opengameart\.org/sites/default/files/[^\s"<>]+\.(?:wav|mp3)', source)))
        urls = [u for u in urls if '/audio_preview/' not in u]
    assert urls, 'No public audio download: ' + ident
    rows = []
    for i, url in enumerate(urls):
        target = RAW / (ident + (f'_{i+1}' if len(urls) > 1 else '') + Path(url).suffix)
        if not target.exists():
            subprocess.run(['curl.exe', '-L', '--fail', '--max-time', '45', '--silent', '--show-error', '-o', str(target), url], check=True)
        assert target.stat().st_size > 1000
        rows.append(dict(id=target.stem, author=author, page=page, download=url, license='CC0-1.0',
            verified_date='2026-10-30', public_hq_preview='freesound.org' in page,
            file=target.relative_to(OUT).as_posix(), bytes=target.stat().st_size,
            sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
    return rows

with ThreadPoolExecutor(max_workers=5) as pool:
    records = [row for rows in pool.map(fetch, SOURCES) for row in rows]
(OUT/'来源与许可.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')
print('PASS',len(records),'verified CC0 audio sources; no raw HTML saved')
