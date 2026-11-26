# R5 映射引擎本地候选

在干净的生产提交使用 macOS ARM64 和 .NET SDK `10.0.404`。将 `REFERENCE_NUPKG` 设为 [固定 AutoMapper 13 参考主包](README.r3.md)的绝对路径。`ORDINARY_OUT` 和 `CANDIDATE_OUT` 各指向尚未创建的证据目录。

```sh
API_COMPAT_DOTNET="$DOTNET" API_COMPAT_ISOLATE_BUILD=true \
  sh src/AutoMapper/ApiCompat/verify-release.sh "$ORDINARY_OUT" "$REFERENCE_NUPKG" 14.0.1
API_COMPAT_DOTNET="$DOTNET" \
  sh src/AutoMapper/ApiCompat/pack-r3-engine.sh --r5 "$REFERENCE_NUPKG" "$CANDIDATE_OUT"
```

普通 Release 与候选分别运行对 AutoMapper 13.0.0 的 ApiCompat。候选打包使用已完成比较的 Release 输出，生成 `feed/Du.AutoMapper.14.0.1.nupkg` 和 `symbols/Du.AutoMapper.14.0.1.snupkg`。两次比较各自记录 SDK、输入参考、比较结果和 Release DLL 摘要。候选目录另记录生产提交和两份归档摘要。
