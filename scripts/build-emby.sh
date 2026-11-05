#!/usr/bin/env bash
# 按 Emby 版本构建并打包插件。
#
# 用法：./scripts/build-emby.sh [4.10|4.9|4.8|all]   （默认 4.10）
#
# 产物：
#   4.10 → dist/plugins/              （与以前完全一致，sync-deploy.sh / check-deploy.sh 只认这个）
#   4.9  → dist/emby4.9/plugins/      （net8.0，引用 mediabrowser.server.core 4.9.1.80）
#   4.8  → dist/emby4.8/plugins/      （net6.0，引用 mediabrowser.server.core 4.8.11）
#
# 每个目录的内容都按 dist/INSTALL.md 的结构整体拷到 <Emby 配置目录>/plugins/ 即可。
# 注意：不同版本的 DLL 不能混用——4.10 的包在 4.9 上会因为找不到 4.10 的 MediaBrowser.* 而加载失败，
#       4.9/4.10 的包在 4.8（.NET 6）上根本加载不了。
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

build_one() {
  local target="$1" tfm bin out
  case "$target" in
    4.10) tfm=net8.0; bin="StrmAssistant/bin/Release/$tfm";            out="dist/plugins" ;;
    4.9)  tfm=net8.0; bin="StrmAssistant/bin/emby4.9/Release/$tfm";    out="dist/emby4.9/plugins" ;;
    4.8)  tfm=net6.0; bin="StrmAssistant/bin/emby4.8/Release/$tfm";    out="dist/emby4.8/plugins" ;;
    *) echo "不支持的版本：$target（可选 4.10 / 4.9 / 4.8 / all）"; exit 2 ;;
  esac

  echo "===== 构建 Emby $target（$tfm）====="
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

case "${1:-4.10}" in
  all) build_one 4.10; build_one 4.9; build_one 4.8 ;;
  *) build_one "$1" ;;
esac
