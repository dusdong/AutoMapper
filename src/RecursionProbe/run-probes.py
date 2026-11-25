import argparse
import json
from pathlib import Path
import subprocess


DEFAULT_SCENARIOS = ("reference-graph", "constructor-depth", "runtime-dispatch")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--program", type=Path, required=True)
    parser.add_argument("--dll-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--scenario", choices=(*DEFAULT_SCENARIOS, "application-exception"))
    args = parser.parse_args()
    args.output.mkdir()
    output = args.output.resolve()
    report = []
    scenarios = (args.scenario,) if args.scenario else DEFAULT_SCENARIOS
    for scenario in scenarios:
        command = [str(args.dotnet.resolve()), str(args.program.resolve()), scenario]
        if scenario == "application-exception":
            command.append(args.dll_sha256)
        entry = {"scenario": scenario, "command": command, "cwd": str(output), "timeoutSeconds": 30}
        with (output / f"{scenario}.stdout").open("wb") as stdout, (output / f"{scenario}.stderr").open("wb") as stderr:
            try:
                result = subprocess.run(command, cwd=output, stdout=stdout, stderr=stderr, timeout=30, check=False)
                entry.update(exitCode=result.returncode, timedOut=False)
            except subprocess.TimeoutExpired:
                entry.update(exitCode=None, timedOut=True)
        if entry["exitCode"] == 0:
            try:
                payload = json.loads((output / f"{scenario}.stdout").read_text(encoding="utf-8"))
                entry["actual"] = payload
                entry["passed"] = payload["scenario"] == scenario and payload["sha256"] == args.dll_sha256
            except (ValueError, KeyError, TypeError) as error:
                entry.update(passed=False, outputError=str(error))
        else:
            entry["passed"] = False
        report.append(entry)
    (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for entry in report:
        print(entry["scenario"], "通过" if entry["passed"] else "失败")
    return 0 if all(entry["passed"] for entry in report) else 1


if __name__ == "__main__":
    raise SystemExit(main())
