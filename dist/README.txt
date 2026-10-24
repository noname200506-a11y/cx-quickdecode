这个目录是「分发给使用者」的内容：样例图片 + 使用说明。
编译产物（CxQuickDecode.exe）不入库，需要时用仓库根目录的 build.sh 生成：

  bash build.sh publish      # 产出 dist/CX快速解码.exe 与 轻版

自检向量也可以用 tests/make_vectors.py 现场生成（见 NOTICE.md）。
