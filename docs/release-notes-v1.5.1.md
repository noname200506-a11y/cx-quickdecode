# CX 快速解码 v1.5.1

修复版：预览窗改成常见看图工具的操作方式，并修掉「结果浮层点了没反应」。

## 修复

- **结果浮层点击打不开预览**
  浮层是 `WS_EX_NOACTIVATE` 窗口（为了不抢输入焦点），这种窗口上 WinForms 的 `MouseClick` 不保证派发；
  改为 `MouseUp` + 窗口消息 `WM_LBUTTONUP` 双通道（带防重入），并在按下时 `Activate()`。
  另外鼠标移入只把不透明度拉回 1、**没停淡出定时器**，停在浮层上也会消失 —— 现在移入即暂停计时、计时归零。
- **高 DPI 下预览窗顶部按钮消失**
  预览窗没设 `AutoScaleMode`，绝对坐标被 Font 自动缩放二次放大，两个按钮被推到窗口外
  （实测按钮 x=1534，窗口只有 900 宽）→ 显式 `AutoScaleMode.Dpi`，顶部/底部全部改停靠布局。
- **最大化图标空白**：原来用字符 `□` / `❐`，部分字体缺字形渲染成空白 → 改为自绘方框。
- **图库 / 设置 / 预览跑到另一块屏幕**：`CenterScreen` 在无边框 + 高 DPI 下算偏 → 改为在**鼠标所在屏幕**居中。

## 改进

- 预览窗交互按常见看图工具重做：
  - 滚轮 **以鼠标位置为锚点** 缩放（5% – 1600%）
  - 左键拖动 **平移画面**（光标变 SizeAll），不再是拖窗口
  - 双击在「适应窗口 ⇄ 1:1」之间切换
  - 底部新增 **－ / ＋** 缩放按钮
  - 顶部新增 **最大化 / 还原**（快捷键 F11），还原后回到原位置与圆角
  - 缩小用 `HighQualityBicubic`（不锯齿），放大用 `NearestNeighbor`（像素清晰）；只绘制可见区域
- 浮层信息条右侧加「点击查看大图」提示

## 下载

附件：`CXQuickDecode-1.5.1-win-x64.zip`（68.7 MB，解压即用）

| 文件 | 大小 | 说明 |
|---|---|---|
| `CX快速解码.exe` | 63 MB | 自带 .NET 运行时，任意 Win10/11 x64 双击可用 |
| `CX快速解码-轻版.exe` | 1.3 MB | 需要本机装有 .NET 8 Desktop Runtime |
| `samples/` | — | 测试图（密码 `demo2026`），可直接拖进去试 |

## 校验

```
SHA256(CXQuickDecode-1.5.1-win-x64.zip) = 3aa0386b1c4b23cf5a63c3c7151343866b1a319ea3c778a82f2df7b2b5805a3a
SHA256(CX快速解码.exe)                  = 89e5cdb0ee7ccd270b72ef3d93e000ff9863db70ceed603499bbc87570b88571
SHA256(CX快速解码-轻版.exe)             = 7ce5a057af09a04e21ee791930eddf67f9353c00f9e3e5a7aad9f2b7ceadee2e
```

## 升级

直接覆盖旧的 exe 即可。配置在 `文档\CxQuickDecode\cxq.config.json`，不受覆盖影响。

## 来源

混淆与解混淆算法来自他人仓库 **[cxxy161/cx-E2EE](https://github.com/cxxy161/cx-E2EE)**（`src/image-crypto.js` 的 V3 区域打码），
本项目只做 C# 移植与 Windows 交互层；算法部分请遵循上游许可。
