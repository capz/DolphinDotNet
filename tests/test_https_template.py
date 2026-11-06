"""Certificate contents must survive managed fixture source generation literally."""
import json
from pathlib import Path
import re
import sys
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from ci_https import render_program

class HttpsTemplateTests(unittest.TestCase):
    def test_pem_tokens_are_not_rescanned(self):
        # URL and UTC are valid base64 sequences in real random certificates.
        # Include every marker and literal dollar text to check substitution is
        # simultaneous, including values that look like template syntax.
        pem='-----BEGIN CERTIFICATE-----\nCAURLUTCWRONGENTROPY$URL\n-----END CERTIFICATE-----\n'
        program=render_program(pem,bytes(range(48)),1793966400,'https://localhost:1234','https://wrong.example:1234/final')
        literal=re.search(r'GetBytes\(("(?:\\.|[^"\\])*")\)',program).group(1)
        self.assertEqual(json.loads(literal),pem)
        self.assertIn('TlsConfiguration.Initialize(ca,seed,1793966400)',program)
        self.assertIn('GetStringAsync("https://wrong.example:1234/final")',program)
        self.assertIn('var seed=new byte[]{'+','.join(map(str,range(48)))+'}',program)

if __name__=='__main__':unittest.main()
