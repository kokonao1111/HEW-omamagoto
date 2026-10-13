#!/bin/sh
# 振り判定・つまみ判定のテストを Mac 上で実行する（実機は要らない）
#   sh device/host_test/run.sh
set -eu
here=$(cd "$(dirname "$0")" && pwd)
src="$here/../src"
out="$here/build"
mkdir -p "$out"

cxx="${CXX:-c++}"
"$cxx" -std=c++17 -Wall -I"$src" "$here/shake_test.cpp" "$src/shake.cpp" -o "$out/shake_test"
"$cxx" -std=c++17 -Wall -I"$src" "$here/knob_test.cpp" "$src/knob.cpp" -o "$out/knob_test"
"$cxx" -std=c++17 -Wall -I"$src" "$here/replay.cpp" "$src/shake.cpp" "$src/knob.cpp" -o "$out/replay"

echo "== 振り判定"
"$out/shake_test"
echo "== つまみ判定"
"$out/knob_test"
