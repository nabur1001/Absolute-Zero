"""Select the strongest completed evidence for each matrix case, preserving failures."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("root", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
cases = {f"win-{n}-w{w}": ["Batch1"] for n in (3, 4) for w in range(n)}
cases.update({
    "ghost-4-w0": ["Batch1"], "ghost-4-w2": ["Batch1"],
    "special-3-w0": ["Boundary2"], "duel-2-w0": ["Batch1"],
    "disconnect-prep-4-w0": ["Final", "Boundary2", "Batch1"],
    "disconnect-attack-4-w0": ["Boundary2", "Batch1"],
    "disconnect-terminal-4-w0": ["Batch1"], "host-exit-4-w0": ["Batch1"],
    "init-failure-4-w0": ["Final", "Batch1"],
    "inventory-4-w0": ["Inventory"], "inventory-2-w0": ["Inventory"],
    "rpc-guards-4-w0": ["Boundary2"], "services-4-w0": ["Boundary2"],
    "multi-round-4-w0": ["Final"],
})
rows = []
for name, batches in cases.items():
    path = next((args.root / batch / name / "report.json" for batch in batches
                 if (args.root / batch / name / "report.json").exists()), None)
    report = json.loads(path.read_text()) if path else None
    status = "pending" if report is None else ("pass" if report["passed"] else "fail")
    if name == "host-exit-4-w0" and report is not None:
        status = "inconclusive-local-session"
    rows.append(dict(case=name, status=status, evidence=str(path) if path else None,
                     runner_error=report.get("runner_error") if report else None,
                     checkpoints_equal=report.get("checkpoints_equal") if report else None,
                     results=report.get("results") if report else None))
summary = {status: sum(r["status"] == status for r in rows)
           for status in ("pass", "fail", "inconclusive-local-session", "pending")}
args.output.write_text(json.dumps(dict(summary=summary, cases=rows), indent=2), encoding="utf-8")
print(json.dumps(summary))
