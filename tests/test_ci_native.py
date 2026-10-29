"""Execute the CI entry point without invoking the native compiler."""
import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("ci_test_suite",ROOT/"scripts/ci_test_suite.py")
suite=importlib.util.module_from_spec(spec)
spec.loader.exec_module(suite)

class NativeCiTests(unittest.TestCase):
    def test_native_executes_clean_debug_and_optimized_builds_with_assertions(self):
        with patch.object(suite,"run") as run:
            suite.main("native")
        calls=[c.args for c in run.call_args_list]
        builds=[(i,c) for i,c in enumerate(calls) if c[:4]==("make","-C","tests","test")]
        self.assertEqual(2,len(builds))
        for index,command in builds:
            self.assertEqual(("make","-C","tests","clean"),calls[index-1])
            self.assertEqual(("make","-C","tests","clean"),calls[index+1])
            self.assertNotIn("-DNDEBUG",command[4])
        self.assertNotIn("-O3",builds[0][1][4])
        for flag in ("-O3","-flto","-fno-strict-aliasing"):
            self.assertIn(flag,builds[1][1][4])

    def test_failed_native_test_still_cleans_and_fails_the_job(self):
        for failing_mode in (0,1):
            builds=0
            def run(*args):
                nonlocal builds
                if args[:4]==("make","-C","tests","test"):
                    builds+=1
                    if builds==failing_mode+1:
                        raise subprocess.CalledProcessError(1,args)
            with self.subTest(mode=failing_mode),patch.object(suite,"run",side_effect=run) as invoke:
                with self.assertRaises(subprocess.CalledProcessError):suite.main("native")
                self.assertEqual(("make","-C","tests","clean"),invoke.call_args.args)

if __name__=="__main__":unittest.main()
