"""Run a visible four-player development match and build an evidence gallery."""
import argparse
import ctypes
from ctypes import wintypes
import html
import json
from pathlib import Path
import re
import subprocess
import time
import uuid


def windows_for_pid(pid):
    found = []
    enum_proc = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)

    def callback(hwnd, _):
        process_id = wintypes.DWORD()
        ctypes.windll.user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
        if process_id.value == pid and ctypes.windll.user32.IsWindowVisible(hwnd):
            found.append(hwnd)
        return True

    ctypes.windll.user32.EnumWindows(enum_proc(callback), 0)
    return found


def place_window(process, x, y, width=960, height=540, timeout=25):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline and process.poll() is None:
        windows = windows_for_pid(process.pid)
        if windows:
            ctypes.windll.user32.SetWindowPos(windows[0], 0, x, y, width, height, 0x0040)
            return True
        time.sleep(0.25)
    return False


def read(path):
    return path.read_text(errors="replace") if path.exists() else ""


def build_report(output, metadata, processes, placement, runner_error):
    roles = ["host", "client1", "client2", "client3"]
    results = {}
    error_pattern = re.compile(
        r"^.*(?:\[VISUAL\] FAIL|Exception:|NullReferenceException|IndexOutOfRangeException|"
        r"Identity binding timed out|Visual binding failed).*$", re.MULTILINE)
    seat_by_role = {"host": "host"}
    for role in roles[1:]:
        match = re.search(r"\[VISUAL\] ASSIGNED local=(\d+)", read(output / f"{role}.log"))
        seat_by_role[role] = f"seat{match.group(1)}" if match else role
    warning_patterns = {
        "missing_animator": r"_animator is NULL",
        "missing_crown_glyph": r"Unicode value \\u265B was not found",
        "loading_overlay_unassigned": r"SceneLoadSyncManager\] overlayRoot is not assigned",
        "duplicate_minigame_result": r"Mini-game result rejected: no pending game",
        "presentation_timeout": r"PresentationBarrier\] Timeout|Result presentation timeout",
    }
    for role in roles:
        log_path = output / f"{role}.log"
        text = read(log_path)
        process = processes.get(role)
        checkpoints = re.findall(r"\[VISUAL\] CHECKPOINT (.+)", text)
        results[role] = {
            "exit_code": process.returncode if process else None,
            "pass": re.findall(r"\[VISUAL\] PASS (.+)", text),
            "assignments": re.findall(r"\[VISUAL\] ASSIGNED (.+)", text),
            "plans": re.findall(r"\[VISUAL\] PLAN (.+)", text),
            "checkpoints": checkpoints,
            "errors": error_pattern.findall(text),
            "findings": {name: len(re.findall(pattern, text)) for name, pattern in warning_patterns.items()},
            "screenshots": [path.name for path in sorted(output.glob(f"{seat_by_role[role]}.*.png"))],
            "window_placed": placement.get(role, False),
            "authenticated_player_id": (re.search(r"\[ServicesGateway\] Initialized, PlayerId: (\S+)", text).group(1)
                                            if re.search(r"\[ServicesGateway\] Initialized, PlayerId: (\S+)", text) else None),
            "relay_allocation": "[RelayGateway] Allocated" in text,
            "relay_join": "[RelayGateway] Joined relay" in text,
            "relay_connected": ("[VISUAL] RELAY_HOST_LISTENING" in text
                                or "[VISUAL] RELAY_CLIENT_CONNECTED" in text),
        }
    if metadata.get("ghost_showcase"):
        skill_signatures = [re.findall(r"\[VISUAL\] GHOST_VFX_START (.+)", read(output / f"{role}.log"))
                            for role in roles]
        synchronized = (len(skill_signatures[0]) == 2
                        and all(rows == skill_signatures[0] for rows in skill_signatures[1:]))
        capture_complete = all(len(result["screenshots"]) >= 7 for result in results.values())
    else:
        checkpoint_signatures = []
        for result in results.values():
            checkpoint_signatures.append([re.sub(r" local=\d+", "", row) for row in result["checkpoints"]])
        synchronized = (bool(checkpoint_signatures[0])
                        and all(rows == checkpoint_signatures[0] for rows in checkpoint_signatures[1:]))
        capture_complete = all(len(result["screenshots"]) >= metadata["turns"] * 2 + 1
                               for result in results.values())
    relay_verified = True
    if metadata.get("transport") == "unity-relay":
        player_ids = [result["authenticated_player_id"] for result in results.values()]
        relay_verified = (all(player_ids) and len(set(player_ids)) == 4
                          and results["host"]["relay_allocation"]
                          and all(results[role]["relay_join"] for role in roles[1:])
                          and all(result["relay_connected"] for result in results.values()))
    passed = (runner_error is None and len(results) == 4 and synchronized and capture_complete and relay_verified
              and all(result["exit_code"] == 0 and len(result["pass"]) == 1 and not result["errors"]
                      for result in results.values()))
    report = {"passed": passed, "runner_error": runner_error, "checkpoints_synchronized": synchronized,
              "capture_complete": capture_complete, "relay_verified": relay_verified,
              "metadata": metadata, "players": results}
    (output / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    return report


def write_gallery(output, report):
    rows = []
    for role, result in report["players"].items():
        images = []
        for name in result["screenshots"]:
            images.append(f'<figure><img src="{html.escape(name)}"><figcaption>{html.escape(name)}</figcaption></figure>')
        plans = "\n".join(result["plans"]) or "No recorded plan"
        errors = "\n".join(result["errors"]) or "None"
        findings = "\n".join(f"{name}: {count}" for name, count in result["findings"].items() if count) or "None"
        rows.append(f"<section><h2>{html.escape(role)}</h2><pre>{html.escape(plans)}</pre>"
                    f"<h3>Detected errors</h3><pre>{html.escape(errors)}</pre><h3>Findings</h3><pre>{html.escape(findings)}</pre>"
                    f"<div class='shots'>{''.join(images)}</div></section>")
    page = f"""<!doctype html><html><head><meta charset='utf-8'><title>Absolute Zero 4P Visual Debug</title>
<style>body{{font-family:Segoe UI,sans-serif;background:#10151d;color:#e7edf5;margin:24px}} .status{{padding:14px;background:#1c2633;border-radius:8px}}
section{{margin-top:28px;border-top:1px solid #415064}} pre{{white-space:pre-wrap;background:#18212c;padding:12px}} .shots{{display:grid;grid-template-columns:1fr 1fr;gap:12px}}
figure{{margin:0;background:#18212c;padding:8px}} img{{width:100%;height:auto;display:block}} figcaption{{font-size:12px;margin-top:6px;word-break:break-all}}</style></head>
<body><h1>Absolute Zero — Four-player visual debug</h1><div class='status'>Result: <b>{'PASS' if report['passed'] else 'FAIL'}</b><br>
Seed: {report['metadata']['seed']} | Turn limit: {report['metadata']['turns']} | Run: {html.escape(report['metadata']['run'])}</div>{''.join(rows)}</body></html>"""
    (output / "gallery.html").write_text(page, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("executable", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--seed", type=int, default=29031)
    parser.add_argument("--turns", type=int, default=4)
    parser.add_argument("--port", type=int, default=17859)
    parser.add_argument("--timeout", type=int, default=320)
    parser.add_argument("--ghost-showcase", action="store_true")
    parser.add_argument("--relay", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    executable = args.executable.resolve()
    if not executable.exists():
        parser.error(f"Executable does not exist: {executable}")

    token = uuid.uuid4().hex
    metadata = {"run": token, "seed": args.seed, "turns": args.turns, "port": args.port,
                "transport": "unity-relay" if args.relay else "local-utp",
                "ghost_showcase": args.ghost_showcase,
                "executable": str(executable)}
    (args.output / "scenario.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    processes = {}
    placement = {}
    runner_error = None
    positions = {"host": (0, 0), "client1": (960, 0), "client2": (0, 540), "client3": (960, 540)}
    try:
        for index, role in enumerate(positions):
            profile_suffix = token[:8]
            services_profile = f"azr-{'h' if role == 'host' else role[-1]}-{profile_suffix}"
            command = [str(executable), "-screen-fullscreen", "0", "-force-d3d11",
                       "-screen-width", "960", "-screen-height", "540",
                       "--az-visual-flow", "1", "--az-role", "host" if role == "host" else "client",
                       "--az-run", token, "--az-seed", str(args.seed), "--az-turns", str(args.turns),
                       "--az-port", str(args.port), "-logFile", str((args.output / f"{role}.log").resolve())]
            if args.relay:
                command.extend(["--az-relay-flow", "1", "--az-services-profile", services_profile,
                                "--az-coordination-file", str((args.output / "lobby-code.tmpdata").resolve())])
            if args.ghost_showcase:
                command.extend(["--az-ghost-showcase", "1"])
            processes[role] = subprocess.Popen(command)
            placement[role] = place_window(processes[role], *positions[role])
            if role == "host":
                deadline = time.monotonic() + 35
                while time.monotonic() < deadline:
                    ready_marker = "[VISUAL] RELAY_LOBBY_READY" if args.relay else "[VISUAL] HOST_LISTENING"
                    if ready_marker in read(args.output / "host.log"):
                        break
                    if processes[role].poll() is not None:
                        raise RuntimeError("Host exited before listening")
                    time.sleep(0.5)
                else:
                    raise TimeoutError("Host did not listen within 35 seconds")
            time.sleep(0.4 if index else 0.8)

        deadline = time.monotonic() + args.timeout
        while time.monotonic() < deadline and any(process.poll() is None for process in processes.values()):
            for role, process in processes.items():
                log = read(args.output / f"{role}.log")
                if "[VISUAL] FAIL" in log or (process.poll() not in (None, 0)):
                    runner_error = f"{role} failed during the scenario"
                    break
            if runner_error:
                break
            time.sleep(1)
        if any(process.poll() is None for process in processes.values()) and not runner_error:
            runner_error = f"Scenario exceeded {args.timeout} seconds"
    except Exception as error:
        runner_error = str(error)
    finally:
        for process in processes.values():
            if process.poll() is None:
                process.terminate()
        for process in processes.values():
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        coordination_file = args.output / "lobby-code.tmpdata"
        if coordination_file.exists():
            coordination_file.unlink()

    report = build_report(args.output, metadata, processes, placement, runner_error)
    write_gallery(args.output, report)
    print(json.dumps(report, indent=2, ensure_ascii=False))
    raise SystemExit(0 if report["passed"] else 1)


if __name__ == "__main__":
    main()
