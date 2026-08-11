#!/usr/bin/env bash
# REBEL-6 conformance suite: Workbench assembly check (plan workstream A2).
#
# run.sh executes the suite on a REBEL-6 executor; this script exercises the
# other half of the "shared asset" property - every test must also assemble
# through the Workbench C# assembler (directives, ABI register names,
# platform register names, labels, immediates). No execution happens here;
# PASS means the Workbench accepts the file and produces machine code.
#
# Usage:
#   ./assemble.sh [pattern]
#
# TWB_CLI overrides the path to the built TernaryWorkbench.Cli.dll; by
# default the script builds the CLI (quietly) and uses the Debug output.
#
# Known dialect skips (reported as SKIP, never counted as failures):
#   legacy_shift_aliases_t - uses srz.t, a register right shift R2R keeps as
#   a deprecated runtime-negating alias. The ratified signed-shift design
#   retired SR{N,Z,P}.T outright (an assembler cannot negate a runtime
#   amount), so a spec-conformant assembler must reject it. The file is
#   deleted when R2R drops the aliases.

set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TESTS_DIR="$(cd "$(dirname "$0")" && pwd)/tests"
PATTERN="${1:-*}"

SKIP_LIST="legacy_shift_aliases_t"

if [ -z "${TWB_CLI:-}" ]; then
    CLI_PROJECT="$ROOT/TernaryWorkbench/src/TernaryWorkbench.Cli"
    if ! dotnet build "$CLI_PROJECT" -v q --nologo >/dev/null 2>&1; then
        echo "error: failed to build TernaryWorkbench.Cli" >&2
        exit 2
    fi
    TWB_CLI="$CLI_PROJECT/bin/Debug/net10.0/TernaryWorkbench.Cli.dll"
fi

if [ ! -f "$TWB_CLI" ]; then
    echo "error: TWB_CLI does not exist: $TWB_CLI" >&2
    exit 2
fi

pass=0
fail=0
skip=0
ran=0

for t in "$TESTS_DIR"/*.tas; do
    [ -e "$t" ] || continue
    name="$(basename "$t" .tas)"
    case "$name" in
        $PATTERN) ;;
        *) continue ;;
    esac
    case " $SKIP_LIST " in
        *" $name "*)
            printf 'SKIP  %s (R2R-only dialect - see header)\n' "$name"
            skip=$((skip + 1))
            continue
            ;;
    esac
    ran=$((ran + 1))
    out="$(dotnet "$TWB_CLI" rebel6 asm - < "$t" 2>&1)"
    rc=$?
    if [ "$rc" -eq 0 ]; then
        printf 'PASS  %s\n' "$name"
        pass=$((pass + 1))
    else
        printf 'FAIL  %s (exit=%d)\n' "$name" "$rc"
        printf '%s\n' "$out" | sed 's/^/      | /' | tail -4
        fail=$((fail + 1))
    fi
done

echo "----------------------------------------"
echo "ran: $ran  pass: $pass  fail: $fail  skip: $skip"

if [ "$ran" -eq 0 ] && [ "$skip" -eq 0 ]; then
    echo "error: no tests matched pattern '$PATTERN'" >&2
    exit 2
fi

[ "$fail" -eq 0 ] || exit 1
exit 0
