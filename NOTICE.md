# 来源与许可

本工具的算法核心（`src/Core/CxHash.cs`、`RegionEnc.cs`、`Meta.cs`、`Decoy.cs`）
是 **cx-E2EE** 项目 `src/image-crypto.js` 中「V3 区域打码」路径的 C# 逐字节等价移植：

- 上游：`cxxy161/cx-E2EE`（本机 fork：`D:\project\RedMoon\cx-E2EE`）
- 参考实现（同算法的 Python 版，用于自检对照）：本工作区 `ComfyUI-CX-ImageScramble/cx_core.py`
- 使用/再分发时请遵循 cx-E2EE 仓库的许可；本目录其余部分（WinForms 交互层、图库、手势系统、
  自写 PNG 编解码等）为本工具自身实现。

自检向量需要那份 Python 参考实现：

```bash
python tests/make_vectors.py   --out <vectors> --core <cx_core.py 所在目录>
python tests/make_sort_vectors.py --out <vectors>/sort
python tests/make_samples.py   --out samples --core <cx_core.py 所在目录>
CxQuickDecode.exe --selftest <vectors>
```

## 构建环境

- .NET 8 SDK（WinForms / `net8.0-windows`）
- 首次 `dotnet restore` 需要访问 NuGet（依赖 `Svg` 3.4.7，用于把 SVG 图标按目标尺寸矢量栅格化）
- 构建：`bash build.sh`（只编译）/ `bash build.sh publish`（编译并产出单文件 exe）
  - 编译目录默认 `/mnt/c/cxq-build`，可用 `CXQ_BUILD_DIR` 覆盖
  - dotnet 优先用 PATH 里的，其次 `/mnt/c/Program Files/dotnet/dotnet.exe`，可用 `DOTNET_EXE` 指定
