"""Actual compiler checks for CI object reuse, flag separation and header changes."""
import os
from pathlib import Path
import shutil
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from ci_cc import compile_command


class CiCompileTests(unittest.TestCase):
    def test_link_and_header_changes_use_fresh_objects(self):
        with TemporaryDirectory() as folder:
            root=Path(folder);header=root/'value.h';a=root/'a.c';b=root/'b.c';exe=root/'program'
            a.write_text('#include "value.h"\nint get(void){return VALUE;}\n')
            b.write_text('int get(void);int main(void){return get();}\n')
            arguments=['-std=c11','-Wall','-Wextra','-Werror',str(a),str(b),'-o',str(exe)]
            for value in (1,2):
                header.write_text(f'#define VALUE {value}\n')
                compile_command(arguments)
                self.assertEqual(value,subprocess.run([str(exe)]).returncode)

    def test_flags_and_link_options_remain_separate(self):
        calls=[]
        with patch('ci_cc.shutil.which',return_value='/usr/bin/ccache'),patch('ci_cc.subprocess.run',side_effect=lambda cmd,**_:calls.append(cmd)):
            compile_command(['-m32','-DVALUE=3','a.c','b.c','existing.o','-lm','-Wl,--wrap=malloc','-o','out'])
        self.assertEqual(3,len(calls))
        for command in calls[:2]:
            self.assertIn('-m32',command);self.assertIn('-DVALUE=3',command)
            self.assertNotIn('-lm',command);self.assertNotIn('-Wl,--wrap=malloc',command)
            self.assertNotIn('existing.o',command)
        self.assertIn('-lm',calls[-1]);self.assertIn('-Wl,--wrap=malloc',calls[-1]);self.assertIn('existing.o',calls[-1])
        self.assertNotIn('a.c',calls[-1]);self.assertNotIn('b.c',calls[-1])

    def test_compile_only_and_unfamiliar_modes_pass_through(self):
        for arguments in [['-c','a.c','-o','a.o'],['-E','a.c'],['-include','header.c','a.c','-o','app']]:
            with patch('ci_cc.shutil.which',return_value=None),patch('ci_cc.subprocess.run') as run:
                compile_command(arguments)
                run.assert_called_once_with(['/usr/bin/cc']+arguments,check=True)

    def test_compiler_failure_propagates(self):
        with patch('ci_cc.subprocess.run',side_effect=subprocess.CalledProcessError(1,['cc'])):
            with self.assertRaises(subprocess.CalledProcessError):compile_command(['a.c','-o','app'])


if __name__=='__main__':unittest.main()
