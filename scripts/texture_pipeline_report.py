#!/usr/bin/env python3
"""Collect validated texture runs without mixing their samples or source workloads."""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def collect(runs):
    records=[]
    for spec in runs:
        label,filename=spec.split('=',1)
        path=Path(filename)
        proof=json.loads(path.read_text())
        if not proof.get('benchmark') or len(proof.get('completedCases',[]))!=9:
            raise ValueError('Require a validated nine-case result: '+filename)
        for state in proof['completedCases']:
            row=dict(run=label,case=state['case'],group=state['group'],budgetKiB=state['budgetKiB'],
                     mode=state.get('optimizationMode',0),meanMs=state['measuredMs']/state['timingSamples'],
                     p95Ms=state['frameP95TenthsMs']/10,p99Ms=state['frameP99TenthsMs']/10,
                     movingCoverage=state['motionCoverageBasisPoints']/100,
                     staticCoverage=state['staticCoverageBasisPoints']/100,promotions=state['promotions'],
                     scratchKiB=state.get('scratchKiB',84),source=str(path))
            for name in ('preparationMeanTenthsMs','preparationMaxTenthsMs','queueMeanTenthsMs',
                         'readyMeanTenthsMs','meanUploadTenthsMs','meanEndTenthsMs','meanViTenthsMs',
                         'meanOtherTenthsMs','beginCopies','pendingPeak','readyPeak','maxPublications','reused','allocations'):
                row[name]=state.get(name)
            records.append(row)
    return records


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run',action='append',required=True,help='label=validated-results.json')
    parser.add_argument('--dol',action='append',default=[],help='label=Game.dol')
    parser.add_argument('--output',type=Path,required=True,help='Output filename prefix')
    args=parser.parse_args()
    rows=collect(args.run)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    manifest={}
    for spec in args.dol:
        label,filename=spec.split('=',1);path=Path(filename)
        manifest[label]=dict(path=str(path),bytes=path.stat().st_size,sha256=hashlib.sha256(path.read_bytes()).hexdigest())
    args.output.with_suffix('.json').write_text(json.dumps(dict(rows=rows,dols=manifest),indent=2)+'\n')
    with args.output.with_suffix('.csv').open('w',newline='') as stream:
        writer=csv.DictWriter(stream,fieldnames=list(rows[0]),lineterminator="\n");writer.writeheader();writer.writerows(rows)
    lines=['# Texture pipeline comparisons','','Guest elapsed intervals in Dolphin. Preparation is elapsed job time, not exclusive CPU time.',
           'The source and SDK controls differ by labeled mode; storage is not part of this workload.','',
           '| Run | Case | Group | KiB | Mode | Mean ms | P99 ms | Moving detail |',
           '| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |']
    for row in rows:
        lines.append('| {run} | {case} | {group} | {budgetKiB} | {mode} | {meanMs:.2f} | {p99Ms:.1f} | {movingCoverage:.2f}% |'.format(**row))
    args.output.with_suffix('.md').write_text('\n'.join(lines)+'\n')


if __name__=='__main__':main()
