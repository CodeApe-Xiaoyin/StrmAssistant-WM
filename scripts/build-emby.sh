#!/usr/bin/env bash
# 构建并打包插件。
#
# 用法：./scripts/build-emby.sh            通用包（默认）：一个包同时支持 Emby 4.8 / 4.9 / 4.10
#       ./scripts/build-emby.sh 4.10|4.9|4.8   单版本包（排查兼容问题时对照用）
#
# 产物：
#   通用包 → dist/plugins/ 和 dist/StrmAssistantLite.zip（Release 只发这一个）
#   单版本 → dist/emby<版本>/plugins/
#
# 通用包按 Emby 4.8 接口、.NET 6 编译：新版 Emby 能加载引用旧接口的插件，反过来不行
# （以前 4.10 的包在 4.9 上加载失败就是这个原因）。版本差异在 Common/EmbyCompat.cs 里运行时处理。
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

build_one() {
  local target="$1" tfm bin out
  case "$target" in
    all)  tfm=net6.0; bin="StrmAssistant/bin/Release/$tfm";            out="dist/plugins" ;;
    4.10) tfm=net8.0; bin="StrmAssistant/bin/emby4.10/Release/$tfm";   out="dist/emby4.10/plugins" ;;
    4.9)  tfm=net8.0; bin="StrmAssistant/bin/emby4.9/Release/$tfm";    out="dist/emby4.9/plugins" ;;
    4.8)  tfm=net6.0; bin="StrmAssistant/bin/emby4.8/Release/$tfm";    out="dist/emby4.8/plugins" ;;
    *) echo "不支持的目标：$target（可选 all / 4.10 / 4.9 / 4.8）"; exit 2 ;;
  esac

  echo "===== 构建 $([ "$target" = all ] && echo 通用包 || echo "Emby $target")（$tfm）====="
  # 构建失败时 set -e 直接退出，不会把旧 DLL 拷进 dist
  dotnet build StrmAssistant/StrmAssistant.csproj -c Release -p:EmbyTarget="$target" -nologo -v q

  mkdir -p "$out/StrmAssistantDeps" "$out/zh" "$out/zh-hant"
  cp -f "$bin/StrmAssistantLite.dll" "$out/"
  for dep in 0Harmony.dll TinyPinyin.dll ChineseConverter.dll; do
    cp -f "$bin/$dep" "$out/StrmAssistantDeps/"
  done
  cp -f "$bin/zh/StrmAssistantLite.resources.dll" "$out/zh/"
  cp -f "$bin/zh-hant/StrmAssistantLite.resources.dll" "$out/zh-hant/"
  echo "  → $out  sha256=$(shasum -a 256 "$out/StrmAssistantLite.dll" | cut -c1-16)"
}

build_one "${1:-all}"

if [ "${1:-all}" = all ]; then
  zip="$ROOT/dist/StrmAssistantLite.zip"
  rm -f "$zip"
  (cd dist/plugins && zip -qr "$zip" StrmAssistantLite.dll StrmAssistantDeps zh zh-hant)
  echo "  → dist/StrmAssistantLite.zip"
fi
