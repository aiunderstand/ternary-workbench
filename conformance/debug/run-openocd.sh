#!/usr/bin/env bash
# Debug conformance category (docs/rebel6-debug.md "Conformance"): drives the
# simulated JTAG stack through stock OpenOCD - remote_bitbang adapter, generic
# irscan/drscan TCL (rebel6-dm.tcl), no riscv target (its 32-bit dmi cannot
# drive the 73-bit DR; dtmcs.version = 7 is the guard).
#
# Usage: REBEL6_SIM=<executor> ./run-openocd.sh [program.tas]
set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
REBEL6_SIM="${REBEL6_SIM:-/Users/stevenbos/Documents/git-repos/RV32IToREBEL/build/RV32IToREBEL}"
PROGRAM="${1:-$HERE/../tests/add_t.tas}"
PORT="${JTAG_PORT:-3737}"

if ! command -v openocd >/dev/null; then
    echo "error: openocd not found on PATH" >&2
    exit 2
fi
if [ ! -x "$REBEL6_SIM" ]; then
    echo "error: REBEL6_SIM is not an executable: $REBEL6_SIM" >&2
    exit 2
fi

"$REBEL6_SIM" --rebel6 "$PROGRAM" --jtag-bitbang-port "$PORT" >/dev/null 2>&1 &
SIM_PID=$!
trap 'kill $SIM_PID 2>/dev/null' EXIT

openocd -f "$HERE/rebel6-sim.cfg" -f "$HERE/rebel6-sim-test.tcl"
