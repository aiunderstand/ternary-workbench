#!/usr/bin/env bash
# REBEL-6 conformance suite runner (plan workstream A2).
#
# Usage:
#   REBEL6_SIM=<path-to-executor> ./run.sh [pattern]
#
# REBEL6_SIM defaults to the R2R reference executor. The optional
# pattern is a shell glob matched against test basenames, e.g.
#   ./run.sh 'shifts_*'
#
# Pass criterion per test: the program exits through ECALL.T 93 with
# a0 = 0, which the executor propagates as process exit code 0. A
# nonzero exit code is the index of the failed check inside the file.
# Files named *.tas.xfail are known-broken on the current simulator
# and are skipped (reported, not counted as failures).
#
# Streaming-register replay (A1.7): a test may ship a stream script as
# <test>.script next to its .tas; the runner auto-detects it and passes
# it to the executor as --stream-script <file>.

set -u

REBEL6_SIM="${REBEL6_SIM:-/Users/stevenbos/Documents/git-repos/RV32IToREBEL/build/RV32IToREBEL}"
TESTS_DIR="$(cd "$(dirname "$0")" && pwd)/tests"
PATTERN="${1:-*}"

if [ ! -x "$REBEL6_SIM" ]; then
    echo "error: REBEL6_SIM is not an executable: $REBEL6_SIM" >&2
    echo "set REBEL6_SIM=<path to the REBEL-6 executor> and retry" >&2
    exit 2
fi

pass=0
fail=0
xfail=0
ran=0

for t in "$TESTS_DIR"/*.tas; do
    [ -e "$t" ] || continue
    name="$(basename "$t" .tas)"
    case "$name" in
        $PATTERN) ;;
        *) continue ;;
    esac
    ran=$((ran + 1))
    script="$TESTS_DIR/$name.script"
    if [ -f "$script" ]; then
        out="$("$REBEL6_SIM" --stream-script "$script" "$t" 2>&1)"
    else
        out="$("$REBEL6_SIM" "$t" 2>&1)"
    fi
    rc=$?
    if [ "$rc" -eq 0 ]; then
        printf 'PASS  %s\n' "$name"
        pass=$((pass + 1))
    else
        printf 'FAIL  %s (exit=%d)\n' "$name" "$rc"
        printf '%s\n' "$out" | sed 's/^/      | /' | tail -6
        fail=$((fail + 1))
    fi
done

for t in "$TESTS_DIR"/*.tas.xfail; do
    [ -e "$t" ] || continue
    name="$(basename "$t" .tas.xfail)"
    case "$name" in
        $PATTERN) ;;
        *) continue ;;
    esac
    printf 'XFAIL %s (known simulator issue - see README)\n' "$name"
    xfail=$((xfail + 1))
done

echo "----------------------------------------"
echo "ran: $ran  pass: $pass  fail: $fail  xfail (skipped): $xfail"

if [ "$ran" -eq 0 ] && [ "$xfail" -eq 0 ]; then
    echo "error: no tests matched pattern '$PATTERN'" >&2
    exit 2
fi

[ "$fail" -eq 0 ] || exit 1
exit 0
