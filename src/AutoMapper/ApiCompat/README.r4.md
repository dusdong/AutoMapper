# R4 映射引擎打包

在干净的生产提交使用 macOS ARM64 和 .NET SDK `10.0.404`。将 `REFERENCE_NUPKG` 设为 [固定 AutoMapper 13 参考主包](README.r3.md)的绝对路径，`OUT` 设为尚未创建的输出目录。

```sh
API_COMPAT_DOTNET="$DOTNET" sh src/AutoMapper/ApiCompat/pack-r3-engine.sh --r4 "$REFERENCE_NUPKG" "$OUT"
```

同一次 Release 构建执行 ApiCompat，随后复用构建结果生成 `feed/Du.AutoMapper.14.0.0.nupkg` 和 `symbols/Du.AutoMapper.14.0.0.snupkg`。目录记录生产提交、SDK、ApiCompat 和包摘要。Collection 打包取主包及其 SHA-256。
