# Debug conformance over OpenOCD (docs/rebel6-debug.md "Conformance",
# sim-testable set): run after rebel6-sim.cfg against a simulator started with
# --jtag-bitbang-port on conformance/tests/add_t.tas. Exits nonzero on the
# first failing check. The Python bitbang client (R2R run-jtag-tests.sh)
# covers the same ground plus an RSP cross-check; this script proves the
# stock-OpenOCD path the Phase F J-Link flow reuses verbatim.

set failures 0

proc check {cond what} {
    global failures
    if {$cond} {
        echo "PASS  $what"
    } else {
        echo "FAIL  $what"
        incr failures
    }
}

rebel6_check_dtm

rebel6_dm_activate
check [expr {[rebel6_dmi_read 0x10] & 1}] "dmcontrol.dmactive reads back set"
check [rebel6_halted] "hart halted at the entry point (resethaltreq semantics)"

set entry [rebel6_readreg 0x0F01]
check [expr {[rebel6_readreg 0x0002] == 0}] "mepc reads 0 at reset"
check [expr {[rebel6_readreg 0x000B] == 0}] "minstret: nothing retired yet"

# Exec trigger at entry+5 (add_t.tas: the first bne.t), resume, verify dpc.
set bp [expr {$entry + 5}]
rebel6_trigger 0 $bp
rebel6_resume
check [rebel6_halted] "resume halted again"
check [expr {[rebel6_readreg 0x0F01] == $bp}] "dpc = trigger address (match before execute)"
check [expr {([rebel6_readreg 0x0F00] & 0xF) == 2}] "dcsr.cause = trigger"

# X5 (t0) holds 5 at the first check of add_t.tas.
check [expr {[rebel6_readreg 0x1005] == 5}] "X5 (t0) = 5 at the first conformance check"

rebel6_step
check [expr {[rebel6_readreg 0x0F01] == $bp + 1}] "dcsr.step retired one instruction"
check [expr {([rebel6_readreg 0x0F00] & 0xF) == 4}] "dcsr.cause = step"

# Reset with the halt armed: back at the entry, nothing retired.
rebel6_clear_trigger 0
rebel6_reset_halt
check [rebel6_halted] "halted after ndmreset + resethaltreq"
check [expr {[rebel6_readreg 0x0F01] == $entry}] "dpc back at the entry point"
check [expr {[rebel6_readreg 0x000B] == 0}] "minstret = 0 after the reset halt"

echo "----------------------------------------"
if {$failures == 0} {
    echo "ALL OPENOCD DEBUG CHECKS PASSED"
    shutdown
} else {
    echo "$failures OPENOCD DEBUG CHECKS FAILED"
    shutdown error
}
