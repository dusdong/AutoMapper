#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import zipfile

SOURCE = "https://api.nuget.org/v3/index.json"
MANIFEST_SHA256 = "c3a905b6a6373a59236214f94fce953abb530fc61c80a7fe94ff99f028f534b0"
PACKAGE = "Du.AutoMapper"
VERSION = "14.0.0"
ASSET = "lib/net8.0/AutoMapper.dll"
TFM = "net10.0"
PROJECT = "CollectionOrderProbe"
CASES = {
    "warm-order-first": (["lines", "order", "invoice"], ["order", "invoice"]),
    "warm-invoice-first": (["lines", "order", "invoice"], ["invoice", "order"]),
    "cold-order-first": (["lines", "order", "invoice"], []),
    "cold-invoice-first": (["lines", "invoice", "order"], []),
}


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def report_space(path):
    available = shutil.disk_usage(path).free
    print(f"{path}: {available} bytes available", flush=True)
    require(os.access(path, os.W_OK), f"目录不可写: {path}")


def run(work, env, log, args):
    with (work / "commands.log").open("a", encoding="utf-8") as commands:
        print(f"cwd={work} output={log}\n{shlex.join(args)}", file=commands, flush=True)
        with (work / log).open("wb") as output, (work / f"{log}.stderr").open("wb") as errors:
            try:
                result = subprocess.run(args, cwd=work, env=env, stdout=output,
                                        stderr=errors, timeout=120, check=False)
            except subprocess.TimeoutExpired:
                print("timeout=120s", file=commands)
                raise
        print(f"exit={result.returncode}", file=commands)
    require(result.returncode == 0, f"执行失败（{result.returncode}），见 {work / log} 及 stderr")


def get_expected_package(work, public_manifest, verified_downloads):
    manifest = work / "R4-PACKAGE-CHECKSUMS.json"
    downloads = work / "verified-downloads.json"
    shutil.copyfile(public_manifest, manifest)
    shutil.copyfile(verified_downloads, downloads)
    public = read_json(manifest)
    verified = read_json(downloads)
    require(sha256(manifest) == MANIFEST_SHA256, "R4 公开清单摘要不符")
    require(sha256(downloads) == public["verifiedDownloadsSha256"], "R4 验签下载记录摘要不符")
    require(public["nugetSource"] == verified["nugetSource"] == SOURCE, "R4 来源不符")
    entry = public["packages"][PACKAGE]
    verified_entry = verified["packages"][PACKAGE]
    expected = entry["mainPackage"]["anonymousDownloadSha256"]
    require(entry["version"] == verified_entry["version"] == VERSION
            and expected == verified_entry["downloadSha256"], "R4 公开包下载登记不符")
    return expected


def verify_restored_package(cache, assets, expected):
    restore = assets["project"]["restore"]
    require(set(restore["sources"]) == {SOURCE}, "项目还原来源不唯一")
    require(not restore["frameworks"][TFM]["projectReferences"], "不允许项目引用")
    require([Path(path) for path in assets["packageFolders"]] == [cache], "还原缓存目录不符")
    direct = assets["project"]["frameworks"][TFM]["dependencies"]
    require(set(direct) == {PACKAGE} and direct[PACKAGE]["version"] == f"[{VERSION}, {VERSION}]",
            "直接包引用必须是精确 Du.AutoMapper [14.0.0]")

    target = assets["targets"][TFM]
    engine = f"{PACKAGE}/{VERSION}"
    require(set(assets["libraries"]) == set(target), "还原图与库条目不一致")
    inventory = {}
    for key, node in target.items():
        require(node["type"] == "package", f"还原图含非包资产: {key}")
        package_dir = cache / assets["libraries"][key]["path"]
        metadata = read_json(package_dir / ".nupkg.metadata")
        require(metadata["source"] == SOURCE, f"包来源不符: {key}")
        inventory[key] = {"source": metadata["source"], "cachePath": str(package_dir)}
    require(list(target[engine]["runtime"]) == list(target[engine]["compile"]) == [ASSET],
            "引擎编译或运行资产不符")

    archive = Path(inventory[engine]["cachePath"]) / f"{PACKAGE.lower()}.{VERSION}.nupkg"
    actual = sha256(archive)
    require(actual == expected, f"已还原包 SHA-256 不符: {actual}")
    with zipfile.ZipFile(archive) as package:
        asset_hash = hashlib.sha256(package.read(ASSET)).hexdigest()

    return {"packages": inventory, "archive": str(archive), "r4DownloadSha256": expected,
            "archiveSha256": actual, "asset": ASSET, "assetSha256": asset_hash}


def verify_observations(report, dll_path, identity):
    require(len(report["assemblies"]) == 1, "进程应只加载一份 AutoMapper")
    loaded = report["assemblies"][0]
    require(Path(loaded["path"]).resolve() == dll_path.resolve()
            and loaded["sha256"] == sha256(dll_path) == identity["assetSha256"],
            "进程加载 DLL、Release 输出与包内资产不符")
    results = {}
    warmup_deviations = []
    for row in report["observations"]:
        case, repeat = row["Case"], row["Repeat"]
        require((case, repeat) not in results, "重复的观测记录")
        require((row["Registration"], row["WarmupOrder"]) == CASES[case], "注册或预热顺序不符")
        require(len(row["Warmups"]) == len(row["WarmupOrder"]), "预热观测覆盖不足")
        for name, result in zip(row["WarmupOrder"], row["Warmups"]):
            if not result["MatchesExpected"]:
                warmup_deviations.append({"case": case, "repeat": repeat, "mapping": name})
        require(len(row["Measurements"]) == 2, "待测调用覆盖不足")
        results[case, repeat] = [(result["Actual"], result["Exceptions"]) for result in row["Measurements"]]
    require(set(results) == {(case, repeat) for case in CASES for repeat in range(1, 4)},
            "需要每侧三次独立配置观测")

    expected = (report["expected"], [])
    pairs = {}
    for stage in ("warm", "cold"):
        left = [results[f"{stage}-order-first", repeat] for repeat in range(1, 4)]
        right = [results[f"{stage}-invoice-first", repeat] for repeat in range(1, 4)]
        if left.count(left[0]) != 3 or right.count(right[0]) != 3:
            pairs[stage] = "unstable"
        elif all(result == expected for result in left[0] + right[0]):
            pairs[stage] = "as-expected"
        elif left[0] == right[0]:
            pairs[stage] = "same-deviation"
        elif stage == "warm" and warmup_deviations:
            pairs[stage] = "different-with-warmup-deviation"
        else:
            pairs[stage] = "order-difference"
    stable_difference = "order-difference" in pairs.values()
    return {"pairs": pairs, "warmupDeviations": warmup_deviations, "stableOrderDifference": stable_difference}


def main():
    parser = argparse.ArgumentParser(description="从隔离空缓存观察公开 Du.AutoMapper 14.0.0 的普通集合映射")
    parser.add_argument("output", type=Path, help="本轮新建的证据目录，父目录须已存在")
    parser.add_argument("--public-manifest", type=Path, required=True)
    parser.add_argument("--verified-downloads", type=Path, required=True)
    args = parser.parse_args()
    work = args.output.resolve()
    temp = Path(tempfile.gettempdir()).resolve()
    report_space(work.parent)
    report_space(temp)
    work.mkdir()
    cache = work / "packages"
    cache.mkdir()
    report_space(cache)
    require(not any(cache.iterdir()), "还原缓存必须为空")
    env = os.environ | {
        "DOTNET_CLI_HOME": str(work / "dotnet-home"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_NOLOGO": "1",
        "NUGET_PACKAGES": str(cache),
        "NUGET_HTTP_CACHE_PATH": str(work / "http-cache"),
        "NUGET_PLUGINS_CACHE_PATH": str(work / "plugins-cache"),
        "NUGET_SCRATCH": str(work / "nuget-scratch"),
    }
    for name in (f"{PROJECT}.csproj", "Program.cs", "verify.py"):
        shutil.copyfile(Path(__file__).with_name(name), work / name)
    write_json(work / "global.json", {"sdk": {"version": "10.0.100", "rollForward": "latestFeature"}})
    expected = get_expected_package(work, args.public_manifest, args.verified_downloads)
    (work / "NuGet.Config").write_text(
        f'<configuration><packageSources><clear/><add key="nuget.org" value="{SOURCE}"/>'
        "</packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>\n",
        encoding="utf-8")
    run(work, env, "dotnet-info.txt", ["dotnet", "--info"])
    run(work, env, "restore.log", ["dotnet", "restore", f"{PROJECT}.csproj", "--configfile",
                                  "NuGet.Config", "--packages", str(cache), "--no-http-cache",
                                  "--disable-build-servers", "--verbosity", "normal"])
    assets = work / "obj/project.assets.json"
    identity = verify_restored_package(cache, read_json(assets), expected)
    write_json(work / "package-identity.json", identity)
    run(work, env, "build.log", ["dotnet", "build", f"{PROJECT}.csproj", "-c", "Release",
                                "--no-restore", "--disable-build-servers"])
    output = work / "bin" / "Release" / TFM
    run(work, env, "observations.json", ["dotnet", str(output / f"{PROJECT}.dll")])
    report = read_json(work / "observations.json")
    summary = verify_observations(report, output / "AutoMapper.dll", identity)
    write_json(work / "result.json", {**summary, "assemblies": report["assemblies"],
                                     "measurements": 24, "warmupCalls": 12})
    print(json.dumps(summary))
    return 2 if summary["warmupDeviations"] or "unstable" in summary["pairs"].values() else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (KeyError, OSError, ValueError, subprocess.TimeoutExpired, zipfile.BadZipFile) as error:
        sys.exit(f"验收证据有误: {error}")
