"""Package the verified EXE, documentation and release checksums."""
import hashlib,json,zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
REPO='OldManCraveGuitars-bit/XBOX-GunValkyrie-Kr'
EXE='XBOX-GunValkyrie-Kr-v0.9-Patcher.exe'
ZIP='XBOX-GunValkyrie-Kr-v0.9.zip'
PATCH='GunValkyrie-Kr-v0.9.gvpatch'


def sha(path):
    with Path(path).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()


def main():
    p=ROOT;h=json.loads((p/'build/release-hashes.json').read_text('utf8'))
    assert sha(p/'dist'/EXE)==h['patcher_exe']['sha256']
    checks='건 발키리 한국어 패치 V0.9 · SHA-256\n\n'
    for name,key in [('Japan (En,Ja) 원본 ISO','source_iso'),('V0.9 패치 결과 ISO','patched_iso'),(EXE,'patcher_exe'),(PATCH,'patch_data')]:
        checks+=f'{name}\n크기: {h[key]["bytes"]:,} bytes\nSHA-256: {h[key]["sha256"]}\n\n'
    (p/'dist/HASHES.txt').write_text(checks,'utf-8-sig')
    instructions=f'''건 발키리 (GUNVALKYRIE) 한국어 패치 V0.9
코리아 로컬라이즈 GiKakNo

1. {EXE}를 실행합니다.
2. Gun Valkyrie (Japan) (En,Ja) 원본 ISO를 선택합니다.
3. 한글 ISO를 저장할 새 경로를 지정하고 [한글 패치 시작]을 누릅니다.
4. 결과 ISO SHA-256 검증 완료를 기다립니다.

Windows 10/11의 기본 .NET Framework에서 실행합니다. Python 설치는 필요 없습니다.
원본과 기존 출력은 덮어쓰지 않습니다. 이미 패치된 ISO에는 재적용하지 않습니다.
원본과 결과 ISO는 모두 722,206,720바이트입니다. 출력 드라이브에 800MB 이상을 확보하세요.
HASHES.txt에 원본·결과·EXE의 SHA-256이 있습니다. EXE가 적용 전후 해시를 자동 검사합니다.

저장소: https://github.com/{REPO}
릴리스: https://github.com/{REPO}/releases/tag/v0.9

타이틀, 메뉴, 일본어 문구, 오프닝 자막과 최신 13차 UI 수정 내용을 포함합니다.
게임 원본 ISO는 포함하지 않습니다.
'''
    (p/'dist/사용법.txt').write_text(instructions,'utf-8-sig')
    with zipfile.ZipFile(p/'dist'/ZIP,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for name in [EXE,'사용법.txt','HASHES.txt']:z.write(p/'dist'/name,name)
    assets=[(EXE,p/'dist'/EXE),(ZIP,p/'dist'/ZIP),(PATCH,p/'patches'/PATCH),('HASHES.txt',p/'dist/HASHES.txt')]
    sums=''.join(f'{sha(path)}  {name}\n' for name,path in assets)
    (p/'SHA256SUMS.txt').write_text(sums,'utf8');(p/'dist/SHA256SUMS.txt').write_text(sums,'utf8')
    table='| 대상 | 크기 (bytes) | MD5 | SHA-1 | SHA-256 |\n| --- | ---: | --- | --- | --- |\n'
    for name,key in [('일본판 원본 ISO','source_iso'),('V0.9 결과 ISO','patched_iso'),(EXE,'patcher_exe'),(PATCH,'patch_data')]:
        r=h[key];table+=f'| {name} | {r["bytes"]:,} | `{r["md5"]}` | `{r["sha1"]}` | `{r["sha256"]}` |\n'
    (p/'HASHES.md').write_text('# V0.9 해시\n\n'+table+'\n## 배포 파일 SHA-256\n\n```text\n'+sums+'```\n','utf8')
    manifest=dict(version='V0.9',game_content_revision=13,repository=f'https://github.com/{REPO}',source=h['source_iso'],output=h['patched_iso'],
                  assets=[dict(name=name,bytes=path.stat().st_size,sha256=sha(path)) for name,path in assets],screenshots_in_exe=False)
    (p/'release-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps(manifest,ensure_ascii=False,indent=2))


if __name__=='__main__':main()
