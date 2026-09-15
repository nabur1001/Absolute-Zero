"""Run four isolated development players with bounded lifetime and exact exit codes."""
import argparse
import json
from pathlib import Path
import subprocess
import time
import uuid
from check_multiplayer_scenario import check

parser = argparse.ArgumentParser()
parser.add_argument("executable", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=False)
processes = []
token = uuid.uuid4().hex
try:
    for name in ("host", "client1", "client2", "client3"):
        info = subprocess.STARTUPINFO()
        info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        info.wShowWindow = 0
        process = subprocess.Popen([str(args.executable.resolve()), "-screen-fullscreen", "0", "-force-d3d11",
                                    "-screen-width", "960", "-screen-height", "540",
                                    "--az-scenario", "host" if name == "host" else "client",
                                    "--az-run", token, "-logFile", str((args.output / f"{name}.log").resolve())],
                                   startupinfo=info)
        processes.append((name, process))
        if name == "host":
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                log = args.output / "host.log"
                if log.exists() and "[SCENARIO] HOST_LISTENING" in log.read_text(errors="replace"):
                    break
                if process.poll() is not None:
                    raise RuntimeError("Host exited before listening")
                time.sleep(0.5)
            else:
                raise TimeoutError("Host did not listen within 30 seconds")
    deadline = time.monotonic() + 270
    while any(p.poll() is None for _, p in processes) and time.monotonic() < deadline:
        time.sleep(1)
finally:
    for _, process in processes:
        if process.poll() is None:
            process.terminate()
        process.wait(timeout=15)
    codes = {name: p.returncode for name, p in processes}
    (args.output / "exit_codes.json").write_text(json.dumps(codes, indent=2))
report = check(args.output)
report["exit_codes"] = codes
report["protocol_passed"] = report["protocol_passed"] and len(codes) == 4 and all(code == 0 for code in codes.values())
(args.output / "comparison.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
raise SystemExit(0 if report["protocol_passed"] else 1)
