#!/usr/bin/env python3
"""Reproduce the pinned exFAT false-ENOMEM limit, then verify the matched SDK fix."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from build_fat_overlay import checkout
from prepare_fat_overlay import EXFAT_PATH_DEPTH,FATFS_REVISION,LIBDVM_REVISION,prepare,verify_source

def run(*args):
    print('+',' '.join(map(str,args)),flush=True)
    subprocess.run(list(map(str,args)),cwd=ROOT,check=True)

def errno_source(tree,destination):
    source=(tree/'source/fat_driver.c').read_text()
    begin=source.index('static inline bool _FAT_set_errno(')
    end=source.index('static inline const char* _FAT_strip_device',begin)
    destination.write_text('#include <stdbool.h>\n#include <stdint.h>\n#include <errno.h>\n'
                           '#define dvmDebug(...) ((void)0)\n'+source[begin:end])

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,help='existing clean checkout of the exact pinned libdvm and FatFs')
    parser.add_argument('--evidence',type=Path,help='retain logs, source hashes and reproduction metadata')
    options=parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='dnd-fatfs-depth-') as temp:
        work=Path(temp)
        source=options.source.resolve() if options.source else work/'upstream'
        checkout(source);verify_source(source)
        patched=prepare(source,work/'patched')
        # Reject ANY attempted dynamic allocation from the exact FatFs units.
        # This is portable across the framework's Windows and macOS runners.
        guard=work/'allocation-guard.h'
        guard.write_text('#include <stdlib.h>\n#define malloc fatfs_test_malloc\n'
                         '#define calloc fatfs_test_calloc\n#define realloc fatfs_test_realloc\n'
                         '#define free fatfs_test_free\n')
        image=work/'identical-exfat.img';logs={};hash_before=None
        for label,tree,mode in [('unpatched',source,'--baseline'),('patched',patched,'--patched')]:
            build=work/(label+'-build');build.mkdir();errno_source(tree,build/'fatfs_errno.inc')
            include=tree/'fatfs/source'
            flags=['cc','-std=c11','-O2','-Wall','-Wextra','-Werror','-I'+str(include)]
            objects=[]
            for module in ('ff','ffunicode'):
                target=build/(module+'.o')
                run(*flags,'-include',guard,'-c',include/(module+'.c'),'-o',target);objects.append(target)
            binary=build/'fatfs-depth'
            run(*flags,'-I'+str(build),ROOT/'tests/fatfs_depth_test.c',*objects,'-o',binary)
            result=subprocess.run([str(binary),mode,str(image)],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
            print(result.stdout,end='',flush=True);logs[label]=result.stdout
            if result.returncode:raise RuntimeError(label+' exact FatFs depth fixture failed')
            digest=hashlib.sha256(image.read_bytes()).hexdigest()
            if hash_before is None:hash_before=digest
            else:assert digest==hash_before,'Patched read-only pass changed the input image'
        proof={'libdvm':LIBDVM_REVISION,'fatfs':FATFS_REVISION,'originalExfatPathDepth':10,
               'patchedExfatPathDepth':EXFAT_PATH_DEPTH,'unpatchedDepth11Result':'FR_NOT_ENOUGH_CORE -> ENOMEM',
               'patchedDepth11And31And64Pass':True,'patchedDepth65Result':'FR_PATH_TOO_DEEP -> ENAMETOOLONG',
               'fat32Depth65Unchanged':True,'metadataNativeAllocations':0,'metadataSectorWrites':0,
               'identicalInputImageSha256':hash_before,'identicalInputImageBytes':image.stat().st_size,
               'sources':json.loads((patched/'source-manifest.json').read_text())}
        if options.evidence:
            evidence=options.evidence.resolve();evidence.mkdir(parents=True,exist_ok=True)
            for label,log in logs.items():(evidence/(label+'-depth.log')).write_text(log)
            (evidence/'fatfs-depth-proof.json').write_text(json.dumps(proof,indent=2)+'\n')
            shutil.copy2(patched/'source-manifest.json',evidence/'fatfs-source-manifest.json')
        print('Pinned FatFs before/after depth regression passed; image SHA256 '+str(hash_before))

if __name__=='__main__':main()
