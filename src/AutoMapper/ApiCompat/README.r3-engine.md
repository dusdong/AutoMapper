# 生产 R3 映射引擎候选

从已核对并物化的引擎源码根目录运行。准备固定的 `AutoMapper 13.0.0` 参考主包、macOS ARM64 的 .NET SDK `10.0.404` 和新的输出目录。R3 序号由主仓统一分配。继续使用 [Release API 兼容入口](README.r3.md) 及其固定输入。

```sh
API_COMPAT_DOTNET="$DOTNET" sh src/AutoMapper/ApiCompat/pack-r3-engine.sh \
    "$VERSION" "$REFERENCE_NUPKG" "$OUTPUT"
```

生产脚本以候选身份运行一次 Release Rebuild 和 ApiCompat，再用同一产物执行 `pack --no-build --no-restore`。输出包括 `feed/` 主包、`symbols/` 符号包、阶段退出码、固定参考与打包日志。候选保留 `net8.0`、`AutoMapper` 程序集和命名空间，主包内带原始 MIT 许可。常规 Release 构建保持原有包名。

## 完整实包行为复验

将当前 `src/RecursionProbe/` 和根目录的 `Directory.Build.props` 复制到新目录中的对等位置。使用包含 `ApplicationExceptionProbe.cs` 的验收源码。在还原配置中将 `Du.AutoMapper` 映射到冻结 feed，第三方依赖映射到 nuget.org。切换到含 `10.0.404` SDK 固定配置的空工作目录，使用独立的 `NUGET_PACKAGES` 和 `NUGET_HTTP_CACHE_PATH`。从冻结清单取得主包路径 `ARCHIVE`、版本 `VERSION`、主包摘要 `ARCHIVE_SHA256` 和包内 DLL 摘要 `ARCHIVE_DLL_SHA256`。将 `PROBE_REPORT` 与 `EXCEPTION_REPORT` 设为两个不同且尚未创建的报告目录。

```sh
set -eu
printf '%s  %s\n' "$ARCHIVE_SHA256" "$ARCHIVE" | shasum -a 256 -c
test "$(unzip -p "$ARCHIVE" lib/net8.0/AutoMapper.dll | shasum -a 256 | cut -d ' ' -f 1)" = "$ARCHIVE_DLL_SHA256"
PROBE="$PROBE_SOURCE/src/RecursionProbe"
PROJECT="$PROBE/RecursionProbe.csproj"
"$DOTNET" restore "$PROJECT" --configfile "$CONFIG" --no-http-cache "-p:CandidateVersion=$VERSION"
"$DOTNET" build "$PROJECT" -c Release --no-restore --disable-build-servers "-p:CandidateVersion=$VERSION"
PROGRAM="$PROBE/bin/Release/net9.0/RecursionProbe.dll"
python3 "$PROBE/run-probes.py" --dotnet "$DOTNET" --program "$PROGRAM" \
    --dll-sha256 "$ARCHIVE_DLL_SHA256" --output "$PROBE_REPORT"
python3 "$PROBE/run-probes.py" --dotnet "$DOTNET" --program "$PROGRAM" \
    --dll-sha256 "$ARCHIVE_DLL_SHA256" --scenario application-exception --output "$EXCEPTION_REPORT"
```

第一次运行覆盖原有的 `reference-graph`、`constructor-depth`、`runtime-dispatch` 三个场景。第二次单独运行 `application-exception`。探针按场景启动独立进程，保存加载程序集身份、路径、摘要、断言结果、退出码和超时。候选引用使用精确 NuGet 版本。直接验证源码时，在源码目录中省略 `CandidateVersion` 构建，将 Release 引擎 DLL 的摘要传给第一次探针命令，运行原有三个场景。

## 应用层异常传播和恢复

应用层场景在确认加载 DLL 摘要后执行普通标量映射，由测试转换器注入带标记的 `InsufficientExecutionStackException`。探针检查调用方收到的包装链中包含同一个异常对象，再用同一 Mapper 映射普通输入，断言结果为 55。报告将异常来源标记为 `test-injection`，保留运行时、PID 和工作目录。

GHSA-gpwp-5797-mjff 的 CLR 低栈分支验收还须核对同一 DLL 的 IL 与 .NET 9 官方异常契约。使用 `ILSPY` 指定本机 `ilspycmd`，对已核对摘要的 Release 目录副本生成 IL 并在 `Mapper.MapCore` 方法内核对 `RuntimeHelpers.EnsureSufficientExecutionStack` 调用。报告保留三项证据及实际 CLR 低栈触发未观测的事实。[.NET 9 官方异常契约](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimehelpers.ensuresufficientexecutionstack?view=net-9.0)

```sh
"$ILSPY" --ilcode --type AutoMapper.Mapper "$PROBE/bin/Release/net9.0/AutoMapper.dll" > "$IL_REPORT"
```
