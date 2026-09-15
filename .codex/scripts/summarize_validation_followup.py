"""Select current follow-up evidence without hiding superseded or failed attempts."""
import argparse
import json
from pathlib import Path


def summarize(root):
    selected = {}

    def add(folder, names, prefix=""):
        for name in names:
            path = root / folder / name / "report.json"
            key = prefix + name
            if not path.exists():
                selected[key] = dict(status="pending", evidence=str(path))
                continue
            report = json.loads(path.read_text())
            selected[key] = dict(status="pass" if report["passed"] else "fail", evidence=str(path), report=report)

    wins = [f"win-{n}-w{w}" for n in (3, 4) for w in range(n)]
    add("FollowupRegression", wins + ["special-3-w0", "inventory-2-w0", "inventory-4-w0",
                                     "rpc-guards-4-w0", "services-4-w0", "multi-round-4-w0"])
    add("FixedMinigame", [f"ghost-4-w{w}" for w in range(4)] +
        [f"minigame-{kind}-4-w0" for kind in ("target", "actor", "failure")])
    add("FixedDisconnect", [f"disconnect-{kind}-4-w0" for kind in ("prep", "attack", "terminal", "idle")])
    add("FixedTerminal", ["duel-2-w0", "init-failure-4-w0", "joint-4-w0"])
    add("FixedImpairedFinal", wins, prefix="impaired/")
    add("FixedImpairedFinal", ["delayed-4-w0"])
    for seat in (1, 2):
        add(f"FixedSeat{seat}Final", ["disconnect-prep-4-w0", "disconnect-attack-4-w0"], prefix=f"drop-seat-{seat}/")
    add("FixedLateAck", ["win-3-w0"], prefix="late-ack/")
    add("FollowupDiagnostic", ["idle-4-w0"])
    add("FixedReuse", ["duel-repeat-2-w0"])

    excluded = [
        dict(evidence=str(root / "FollowupDiagnostic/disconnect-prep-4-w0/report.json"),
             reason="Pre-hotfix reproduction of KI-007; retained as failing baseline."),
        dict(evidence=str(root / "FollowupRegression/minigame-target-4-w0"),
             reason="Harness actor exited before observer completion; superseded runner stopped and fixture lifetime corrected."),
        dict(evidence=str(root / "FixedTerminal/delayed-4-w0/report.json"),
             reason="Test expected FIFO, but existing scheduler is reverse pending order; expected first processed winner corrected without changing gameplay."),
        dict(evidence=str(root / "FollowupImpaired/win-4-w3/report.json"),
             reason="Stress fixture did not guarantee seeded winner survived/natural target death; superseded by isolated delivery fixture with defending peers and explicit target setup."),
        dict(evidence=str(root / "FixedImpaired/win-3-w0"),
             reason="First isolated fixture reset target to six degrees each turn, preventing the three-damage Fan from finishing; corrected test setup to one degree."),
        dict(evidence=str(root / "FixedSeat1/disconnect-attack-4-w0/report.json"),
             reason="Connections and 19 combat checkpoints remained healthy, but the seeded winner was killed by other actors before reaching five; finishing fixture now seeds a one-degree non-fanning victim after disconnect recovery."),
        dict(evidence=str(root / "FixedSeat2"),
             reason="Superseded old finishing-fixture runner; both phase cases re-run under FixedSeat2Final."),
    ]
    return dict(summary={status: sum(v["status"] == status for v in selected.values())
                         for status in ("pass", "fail", "pending")},
                cases=selected, excluded_attempts=excluded,
                limits=["Separate Windows processes on one PC; not real Lobby/Relay.",
                        "FollowupRegression predates the transport hotfix; Fixed* players include it.",
                        "Stress wins use defending peers and an explicit one-degree non-fanning victim fixture.",
                        "Host-exit production lobby return remains previously inconclusive; not reclassified as pass.",
                        "No rendered camera/HUD/audio, full input, reconnect, or long-duration leak certification."])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    result = summarize(args.root)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result["summary"]))
