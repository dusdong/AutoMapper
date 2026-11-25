#!/bin/sh
set -eu

destination="${1:?缺少 ApiCompat 参考目录}"
archive="${2:-}"
package_id=AutoMapper
version=13.0.0
asset=lib/net6.0/AutoMapper.dll
archive_sha=8ac567aa1ef2ecb44d02583432754e1a57cb64ac2bea3ef01966d25c27c7d621
asset_sha=61f544f9574b415832fb4130c8bde2e3370214a35acc75ce1e89f2032a04abe5

mkdir -p "$destination"
scratch=$(mktemp -d "$destination/reference.XXXXXXXX")
trap 'rm -rf "$scratch"' 0

verify_sha256() {
    actual=$(shasum -a 256 "$1")
    actual=${actual%% *}
    if [ "$actual" != "$2" ]; then
        printf 'ApiCompat %s SHA-256 不符。预期 %s，实际 %s\n' "$1" "$2" "$actual" >&2
        exit 1
    fi
}

if [ -z "$archive" ]; then
    archive="$scratch/automapper.$version.nupkg"
    curl --fail --silent --show-error --location --connect-timeout 20 --max-time 90 \
        "https://api.nuget.org/v3-flatcontainer/automapper/$version/automapper.$version.nupkg" --output "$archive"
fi
if [ ! -f "$archive" ]; then
    printf 'ApiCompat 参考包缺失。%s\n' "$archive" >&2
    exit 1
fi
verify_sha256 "$archive" "$archive_sha"
unzip -p "$archive" "$asset" > "$scratch/AutoMapper.dll"
verify_sha256 "$scratch/AutoMapper.dll" "$asset_sha"
mv "$scratch/AutoMapper.dll" "$destination/AutoMapper.dll"
printf 'ApiCompat reference %s/%s %s nupkg-sha256=%s dll-sha256=%s\n' \
    "$package_id" "$version" "$asset" "$archive_sha" "$asset_sha"
