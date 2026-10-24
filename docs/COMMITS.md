# 提交记录

两种用法，任选：

- **方案 A（推荐首次发仓库）**：一条初始提交，正文用 [`commit-msg.txt`](commit-msg.txt)。
- **方案 B**：拆成 9 条，保留可读的演进历史（下面给了 `git add` 范围与标题）。

---

## 方案 A：单条初始提交

```bat
cd /d D:\project\github\cx-quickdecode
git init
git add .
git commit -F docs\commit-msg.txt
git branch -M main
git remote add origin https://github.com/<你的账号>/<仓库名>.git
git push -u origin main
git tag -a v1.5.0 -m "v1.5.0"
git push origin v1.5.0
```

---

## 方案 B：分 9 条提交

> 说明：这是"按模块搭建"的顺序，前面几条单独编译不过（缺少入口/依赖），
> 只是为了让历史读起来像正常开发过程。想要每条都能编译，用方案 A 一条提交即可。

```bat
cd /d D:\project\github\cx-quickdecode
git init

:: 1. 工程骨架 + 自检脚本
git add src\CxQuickDecode.csproj src\app.manifest tests README.md NOTICE.md build.sh .gitignore dist
git commit -m "chore: 初始化 .NET 8 WinForms 工程与参考向量脚本"

:: 2. 算法核心（hash / rng / perm / regionEnc）
git add src\Core\JsNum.cs src\Core\CxHash.cs src\Core\Rgba.cs src\Core\RegionEnc.cs
git commit -m "feat(core): 移植 cx-E2EE V3 区域打码（hash/rng/perm/regionEnc + sort）"

:: 3. 元数据与 PNG
git add src\Core\Meta.cs src\Core\PngCodec.cs
git commit -m "feat(core): V3 边框元数据读写 + 自写 PNG 编解码（保 LSB）"

:: 4. 解码编排与兜底
git add src\Core\Decoder.cs src\Core\Decoy.cs
git commit -m "feat(core): 解码编排与无元数据自动择优（unit/soft/偏移）+ 诱饵图提取"

:: 5. 自检
git add src\Core\SelfTest.cs
git commit -m "test(core): 86 项参考向量自检（hash/perm/PNG/元数据/手动/sortRegion）"

:: 6. 悬浮窗与三种入口
git add src\App\FloatForm.cs src\App\NativeMethods.cs src\App\ImageIo.cs src\App\Config.cs src\App\IconFactory.cs src\App\Cli.cs src\Program.cs
git commit -m "feat(app): 悬浮窗 + 剪贴板/截图/拖入三种解码入口 + 命令行模式"

:: 7. 剪贴板与截图
git add src\App\ClipboardReader.cs src\App\ScreenCapture.cs
git commit -m "feat(app): 原生剪贴板（FileDrop 优先）与 BitBlt 全屏框选"

:: 8. 浮层 / 预览 / 图库 / 悬浮球
git add src\App\ResultPopup.cs src\App\PreviewForm.cs src\App\Gallery.cs src\App\GalleryForm.cs src\App\OrbForm.cs
git commit -m "feat(app): 结果浮层、看图式预览、图库与悬浮球模式"

:: 9. 设置 / 手势表 / 文档
git add src\App\SettingsForm.cs src\App\HotkeyText.cs src\App\OrbAction.cs src\App\ActionsForm.cs CHANGELOG.md docs README.md
git commit -m "feat(app): 设置界面、快捷键录制与「修饰键 + 鼠标动作」手势表"

git branch -M main
git remote add origin https://github.com/<你的账号>/<仓库名>.git
git push -u origin main
git tag -a v1.5.0 -m "v1.5.0"
git push origin v1.5.0
```

---

## 提交信息规范（后续维护用）

```
<type>(<scope>): <一句话>

<body：为什么改、影响面>

<footer：Closes #12 / BREAKING CHANGE: ...>
```

`type`：`feat` / `fix` / `perf` / `refactor` / `docs` / `test` / `build` / `chore`
`scope`：`core`（算法）/ `app`（界面与系统交互）/ `ui` / `docs`

本次版本的关键提交摘要（可直接用于 Release 正文或 CHANGELOG）：

| 类型 | 内容 |
|---|---|
| feat(core) | cx-E2EE V3 打码算法 C# 逐字节移植（含 sortRegion / 诱饵图） |
| feat(core) | 自写 PNG 编解码，避免 GDI+ 破坏像素最低位 |
| feat(core) | 无元数据时 unit/soft/偏移自动择优（块缝评分） |
| feat(app) | 悬浮窗 + 剪贴板/截图/拖入三入口；结果浮层与看图式预览 |
| feat(app) | 悬浮球模式与「修饰键 + 鼠标动作」手势表 |
| feat(app) | 图库（本次运行解出的图片，可预览/另存/批量导出） |
| feat(app) | 设置界面：快捷键录制、悬浮球外观（png/jpg/svg、缩放旋转、背景色） |
| fix(app) | 原生剪贴板（FileDrop 优先），解决复制图片丢 LSB |
| fix(app) | 高 DPI 缩放导致按钮跑到窗口外；多屏窗口居中；浮层点击失效 |
| perf(app) | 空闲自动降耗（优先级 Idle + 收缩工作集） |
