# MechKit 版本规则

`build-dist.ps1` 会比较上一次成功发布时的源码快照，自动更新程序集版本：

- `PATCH`（如 `0.2.0 → 0.2.1`）：小范围修复、文档、资源或少量代码调整。
- `MINOR`（如 `0.2.1 → 0.3.0`）：新增功能、新增/删除代码文件，或较大范围代码修改。
- `MAJOR`（如 `0.3.0 → 1.0.0`）：COM 标识、ProgID 或目标框架等兼容性发生变化。

普通发布无需手工改版本：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-dist.ps1
```

必要时可覆盖自动判断：

```powershell
.\build-dist.ps1 -VersionLevel Patch
.\build-dist.ps1 -VersionLevel Minor
.\build-dist.ps1 -VersionLevel Major
```

版本快照只会在安装包完整构建成功后写入 `.release-state.json`，因此失败后重试不会重复涨版本。
