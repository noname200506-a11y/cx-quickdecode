#!/usr/bin/env bash
# 在 WSL 里用 Windows 侧 SDK 编译并发布（产出 dist/ 下两个 exe）
# 用法：bash build.sh          # 只编译
#       bash build.sh publish  # 编译 + 发布 + 覆盖 dist
set -euo pipefail
WS="$(cd "$(dirname "$0")" && pwd)"
# 编译目录：默认放在 Windows 可见盘（WSL 里调 dotnet.exe 编译时要能拿到 Windows 路径）
BUILD="${CXQ_BUILD_DIR:-/mnt/c/cxq-build}"

# dotnet：优先 PATH，其次 Windows 默认安装位置
DOTNET="${DOTNET_EXE:-}"
if [ -z "$DOTNET" ]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET="dotnet"
  elif [ -x "/mnt/c/Program Files/dotnet/dotnet.exe" ]; then
    DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
  else
    echo "找不到 dotnet，请装 .NET 8 SDK 或设置 DOTNET_EXE=/path/to/dotnet" >&2
    exit 1
  fi
fi

rm -rf "$BUILD/src"; mkdir -p "$BUILD/src"
cp -r "$WS/src/." "$BUILD/src/"
cd "$BUILD/src"

"$DOTNET" build -c Release

if [[ "${1:-}" == "publish" ]]; then
  WIN_PUB="$(wslpath -w "$BUILD/publish")"
  WIN_PUBLITE="$(wslpath -w "$BUILD/publish-lite")"
  "$DOTNET" publish -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$WIN_PUB"
  "$DOTNET" publish -c Release -r win-x64 --self-contained false \
    -p:PublishSingleFile=true -p:DebugType=none -o "$WIN_PUBLITE"
  mkdir -p "$WS/dist"
  cp "$BUILD/publish/CxQuickDecode.exe"      "$WS/dist/CX快速解码.exe"
  cp "$BUILD/publish-lite/CxQuickDecode.exe" "$WS/dist/CX快速解码-轻版.exe"
  echo "dist updated"
fi
