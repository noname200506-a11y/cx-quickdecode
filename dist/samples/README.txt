CX 快速解码 —— 测试样例（全部密码：demo2026）

01-single-region.scrambled.png   网页端 PNG 口径打码图（外扩 8px 边框 + 区域表在 LSB）
                                 直接拖进悬浮窗 / 复制到剪贴板 / Ctrl+Alt+Q，密码 demo2026
01-single-region.original.png    原图（对照）
01-single-region.expect.png      本工具应当给出的结果（有损，肉眼与原图几乎一致）

02-two-regions.*                 两个区域的打码图（验证多区域逆序还原）
03-large-1280x800.*              1280×800 大图（看耗时）
04-manual-unit16-soft.png        没有任何元数据的打码图（模拟 JPEG/拍屏风格：unit=16、soft=1）
                                 → 用「截图解码」框住整张图，或命令行：
                                   CxQuickDecode.exe --decode 04-manual-unit16-soft.png --password demo2026 --aabb "160,110,320,200" --unit 16 --soft 1 --out out.png
                                 → 也可以只给 --decode 让它自动择优（会打印 unit/soft/偏移）
