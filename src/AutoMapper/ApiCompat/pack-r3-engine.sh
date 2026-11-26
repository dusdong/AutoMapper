#!/bin/sh
set -eu

if [ "$#" -ne 3 ]; then
    printf '用法：sh pack-r3-engine.sh <14.0.0-r3.N | --r4 | --r5> <固定参考 nupkg 绝对路径> <新输出目录>\n' >&2
    exit 2
fi
version=$1
if [ "$version" = --r4 ]; then
    version=14.0.0
    export DuR4Pack=true
elif [ "$version" = --r5 ]; then
    version=14.0.1
    export DuR5Pack=true
elif ! printf '%s\n' "$version" | grep -Eq '^14[.]0[.]0-r3[.][1-9][0-9]*$'; then
    printf '候选版本格式错误 %s\n' "$version" >&2
    exit 2
fi
case "$2" in
    /*) reference=$2 ;;
    *) printf '固定参考包需要绝对路径。\n' >&2; exit 2 ;;
esac
scripts=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
project=$(dirname -- "$scripts")/AutoMapper.csproj
test -f "$reference"
if [ "${DuR4Pack:-}" = true ] || [ "${DuR5Pack:-}" = true ]; then
    source=$(CDPATH='' cd -- "$scripts/../../.." && pwd)
    if [ -n "$(git -C "$source" status --porcelain=v1 --untracked-files=all)" ]; then
        printf '生产打包需要干净的提交。\n' >&2
        exit 1
    fi
    RepositoryCommit=$(git -C "$source" rev-parse HEAD)
    export RepositoryCommit
fi
mkdir "$3"
output=$(CDPATH='' cd -- "$3" && pwd)
trap 'printf "%s\n" "$?" > "$output/exit-code.txt"' EXIT

export DuAutoMapperCandidate=true
sh "$scripts/verify-release.sh" "$output/apicompat" "$reference" "$version"
cd "$output/apicompat"
export DOTNET_CLI_HOME="$PWD/dotnet-home"
export NUGET_PACKAGES="$PWD/packages"
export NUGET_HTTP_CACHE_PATH="$PWD/http-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_CLI_UI_LANGUAGE=en MSBUILDDISABLENODEREUSE=1
export DOTNET_ROLL_FORWARD=Major
dotnet_command=${API_COMPAT_DOTNET:-dotnet}
mkdir "$output/feed" "$output/symbols"
if [ "${DuR4Pack:-}" = true ] || [ "${DuR5Pack:-}" = true ]; then
    printf '%s\n' "$RepositoryCommit" > "$output/source-commit.txt"
    set -- "-p:ArtifactsPath=$output/apicompat/artifacts" "-p:RepositoryCommit=$RepositoryCommit"
else
    set --
fi
(
    set -x
    "$dotnet_command" pack "$project" -c Release --no-build --no-restore \
        "-p:MinVerVersionOverride=$version" -o "$output/feed" "-bl:$output/pack.binlog" "$@"
) > "$output/pack.log" 2>&1
mv "$output/feed/Du.AutoMapper.$version.snupkg" "$output/symbols/"
shasum -a 256 "$output/feed/Du.AutoMapper.$version.nupkg" "$output/symbols/Du.AutoMapper.$version.snupkg" > "$output/packages.sha256"
