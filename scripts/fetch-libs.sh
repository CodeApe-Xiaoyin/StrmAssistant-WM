#!/usr/bin/env bash
# 构建需要 3 个 Emby 自带的程序集（不随仓库分发）：Emby.Web.GenericEdit / Emby.Web.GenericUI / MovieDb。
# 从你自己的 Emby 安装目录（含 system 子目录的那一层，Docker 镜像里就是 /）复制到 libs/。
#
# 用法：./scripts/fetch-libs.sh <Emby 根目录> [4.8|4.9|4.10]     （默认 4.8：通用包按 4.8 接口编译）
# 例如从 Docker 镜像取：
#   cid=$(docker create emby/embyserver:4.8.11.0) && docker cp $cid:/system ./emby48/system && docker rm $cid
#   ./scripts/fetch-libs.sh ./emby48 4.8
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="${1:?用法：$0 <Emby 根目录> [4.10|4.9|4.8]}"
VER="${2:-4.8}"
case "$VER" in
  4.10) OUT="$ROOT/libs" ;;
  4.9|4.8) OUT="$ROOT/libs/emby$VER" ;;
  *) echo "不支持的版本：$VER"; exit 2 ;;
esac
mkdir -p "$OUT"
for f in Emby.Web.GenericEdit.dll Emby.Web.GenericUI.dll MovieDb.dll; do
  p=$(find "$SRC/system" -name "$f" -print -quit 2>/dev/null || true)
  [ -n "$p" ] || { echo "在 $SRC/system 下找不到 $f"; exit 1; }
  cp -f "$p" "$OUT/"
  echo "  $f ← $p"
done
echo "已复制到 $OUT"
