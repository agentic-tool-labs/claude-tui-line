#!/usr/bin/env bash
# Calibrated old-vs-new benchmark (SPEC.md acceptance criterion 4).
#
# Calibration discipline: measure old-vs-old first and require a ~0ms gap
# before trusting an old-vs-new comparison. Uses hyperfine when installed,
# otherwise falls back to a 50-iteration timing loop.
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURE="$REPO_DIR/bench/fixture.json"
OLD_SCRIPT="$HOME/.claude/statusline-command.sh"
NEW_BIN="$REPO_DIR/publish/claude-tui-line"
ITERATIONS=50
export COLUMNS=120

if [[ ! -f "$OLD_SCRIPT" ]]; then
  echo "old script not found at $OLD_SCRIPT" >&2
  exit 1
fi
if [[ ! -x "$NEW_BIN" ]]; then
  echo "new binary not found or not executable at $NEW_BIN" >&2
  exit 1
fi

if command -v hyperfine >/dev/null 2>&1; then
  echo "== hyperfine available: calibrating old-vs-old, then old-vs-new =="
  hyperfine --warmup 3 --min-runs "$ITERATIONS" \
    --input "$FIXTURE" \
    -n "old (calibration A)" "bash '$OLD_SCRIPT'" \
    -n "old (calibration B)" "bash '$OLD_SCRIPT'"
  hyperfine --warmup 3 --min-runs "$ITERATIONS" \
    --input "$FIXTURE" \
    -n "old" "bash '$OLD_SCRIPT'" \
    -n "new" "$NEW_BIN"
  exit 0
fi

echo "hyperfine not found; falling back to a ${ITERATIONS}-iteration timing loop"

python3 - "$OLD_SCRIPT" "$NEW_BIN" "$FIXTURE" "$ITERATIONS" <<'PY'
import subprocess
import sys
import time

old_script, new_bin, fixture, iterations = sys.argv[1:5]
iterations = int(iterations)

with open(fixture, "rb") as f:
    payload = f.read()

def run_many(cmd, n):
    samples = []
    for _ in range(n):
        start = time.perf_counter()
        subprocess.run(cmd, input=payload, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        samples.append((time.perf_counter() - start) * 1000.0)
    return samples

def median(samples):
    s = sorted(samples)
    n = len(s)
    mid = n // 2
    if n % 2 == 1:
        return s[mid]
    return (s[mid - 1] + s[mid]) / 2.0

old_cmd = ["bash", old_script]
new_cmd = [new_bin]

print(f"== Calibration: old-vs-old ({iterations} iterations each) ==")
old_a = median(run_many(old_cmd, iterations))
old_b = median(run_many(old_cmd, iterations))
gap = abs(old_a - old_b)
print(f"old (calibration A) p50 = {old_a:.2f} ms")
print(f"old (calibration B) p50 = {old_b:.2f} ms")
print(f"calibration gap = {gap:.2f} ms")

if gap > 5.0:
    print("WARNING: calibration gap exceeds 5ms; old-vs-new comparison below may not be trustworthy.")

print(f"== Measurement: old vs new ({iterations} iterations each) ==")
old_p50 = median(run_many(old_cmd, iterations))
new_p50 = median(run_many(new_cmd, iterations))
print(f"old p50 = {old_p50:.2f} ms")
print(f"new p50 = {new_p50:.2f} ms")
print(f"target: new p50 <= 44 ms -> {'PASS' if new_p50 <= 44.0 else 'FAIL'}")
PY
