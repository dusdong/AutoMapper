# 生产 R3 映射引擎候选

从已核对并物化的引擎源码根目录运行。准备固定的 `AutoMapper 13.0.0` 参考主包、macOS ARM64 的 .NET SDK `10.0.404` 和新的输出目录。R3 序号由主仓统一分配。继续使用 [Release API 兼容入口](README.r3.md) 及其固定输入。

```sh
API_COMPAT_DOTNET="$DOTNET" sh src/AutoMapper/ApiCompat/pack-r3-engine.sh \
    "$VERSION" "$REFERENCE_NUPKG" "$OUTPUT"
```

生产脚本以候选身份运行一次 Release Rebuild 和 ApiCompat，再用同一产物执行 `pack --no-build --no-restore`。输出包括 `feed/` 主包、`symbols/` 符号包、阶段退出码、固定参考与打包日志。候选保留 `net8.0`、`AutoMapper` 程序集和命名空间，主包内带原始 MIT 许可。常规 Release 构建保持原有包名。

将 `src/RecursionProbe/` 和根目录的 `Directory.Build.props` 复制到新目录中的对等位置。在还原配置中将 `Du.AutoMapper` 映射到上述本地 feed，第三方依赖映射到 nuget.org。切换到含 `10.0.404` SDK 固定配置的空工作目录，使用独立的 `NUGET_PACKAGES` 和 `NUGET_HTTP_CACHE_PATH`。

```sh
PROJECT="$PROBE_SOURCE/src/RecursionProbe/RecursionProbe.csproj"
"$DOTNET" restore "$PROJECT" --configfile "$CONFIG" --no-http-cache "-p:CandidateVersion=$VERSION"
"$DOTNET" build "$PROJECT" -c Release --no-restore --disable-build-servers "-p:CandidateVersion=$VERSION"
python3 "$PROBE_SOURCE/src/RecursionProbe/run-probes.py" --dotnet "$DOTNET" \
    --program "$PROBE_SOURCE/src/RecursionProbe/bin/Release/net9.0/RecursionProbe.dll" \
    --dll-sha256 "$ARCHIVE_DLL_SHA256" --output "$PROBE_REPORT"
```

`ARCHIVE_DLL_SHA256` 用主包内的 `lib/net8.0/AutoMapper.dll` 计算。探针按场景启动独立进程，保存加载程序集身份、路径、摘要、断言结果、退出码和超时；候选引用使用精确 NuGet 版本。直接验证源码时可在源码目录中用同一项目省略 `CandidateVersion` 构建，再将 Release 引擎 DLL 的摘要传给探针入口。
