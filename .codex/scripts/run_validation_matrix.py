"""Bounded local multi-process cases. Records failures without stopping later cases."""
import argparse
import concurrent.futures
import json
from pathlib import Path
import re
import subprocess
import time
import uuid
from matrix_udp_proxy import MatrixUdpProxy

CASES = [("win", 3, w) for w in range(3)] + [("win", 4, w) for w in range(4)] + [
    ("ghost", 4, 0), ("ghost", 4, 2), ("ghost", 4, 1), ("ghost", 4, 3), ("special", 3, 0), ("duel", 2, 0),
    ("disconnect-prep", 4, 0), ("disconnect-attack", 4, 0),
    ("disconnect-terminal", 4, 0), ("host-exit", 4, 0), ("init-failure", 4, 0),
    ("inventory", 4, 0), ("inventory", 2, 0), ("rpc-guards", 4, 0), ("services", 4, 0), ("multi-round", 4, 0), ("idle", 4, 0),
    ("minigame-target", 4, 0), ("minigame-actor", 4, 0), ("minigame-failure", 4, 0), ("disconnect-idle", 4, 0),
    ("joint", 4, 0), ("delayed", 4, 0), ("duel-repeat", 2, 0)]


def run_case(executable, root, case, port, proxy=False, delay_ms=0, loss=0, hold_uplink=0, drop_seat=3):
    kind, count, winner = case
    folder = root / f"{kind}-{count}-w{winner}"
    folder.mkdir(parents=True, exist_ok=False)
    token = uuid.uuid4().hex
    joined = count - 1 if kind == "init-failure" else count
    names = ["host"] + [f"client{i}" for i in range(1, joined)]
    processes = {}
    killed = None
    failure = None
    proxies = {}
    held = False
    try:
        for name in names:
            endpoint = port
            if proxy and name != "host":
                proxies[name] = MatrixUdpProxy(port, delay_ms, loss, seed=names.index(name))
                endpoint = proxies[name].port
            info = subprocess.STARTUPINFO()
            info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            info.wShowWindow = 0
            processes[name] = subprocess.Popen([str(executable), "-batchmode", "-force-d3d11",
                "--az-matrix", kind, "--az-role", "host" if name == "host" else "client",
                "--az-count", str(count), "--az-winner", str(winner), "--az-run", token,
                "--az-drop-seat", str(drop_seat),
                "--az-network-stress", "1" if proxy and (loss > 0 or delay_ms > 0) else "0",
                "--az-port", str(endpoint), "-logFile", str(folder / f"{name}.log")], startupinfo=info)
            if name == "host":
                deadline = time.monotonic() + 35
                while time.monotonic() < deadline:
                    log = folder / "host.log"
                    if log.exists() and "[MATRIX] LISTENING" in log.read_text(errors="replace"):
                        break
                    if processes[name].poll() is not None:
                        raise RuntimeError("Host exited during startup")
                    time.sleep(.5)
                else:
                    raise TimeoutError("Host startup timeout")
        deadline = time.monotonic() + 320
        while any(p.poll() is None for p in processes.values()) and time.monotonic() < deadline:
            host = (folder / "host.log").read_text(errors="replace")
            if proxies and hold_uplink > 0 and not held and "phase=AttackPhase" in host:
                held = True
                for item in proxies.values():
                    item.uplink_hold_until = time.monotonic() + hold_uplink
                (folder / "uplink-hold.json").write_text(json.dumps(dict(seconds=hold_uplink, trigger="first AttackPhase")))
            for name, process in processes.items():
                if name == killed:
                    continue
                log_path = folder / f"{name}.log"
                text = log_path.read_text(errors="replace") if log_path.exists() else ""
                if "[MATRIX] FAIL" in text or (process.poll() not in (None, 0)):
                    failure = failure or f"{name} failed before scenario completion"
                if kind != "host-exit" and "[MATRIX] NET_STOP" in text and "[MATRIX] PASS" not in text:
                    failure = failure or f"{name} network stopped before scenario completion"
            if failure:
                break
            if killed is None:
                trigger = ((kind in ("disconnect-prep", "disconnect-idle", "host-exit") and "phase=PrepPhase" in host)
                           or (kind == "disconnect-attack" and "phase=AttackPhase" in host)
                           or (kind == "disconnect-terminal" and re.search(r"terminal=[1-9]\d*/1/False", host)))
                if trigger:
                    killed = "host" if kind == "host-exit" else None
                    if killed is None:
                        for candidate in names[1:]:
                            log = folder / f"{candidate}.log"
                            if log.exists() and re.search(r"\[MATRIX\] STATE local=" + str(drop_seat) + " ", log.read_text(errors="replace")):
                                killed = candidate
                                break
                    if killed is None:
                        time.sleep(.5)
                        continue
                    processes[killed].terminate()
                    (folder / "fault.json").write_text(json.dumps(dict(process=killed, trigger=kind)))
            time.sleep(.5)
    except Exception as error:
        failure = str(error)
    finally:
        for process in processes.values():
            if process.poll() is None:
                process.terminate()
            process.wait(timeout=20)
        for item in proxies.values():
            item.close()
    results = {}
    for name, process in processes.items():
        path = folder / f"{name}.log"
        text = path.read_text(errors="replace") if path.exists() else ""
        errors = re.findall(r"^.*(?:Exception:|\[MATRIX\] FAIL|Visual binding failed|Identity binding timed out).*$", text, re.M)
        checks = re.findall(r"\[MATRIX\] CHECK seq=(\d+) (.+)", text)
        results[name] = dict(exit=process.returncode, passes=re.findall(r"\[MATRIX\] PASS (.+)", text),
                             checks=checks, errors=errors, intentionally_terminated=name == killed,
                             visible=re.findall(r"\[MatchResult\] Visible seq=(\d+) local=(\d+) winners=(\d+)", text))
    active = [v for v in results.values() if not v["intentionally_terminated"]]
    passed = failure is None and len(results) == joined and all(v["exit"] == 0 and len(v["passes"]) == 1 and not v["errors"] for v in active)
    # State comparisons are made only at completed presentations. Disconnect can change
    # the visible roster between clients' settlement callbacks, so report that separately.
    synchronized = bool(active) and bool(active[0]["checks"]) and all(v["checks"] == active[0]["checks"] for v in active)
    if kind in ("win", "ghost", "special", "joint", "delayed"):
        passed = passed and synchronized
    if kind == "special":
        host_text = (folder / "host.log").read_text(errors="replace")
        passed = passed and all(re.search(r"\[COMBAT\] Actions:.*main=" + re.escape(item), host_text)
                                for item in ("Cat", "Hug T-shirt", "Ice Cream"))
    if kind in ("win", "ghost", "disconnect-prep", "disconnect-attack", "disconnect-terminal"):
        passed = passed and all(len(v["visible"]) == 1 and int(v["visible"][0][2]) == 1 << winner for v in active)
    if kind in ("joint", "delayed"):
        expected_mask = 3 if kind == "joint" else 2
        passed = passed and all(len(v["visible"]) == 1 and int(v["visible"][0][2]) == expected_mask for v in active)
    if hold_uplink > 0:
        host_text = (folder / "host.log").read_text(errors="replace")
        passed = passed and held and "[PresentationBarrier] Timeout" in host_text and "ACK ignored" in host_text
    report = dict(case=case, passed=passed, checkpoints_equal=synchronized, runner_error=failure, results=results,
                  proxy=dict(enabled=proxy, one_way_delay_ms=delay_ms, loss=loss, hold_uplink_seconds=hold_uplink,
                             stats={k: v.stats for k, v in proxies.items()}), drop_seat=drop_seat)
    (folder / "report.json").write_text(json.dumps(report, indent=2))
    print(f"{folder.name}: {'PASS' if passed else 'FAIL'}", flush=True)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("executable", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--filter", default="")
    parser.add_argument("--workers", type=int, default=2)
    parser.add_argument("--port-base", type=int, default=17900)
    parser.add_argument("--proxy", action="store_true")
    parser.add_argument("--delay-ms", type=float, default=0)
    parser.add_argument("--loss", type=float, default=0)
    parser.add_argument("--hold-uplink", type=float, default=0)
    parser.add_argument("--drop-seat", type=int, choices=[1, 2, 3], default=3)
    parser.add_argument("--limit", type=int, default=0)
    args = parser.parse_args()
    if not 0 <= args.loss <= 1 or args.delay_ms < 0 or args.hold_uplink < 0:
        parser.error("Delay/hold must be nonnegative and loss must be in [0,1]")
    if not args.proxy and (args.delay_ms or args.loss or args.hold_uplink):
        parser.error("Network impairment requires --proxy")
    args.output.mkdir(parents=True, exist_ok=False)
    cases = [case for case in CASES if not args.filter or case[0] in args.filter.split(",")]
    if args.limit > 0:
        cases = cases[:args.limit]
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = [pool.submit(run_case, args.executable.resolve(), args.output.resolve(), case, args.port_base + i, args.proxy, args.delay_ms, args.loss, args.hold_uplink, args.drop_seat) for i, case in enumerate(cases)]
        reports = [future.result() for future in futures]
    (args.output / "matrix.json").write_text(json.dumps(reports, indent=2))
    raise SystemExit(0 if all(r["passed"] for r in reports) else 1)
