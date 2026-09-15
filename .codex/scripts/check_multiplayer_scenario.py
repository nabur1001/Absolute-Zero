"""Compare completed local MultiScenarioProbe logs; never infer success from exit alone."""
import argparse
import json
from pathlib import Path
import re


def nonblank_capture(path):
    if not path.exists():
        return False
    try:
        from PIL import Image
        with Image.open(path) as capture:
            return any(low != high for low, high in capture.convert("RGB").getextrema())
    except ImportError:
        return None  # Pixel validation unavailable; do not install a dependency implicitly.


def check(folder):
    results = []
    for name in ("host", "client1", "client2", "client3"):
        path = folder / f"{name}.log"
        text = path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""
        snapshots = re.findall(r"\[SCENARIO\] SNAP local=(\d+) (.+)", text)
        settled = re.findall(r"\[SCENARIO\] SETTLED seq=(\d+)", text)
        checkpoints = re.findall(r"\[SCENARIO\] CHECKPOINT seq=(\d+) (.+)", text)
        visible = re.findall(r"\[MatchResult\] Visible seq=(\d+) local=(\d+) winners=(\d+)", text)
        finished = re.findall(r"\[SCENARIO\] FINISHED local=(\d+) winnerMask=(\d+)", text)
        errors = re.findall(r"^.*(?:Exception:|\[SCENARIO\] TIMEOUT|Result presentation timeout|Bootstrap retry changed|\[MatchInitialization\]|Visual binding failed|Identity binding timed out).*$", text, re.M)
        # Compare the first complete released snapshot, before process teardown removes seats.
        terminal = [s for _, s in snapshots if re.search(r"terminal=[1-9]\d*/1/True", s)
                    and len(s.split(" seats=", 1)[-1].split(";")) == 4]
        results.append(dict(process=name, seat=snapshots[-1][0] if snapshots else None,
                            settled=settled, checkpoints=checkpoints, visible=visible, finished=finished,
                            terminal=terminal[0] if terminal else None, errors=errors,
                            screenshot=(folder / f"{name}.png").exists(),
                            nonblank_capture=nonblank_capture(folder / f"{name}.png")))
    states = [r["terminal"] for r in results]
    passed = ({r["seat"] for r in results} == {"0", "1", "2", "3"} and None not in states
              and len(set(states)) == 1
              and all(r["checkpoints"] == results[0]["checkpoints"] and r["checkpoints"] for r in results)
              and all(not r["errors"] and len(r["finished"]) == 1
                      and len(r["visible"]) == 1 and r["screenshot"]
                      and r["finished"][0] == (r["seat"], "1")
                      and r["visible"][0][1:] == (r["seat"], "1")
                      and len(r["settled"]) == len(set(r["settled"]))
                      and r["visible"][0][0] in r["settled"] for r in results))
    codes_path = folder / "exit_codes.json"
    codes = json.loads(codes_path.read_text()) if codes_path.exists() else {}
    passed = passed and set(codes) == {"host", "client1", "client2", "client3"} and all(v == 0 for v in codes.values())
    return dict(protocol_passed=passed, exit_codes=codes,
                visual_capture_valid=all(r["nonblank_capture"] is True for r in results),
                processes=results)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    report = check(args.folder)
    output = json.dumps(report, indent=2, ensure_ascii=False)
    (args.folder / "comparison.json").write_text(output, encoding="utf-8")
    print(output)
    raise SystemExit(0 if report["protocol_passed"] else 1)
