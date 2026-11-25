#!/bin/sh
set -eu

if [ "$#" -lt 1 ] || [ "$#" -gt 3 ]; then
    printf '用法：sh verify-release.sh <新证据目录> [固定参考包] [版本]\n' >&2
    exit 2
fi
if [ "$(uname -s)/$(uname -m)" != Darwin/arm64 ]; then
    printf '此验收入口要求 macOS ARM64。\n' >&2
    exit 1
fi
project_directory=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
archive="${2:-}"
case "$archive" in
    ''|/*) ;;
    *) archive="$PWD/$archive" ;;
esac
version="${3:-14.0.0}"
mkdir "$1"
evidence=$(CDPATH='' cd -- "$1" && pwd)
trap 'printf "%s\n" "$?" > "$evidence/exit-code.txt"' EXIT
cd "$evidence"

export DOTNET_CLI_HOME="$evidence/dotnet-home"
export NUGET_PACKAGES="$evidence/packages"
export NUGET_HTTP_CACHE_PATH="$evidence/http-cache"
export NUGET_PLUGINS_CACHE_PATH="$evidence/plugins-cache"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_CLI_UI_LANGUAGE=en MSBUILDDISABLENODEREUSE=1
# MinVer 随还原获取的工具以 .NET 8 为目标，只有新版运行时的工具链也可执行。
export DOTNET_ROLL_FORWARD=Major
dotnet_command="${API_COMPAT_DOTNET:-dotnet}"
cat > "$evidence/global.json" <<'JSON'
{
  "sdk": {
    "version": "10.0.404",
    "rollForward": "disable"
  }
}
JSON
{
    sw_vers
    uname -sm
    printf '工作目录 %s\n项目目录 %s\n' "$PWD" "$project_directory"
    "$dotnet_command" --info
} > "$evidence/environment.txt"

cat > "$evidence/NuGet.Config" <<'XML'
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="dotnet-eng" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-eng/nuget/v3/index.json" />
  </packageSources>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
XML

(
    set -x
    "$dotnet_command" restore "$project_directory/AutoMapper.csproj" --configfile "$evidence/NuGet.Config" --no-http-cache --force \
        -p:Configuration=Release -p:RestoreFallbackFolders= --verbosity normal
) > "$evidence/restore.log" 2>&1
(
    set -x
    "$dotnet_command" build "$project_directory/AutoMapper.csproj" -t:Rebuild -c Release --no-restore --disable-build-servers \
        -p:UseSharedCompilation=false -p:RunApiCompat=true -p:RunApiCompatForSrc=true \
        "-p:MinVerVersionOverride=$version" "-p:ApiCompatReferencePackage=$archive" \
        --verbosity diagnostic "-bl:$evidence/build.binlog"
) > "$evidence/build.log" 2>&1

comparison="$evidence/comparison.log"
# 见证同一次 Rebuild 中的固定参考、工具调用和实际结果。
{
    grep -E '^[[:space:]]*ApiCompat reference AutoMapper/13[.]0[.]0 lib/net6[.]0/AutoMapper[.]dll ' "$evidence/build.log" &&
    grep -E '^[[:space:]]*".*" exec --roll-forward Major .*/Microsoft[.]DotNet[.]ApiCompat[.]dll.*apicompat[.]rsp' "$evidence/build.log" &&
    grep -E '^[[:space:]]*Total Issues: 0([[:space:]]+[(]TaskId:[[:space:]]*[0-9]+[)])?[[:space:]]*$' "$evidence/build.log"
} > "$comparison" || {
    printf 'ApiCompat 比较结果不可验证，见 %s\n' "$evidence/build.log" >&2
    exit 1
}

cp "$project_directory/obj/Release/net8.0/apicompat.rsp" "$evidence/"
shasum -a 256 "$project_directory/bin/Release/net8.0/AutoMapper.dll" > "$evidence/release-sha256.txt"
cat "$comparison"
