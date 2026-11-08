"""Create a deterministic data-only patch for the two supported ISO hashes."""
import argparse,gzip,hashlib,json,struct
from pathlib import Path
import numpy as np

SOURCE_SHA='33adbae5902b038ba42b156e7f4c2359008b4c02f34eefb05c56f1c6c126ce40'
OUTPUT_SHA='c975cdf2f76514f1bc53657567d682443bc804c92dd202144326e1803519cbd4'
SIZE=722206720
MAGIC=b'GVKR0901'


def sha(path):
    with Path(path).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--source',type=Path,required=True);ap.add_argument('--target',type=Path,required=True)
    ap.add_argument('--output',type=Path,default=Path('patches/GunValkyrie-Kr-v0.9.gvpatch'));args=ap.parse_args()
    assert args.source.stat().st_size==args.target.stat().st_size==SIZE
    assert sha(args.source)==SOURCE_SHA and sha(args.target)==OUTPUT_SHA
    stats=dict(format='GVKR0901',version='V0.9',block_bytes=64,source_bytes=SIZE,output_bytes=SIZE,
               source_sha256=SOURCE_SHA,output_sha256=OUTPUT_SHA,copy_bytes=0,literal_bytes=0,zero_bytes=0,operations=0)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    with args.source.open('rb') as source,args.target.open('rb') as target,args.output.open('xb') as raw:
        with gzip.GzipFile(fileobj=raw,mode='wb',filename='',mtime=0,compresslevel=9) as out:
            out.write(MAGIC+struct.pack('<qq',SIZE,SIZE)+bytes.fromhex(SOURCE_SHA)+bytes.fromhex(OUTPUT_SHA))
            offset=0
            while offset<SIZE:
                a=source.read(4*1024*1024);b=target.read(len(a));assert len(a)==len(b) and len(a)%64==0
                equal=np.all(np.frombuffer(a,dtype=np.uint8).reshape(-1,64)==np.frombuffer(b,dtype=np.uint8).reshape(-1,64),axis=1)
                boundaries=np.flatnonzero(np.r_[True,equal[1:]!=equal[:-1],True])
                for begin,end in zip(boundaries[:-1],boundaries[1:]):
                    at=int(begin)*64;length=int(end-begin)*64;stats['operations']+=1
                    if equal[begin]:
                        out.write(b'\0'+struct.pack('<qI',offset+at,length));stats['copy_bytes']+=length
                    else:
                        data=b[at:at+length]
                        if not any(data):out.write(b'\x02'+struct.pack('<I',length));stats['zero_bytes']+=length
                        else:out.write(b'\x01'+struct.pack('<I',length)+data);stats['literal_bytes']+=length
                offset+=len(a)
            out.write(b'\xff')
    assert sum(stats[k] for k in ('copy_bytes','literal_bytes','zero_bytes'))==SIZE
    stats.update(payload_bytes=args.output.stat().st_size,payload_sha256=sha(args.output))
    args.output.with_suffix('.json').write_text(json.dumps(stats,indent=2)+'\n','utf-8')
    print(json.dumps(stats,indent=2))


if __name__=='__main__':main()
