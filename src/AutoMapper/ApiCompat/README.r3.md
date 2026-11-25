# 在 macOS ARM64 验证 Release API 兼容性

在引擎源码根目录运行。入口在证据目录固定 .NET SDK `10.0.404`。

```sh
sh src/AutoMapper/ApiCompat/verify-release.sh /绝对路径/本轮新证据目录
```

可在第二个位置参数传入本地 `automapper.13.0.0.nupkg`。省略时从 nuget.org 读取。第三个参数设置本次 Release 的 MinVer 版本，默认为 `14.0.0`。启用 MinVer 版本覆盖可用同一入口验证固定 Git 对象的干净导出。可用 `API_COMPAT_DOTNET` 选择安装位置。

参考包为 `AutoMapper 13.0.0`，归档 SHA-256 为 `8ac567aa1ef2ecb44d02583432754e1a57cb64ac2bea3ef01966d25c27c7d621`。比较使用归档中的 `lib/net6.0/AutoMapper.dll`，SHA-256 为 `61f544f9574b415832fb4130c8bde2e3370214a35acc75ce1e89f2032a04abe5`。Unix 构建在 ApiCompat 目标前逐一校验并提取到中间目录，旧工具使用单次运行时前滚。比较读取项目原有的 13→14 基线文件，保留该版本已列出的差异并对新差异返回失败。

验收入口使用独立空缓存重新还原并执行 Release Rebuild。证据包括 SDK 与平台、还原和构建日志、二进制构建日志、比较响应文件、实际比较结果和 Release DLL 摘要。缺包、摘要错误、比较未执行、结果缺失或比较失败均退出非零。普通 Release 与候选使用同一参考输入和比较目标。
