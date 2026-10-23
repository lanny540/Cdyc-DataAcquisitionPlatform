#!/usr/bin/env bash
#
# 采集单元测试覆盖率并按口径汇总。
#
# 为什么分两组统计：
#   核心逻辑  —— 领域服务、契约转换、Excel 解析、HTTP 代理，可脱离数据库独立验证，
#                是单元测试真正应该覆盖的部分，覆盖率数字有意义。
#   端点/仓储 —— 只做参数转发或依赖 DbContext，需要真实宿主与数据库才能跑到，
#                属于集成测试范畴。混入统计会让总数字好看，但那些代码并未被真正测到。
#
# 用法：./scripts/core-coverage.sh [输出目录]

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${1:-/tmp/dap-coverage}"
RUNSETTINGS="$ROOT/DAP.Tests.UnitTests/coverage.runsettings"

# dotnet 可能不在 PATH（macOS 常见于 Homebrew 安装），这里探测一次
DOTNET="$(command -v dotnet || true)"
if [ -z "$DOTNET" ] && [ -x /opt/homebrew/bin/dotnet ]; then
  DOTNET=/opt/homebrew/bin/dotnet
fi

if [ -z "$DOTNET" ]; then
  echo "未找到 dotnet 可执行文件，请先安装 .NET SDK。" >&2
  exit 1
fi

rm -rf "$OUT_DIR"

echo "正在运行单元测试并采集覆盖率..."
TEST_LOG="$(mktemp)"
# grep 无匹配时返回非 0，在 set -e 下会直接中断脚本，因此这里显式兜住退出码
"$DOTNET" test "$ROOT/DAP.Tests.UnitTests" \
  --nologo \
  --settings "$RUNSETTINGS" \
  --results-directory:"$OUT_DIR" > "$TEST_LOG" 2>&1 || true

grep -E "Passed!|Failed!" "$TEST_LOG" || true

if grep -qE "error CS|: error" "$TEST_LOG"; then
  echo "构建失败，测试未能执行：" >&2
  grep -E ": error" "$TEST_LOG" | head -10 >&2
  rm -f "$TEST_LOG"
  exit 1
fi

rm -f "$TEST_LOG"

COVERAGE_FILE="$(find "$OUT_DIR" -name 'coverage.cobertura.xml' | head -1)"

if [ -z "$COVERAGE_FILE" ]; then
  echo "未找到覆盖率报告，测试可能未成功执行。" >&2
  exit 1
fi

python3 - "$COVERAGE_FILE" <<'PY'
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

# 依赖 DbContext、宿主或仅做参数转发的代码，单元测试跑不到，单独归类。
# 这些类型的类名在项目中唯一，按类型名过滤不会误伤其他代码。
PERSISTENCE_TYPES = {
    'AcquisitionServerConnectionResolver', 'AcquisitionServerService',
    'ConnectionDiagnosticsService', 'ManagedDataExecutionService',
    'ManagedDataCollectionScheduler', 'PlatformPresentationServiceCollectionExtensions',
}

# 注意：ServerConnectionSettings 含真实的配置解析逻辑，属于核心逻辑，不要归入仓储层。

# 纯 DTO / 路由注册 / Razor 组件，运行时才会执行到，不属于核心业务逻辑。
INFRASTRUCTURE_TYPES = {
    'AcquisitionServerContracts', 'AcquisitionServerConnectionContracts',
    'PlatformApiEndpointRouteBuilderExtensions',
}

root = ET.parse(sys.argv[1]).getroot()
groups = {
    '核心逻辑': defaultdict(lambda: [0, 0]),
    '端点/仓储层': defaultdict(lambda: [0, 0]),
}

for cls in root.iter('class'):
    name = cls.get('name', '')
    filename = cls.get('filename', '').split('/')[-1]
    stem = filename[:-3] if filename.endswith('.cs') else filename

    is_endpoint = 'Endpoints' in filename or stem == 'Program'
    is_persistence = any(t in name or t == stem for t in PERSISTENCE_TYPES)
    is_infra = any(t in name or t == stem for t in INFRASTRUCTURE_TYPES) \
        or filename.endswith('.razor')

    if is_endpoint or is_persistence:
        key = '端点/仓储层'
    elif is_infra:
        # 纯 DTO 与路由注册没有可测分支，计入会稀释核心指标
        continue
    else:
        key = '核心逻辑'

    bucket = groups[key][stem]
    for line in cls.iter('line'):
        bucket[1] += 1
        if int(line.get('hits', 0)) > 0:
            bucket[0] += 1

def pct(hit, total):
    return hit / total * 100 if total else 0.0

for key in ('核心逻辑', '端点/仓储层'):
    files = groups[key]
    hit = sum(v[0] for v in files.values())
    total = sum(v[1] for v in files.values())
    print(f"\n{'=' * 56}")
    print(f"  {key}    行覆盖率 {pct(hit, total):.1f}%   ({hit}/{total})")
    print('=' * 56)
    for stem, (h, t) in sorted(files.items(), key=lambda kv: -kv[1][1]):
        bar = '#' * int(pct(h, t) / 5)
        print(f"  {pct(h, t):5.1f}%  {stem[:40]:42} {h:4}/{t:<5} {bar}")

core_hit = sum(v[0] for v in groups['核心逻辑'].values())
core_total = sum(v[1] for v in groups['核心逻辑'].values())
print(f"\n核心逻辑行覆盖率：{pct(core_hit, core_total):.1f}%（{core_hit}/{core_total}）")

if core_total and pct(core_hit, core_total) < 70:
    print("提示：核心逻辑覆盖率低于 70%，建议补充对应单元测试。")
    sys.exit(1)
PY
