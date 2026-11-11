"""IPL image layout and malformed boot-input checks, without a cross compiler."""
import importlib.util
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('apploader', ROOT/'boot/gamecube/pack.py')
boot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(boot)


def elf():
    data = bytearray(52 + 3*40 + 32)
    data[:7] = b'\x7fELF\x01\x02\x01'
    struct.pack_into('>HHI', data, 16, 2, 20, 1)
    struct.pack_into('>I', data, 24, boot.BASE)
    struct.pack_into('>I', data, 32, 52)
    struct.pack_into('>HHH', data, 46, 40, 3, 0)
    struct.pack_into('>6I', data, 52+40, 0, 1, 6, boot.BASE, 172, 32)
    struct.pack_into('>6I', data, 52+80, 0, 8, 3, boot.BASE+64, 0, 256)
    data[172:] = b'T'*32
    return data


def dol():
    data = bytearray(288)
    for at, value in ((0,256),(0x48,0x80003100),(0x90,32),(0xd8,0x80004000),(0xdc,17),(0xe0,0x80003100)):
        struct.pack_into('>I',data,at,value)
    data[256:] = b'T'*32
    return data


class ApploaderTests(unittest.TestCase):
    def test_complete_deterministic_header_padding_and_bss(self):
        result = boot.image(elf())
        self.assertEqual(result[:16],boot.DATE)
        self.assertEqual(struct.unpack_from('>4I',result,16),(boot.BASE,320,0,0))
        self.assertEqual(result[32:64],b'T'*32)
        self.assertEqual(result[64:],bytes(288))
        self.assertEqual(result,boot.image(elf()))
        self.assertLessEqual(len(result),boot.DOL_OFFSET-boot.DISC_OFFSET)

    def test_invalid_elf_header_table_machine_and_entry(self):
        for at, value in ((5,1),(17,0),(19,21),(27,4),(47,0),(49,255)):
            data=elf();data[at]=value
            with self.subTest(at=at), self.assertRaises(ValueError):boot.image(data)
        for cut in (0,51,100,200):
            with self.assertRaises(ValueError):boot.image(elf()[:cut])

    def test_allocated_ranges_entry_backing_and_overlap(self):
        for at,value in ((52+40+16,1000),(52+40+8,2),
                         (52+80+12,boot.BASE),(52+80+20,boot.MAX_BODY)):
            data=elf();struct.pack_into('>I',data,at,value)
            with self.subTest(at=at),self.assertRaises(ValueError):boot.image(data)

    def test_dol_alignment_ranges_bss_and_entry(self):
        boot.validate_dol(dol())
        for at,value in ((0,257),(0x48,boot.BASE),(0x90,33),
                         (0xd8,boot.BASE),(0xe0,0x80004000),(0x48,0x80003000)):
            data=dol();struct.pack_into('>I',data,at,value)
            with self.subTest(at=at),self.assertRaises(ValueError):boot.validate_dol(data)
        data=dol();struct.pack_into('>I',data,0xd8,0x80003100)
        with self.assertRaisesRegex(ValueError,'nonzero'):boot.validate_dol(data)
        data[256:]=bytes(32);boot.validate_dol(data)

    def test_failed_pack_does_not_overwrite_previous_output(self):
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp);source=folder/'boot.elf';output=folder/'apploader.img'
            source.write_bytes(elf());output.write_bytes(b'previous valid image')
            bad=folder/'bad.dol';bad.write_bytes(b'bad')
            result=subprocess.run(['python3',str(ROOT/'boot/gamecube/pack.py'),str(source),str(output),'--dol',str(bad)],capture_output=True)
            self.assertNotEqual(result.returncode,0)
            self.assertEqual(output.read_bytes(),b'previous valid image')

    def test_standalone_make_validates_dol_even_with_a_newer_cached_image(self):
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp);output=folder/'apploader';bad=folder/'bad.dol'
            output.with_suffix('.elf').write_bytes(elf())
            output.with_suffix('.img').write_bytes(boot.image(elf()))
            bad.write_bytes(b'bad')
            # Make the cached outputs newer than every source and supplied DOL.
            future=max(p.stat().st_mtime for p in (ROOT/'boot/gamecube').iterdir() if p.is_file())+60
            for suffix in ('.elf','.img'):os.utime(output.with_suffix(suffix),(future,future))
            result=subprocess.run(['make','-f',str(ROOT/'boot/gamecube/Makefile'),
                                   'OUTPUT='+str(output),'DOL='+str(bad)],capture_output=True,text=True)
            self.assertNotEqual(result.returncode,0)
            self.assertIn('truncated DOL header',result.stderr)
            self.assertEqual(output.with_suffix('.img').read_bytes(),boot.image(elf()))

    def test_callback_protocol_and_bss_semantics(self):
        # Run the C callback logic on the host. Only the pointer word width and
        # PPC cache instructions are adapted; ELF/DOL wire layout is tested above.
        source=(ROOT/'boot/gamecube/apploader.c').read_text().replace('typedef unsigned int u32;', 'typedef __UINTPTR_TYPE__ u32;')
        start=source.index('static void coherent(');end=source.index('static void init(',start)
        source=source[:start]+'static unsigned flushes; static void coherent(void *p,u32 n){assert(p&&n);flushes++;}\n'+source[end:]
        fixture=r'''
int main(void){
 void (*initialize)(report_fn);int (*read)(void **,int *,int *);void *(*finish)(void);
 unsigned char text[32],data[32],bss[37];for(int i=0;i<37;i++)bss[i]=123;
 entry(&initialize,&read,&finish);initialize(0);void *dst;int size,offset;
 assert(read(&dst,&size,&offset)==1&&dst==header&&size==256&&offset==0x10000);
 for(int i=0;i<64;i++)header[i]=0;
 header[0]=256;header[0x48/4]=(u32)text;header[0x90/4]=32;
 header[17]=288;header[0x48/4+17]=(u32)data;header[0x90/4+17]=32;
 header[0xd8/4]=(u32)bss;header[0xdc/4]=37;header[0xe0/4]=(u32)text;
 assert(read(&dst,&size,&offset)==1&&dst==text&&size==32&&offset==0x10100);
 assert(read(&dst,&size,&offset)==1&&dst==data&&size==32&&offset==0x10120);
 assert(read(&dst,&size,&offset)==0&&flushes==3);
 assert(finish()==text&&flushes==4);for(int i=0;i<37;i++)assert(!bss[i]);
 initialize(0);assert(read(&dst,&size,&offset)==1&&dst==header);return 0;
}
'''
        with tempfile.TemporaryDirectory() as temp:
            source_file=Path(temp)/'probe.c';binary=Path(temp)/'probe'
            source_file.write_text('#include <assert.h>\n'+source+fixture)
            subprocess.run(['cc','-std=c11','-Wall','-Wextra','-Werror',str(source_file),'-o',str(binary)],check=True)
            subprocess.run([str(binary)],check=True)


if __name__=='__main__':unittest.main()
