"""Guard the pre-sharding coverage and required matrix results without YAML dependencies."""
import json
import os
from pathlib import Path
import re
import unittest
from unittest.mock import patch

ROOT=Path(__file__).resolve().parents[1]
WORKFLOW=(ROOT/'.github/workflows/build.yml').read_text()
MANIFEST=json.loads((ROOT/'tests/ci_coverage_manifest.json').read_text())


def job(name):
    start=WORKFLOW.index('  '+name+':\n')
    end=re.search(r'^  [a-z][\w-]*:\s*$',WORKFLOW[start+1:],re.M)
    return WORKFLOW[start:start+1+end.start()] if end else WORKFLOW[start:]


def step_blocks(section):
    return re.findall(r'^      - .*?(?=^      - |\Z)',section.split('    steps:\n',1)[1],re.M|re.S)


class WorkflowCoverageTests(unittest.TestCase):
    def test_every_original_contract_is_owned_once(self):
        self.assertEqual({'managed':44,'gamecube':32,'dolphin':20},
                         {kind:sum(map(len,groups.values())) for kind,groups in MANIFEST.items()})
        for kind,groups in MANIFEST.items():
            seen=[]
            for group,names in groups.items():
                section=job('managed' if kind=='managed' else 'dolphin-smoke' if kind=='dolphin' else 'gamecube' if group=='emulator' else 'gamecube-extra')
                for name in names:
                    matching=[block for block in step_blocks(section) if block.splitlines()[0]=='      - name: '+name]
                    self.assertEqual(1,len(matching),name)
                    if not(kind=='gamecube' and group=='emulator'):
                        self.assertIn(f"if: matrix.group == '{group}'",matching[0],name)
                    seen.append(name)
            self.assertEqual(len(seen),len(set(seen)),kind)

    def test_emulator_waits_only_for_its_artifacts_but_all_jobs_are_required(self):
        self.assertIn('needs: [changes, gamecube]',job('dolphin-smoke'))
        gate=job('ci-result')
        self.assertIn('gamecube-extra',gate)
        self.assertIn("'gamecube-extra': 'gamecube'",gate)
        for name in ('managed','gamecube-extra','dolphin-smoke'):
            self.assertIn('fail-fast: false',job(name))
        self.assertIn('name: DolphinDotNet-gamecube\n',job('gamecube'))
        self.assertIn('name: DolphinDotNet-dolphin-smoke-${{ matrix.group }}',job('dolphin-smoke'))

    def test_missing_or_failed_emulator_cases_cannot_pass(self):
        section=job('dolphin-smoke')
        code=section.split("python3 - <<'PYRESULTS'\n",1)[1].split('          PYRESULTS',1)[0]
        code='\n'.join(line[10:] for line in code.splitlines())
        blocks=step_blocks(section)
        for group,names in MANIFEST['dolphin'].items():
            ids=[]
            for name in names:
                block=next(b for b in blocks if b.splitlines()[0]=='      - name: '+name)
                ids.append(re.search(r'        id: (\S+)',block).group(1))
            good={name:{'outcome':'success'} for name in ids}
            with patch.dict(os.environ,{'GROUP':group,'STEP_RESULTS':json.dumps(good)}):exec(code,{})
            for failed in ids:
                for outcome in ('failure','skipped',None):
                    bad=dict(good)
                    if outcome is None:del bad[failed]
                    else:bad[failed]={'outcome':outcome}
                    with patch.dict(os.environ,{'GROUP':group,'STEP_RESULTS':json.dumps(bad)}):
                        with self.assertRaises(SystemExit):exec(code,{})

    def test_game_soaks_and_full_controller_suite_are_retained(self):
        section=job('dolphin-smoke')
        self.assertIn('--seconds 65 --minimum-seconds 65',section)
        self.assertIn('--seconds 70 --minimum-seconds 70',section)
        self.assertIn('--verify-rejection',section)
        self.assertIn('dolphin-controller/',section)

    def test_executor_ownership_acceptance_uses_automatic_runtime(self):
        section=job('managed')
        block=next(b for b in step_blocks(section) if b.startswith('      - name: Validate controller and memory-card connection transitions\n'))
        self.assertIn('DND_ASYNC_MANUAL: "0"',block)

    def test_generated_c_inputs_are_available_within_each_managed_group(self):
        # A named test can survive sharding while losing a hidden /tmp input.
        # Track AOT outputs in command order for each isolated runner.
        for group in MANIFEST['managed']:
            generated=set()
            for block in step_blocks(job('managed')):
                condition=re.search(r"if: matrix.group == '([^']+)'",block)
                if condition and condition.group(1)!=group:continue
                for line in block.splitlines():
                    command=line.strip()
                    if command.startswith('dotnet ') and re.search(r' --(?:value-)?aot ',command):
                        outputs=re.findall(r'/tmp/[\w/-]+\.c\b',command)
                        generated.update(outputs)
                    if command.startswith('cc '):
                        for source in re.findall(r'/tmp/[\w/-]+\.c\b',command):
                            self.assertIn(source,generated,f'{group}: {source} has no earlier AOT producer')

    def test_only_managed_debugger_scenarios_build_host_dotnet_tools(self):
        for block in step_blocks(job('dolphin-smoke')):
            if block.startswith(('      - uses: actions/setup-dotnet@','      - name: Build .NET source debugger','      - name: Build attach-only DAP')):
                self.assertIn("if: matrix.group == 'debugger'",block)


if __name__=='__main__':unittest.main()
