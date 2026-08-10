# REBEL-6 Debug Module access over generic JTAG scans (docs/rebel6-debug.md).
#
# Adapter-independent by construction: only irscan/drscan touch the wire, so
# the same procs drive the simulated TAP over remote_bitbang today and a
# fabric TAP over a J-Link (adapter driver jlink) in Phase F.
#
# The dmi DR is the spec's divergent 73-bit format: abits(7) + data(64) +
# op(2), op at the LST end. drscan lists fields in shift order (LST first):
# op, then data, then address.

set _TAP rebel6.cpu

# --- DTM ---------------------------------------------------------------------

proc rebel6_dtmcs {} {
    global _TAP
    irscan $_TAP 0x10
    return [expr {"0x[drscan $_TAP 32 0]"}]
}

proc rebel6_check_dtm {} {
    set dtmcs [rebel6_dtmcs]
    set version [expr {$dtmcs & 0xF}]
    set abits [expr {($dtmcs >> 4) & 0x3F}]
    if {$version != 7} {
        error "dtmcs.version = $version, expected 7 (the wide-DMI format guard)"
    }
    if {$abits != 7} {
        error "dtmcs.abits = $abits, expected 7"
    }
    echo "rebel6: dtmcs ok (version 7, abits 7, wide 73-bit dmi)"
}

# One raw dmi scan; returns {status data} of the PREVIOUS transaction.
proc rebel6_dmi_scan {op addr data} {
    global _TAP
    irscan $_TAP 0x11
    set fields [drscan $_TAP 2 $op 64 $data 7 $addr]
    set status [expr {"0x[lindex $fields 0]"}]
    set rdata [expr {"0x[lindex $fields 1]"}]
    return [list $status $rdata]
}

# A complete transaction: issue, then a nop scan to collect the response.
proc rebel6_dmi {op addr data} {
    rebel6_dmi_scan $op $addr $data
    set result [rebel6_dmi_scan 0 0 0]
    set status [lindex $result 0]
    if {$status != 0} {
        # Clear the sticky error (dtmcs.dmireset) before reporting.
        global _TAP
        irscan $_TAP 0x10
        drscan $_TAP 32 0x10000
        error "dmi op $op addr [format 0x%02x $addr]: status $status"
    }
    return [lindex $result 1]
}

proc rebel6_dmi_read {addr} { rebel6_dmi 1 $addr 0 }
proc rebel6_dmi_write {addr data} { rebel6_dmi 2 $addr $data }

# --- DM run control ----------------------------------------------------------
# DMI addresses per the spec's DM table; the 64-bit abstract command carries
# cmdtype [63:56], write [55], aspace [54], aamsize [53:52], address [51:0]
# (Access Memory) / regno [15:0] (Access Register).

proc rebel6_dm_activate {} { rebel6_dmi_write 0x10 1 }

proc rebel6_halted {} { expr {([rebel6_dmi_read 0x11] >> 9) & 1} }

proc rebel6_cmderr {} { expr {([rebel6_dmi_read 0x16] >> 8) & 0x7} }

proc rebel6_clear_cmderr {} { rebel6_dmi_write 0x16 [expr {0x7 << 8}] }

proc rebel6_command {cmd} {
    rebel6_dmi_write 0x17 $cmd
    set err [rebel6_cmderr]
    if {$err != 0} {
        rebel6_clear_cmderr
        error "abstract command [format 0x%016x $cmd]: cmderr $err"
    }
}

proc rebel6_readreg {regno} {
    rebel6_command $regno
    return [rebel6_dmi_read 0x04]
}

proc rebel6_writereg {regno value} {
    rebel6_dmi_write 0x04 $value
    rebel6_command [expr {(1 << 55) | $regno}]
}

proc rebel6_resume {} { rebel6_dmi_write 0x10 [expr {1 | (1 << 30)}] }

proc rebel6_halt {} {
    # The synchronous simulator DM runs the hart only inside a resume request,
    # so between transactions it is already halted; haltreq is accepted as a
    # no-op. On hardware (Phase F) this asserts the real halt request.
    rebel6_dmi_write 0x10 [expr {1 | (1 << 31)}]
}

proc rebel6_step {} {
    # dcsr.step (bit 7 of the dcsr view at regno 0x0F00) + resume, then disarm.
    rebel6_writereg 0x0F00 [expr {1 << 7}]
    rebel6_resume
    rebel6_writereg 0x0F00 0
}

proc rebel6_readmem {addr {trytes 4}} {
    switch $trytes {
        1 { set aamsize 0 }
        2 { set aamsize 1 }
        4 { set aamsize 2 }
        default { error "trytes must be 1, 2 or 4" }
    }
    set cmd [expr {(2 << 56) | ($aamsize << 52) | ($addr & ((1 << 52) - 1))}]
    rebel6_command $cmd
    return [rebel6_dmi_read 0x04]
}

proc rebel6_writemem {addr value {trytes 4}} {
    switch $trytes {
        1 { set aamsize 0 }
        2 { set aamsize 1 }
        4 { set aamsize 2 }
        default { error "trytes must be 1, 2 or 4" }
    }
    rebel6_dmi_write 0x04 $value
    set cmd [expr {(2 << 56) | (1 << 55) | ($aamsize << 52) | ($addr & ((1 << 52) - 1))}]
    rebel6_command $cmd
}

proc rebel6_trigger {n addr} {
    rebel6_dmi_write 0x20 $n
    rebel6_dmi_write 0x22 $addr
    rebel6_dmi_write 0x21 1
}

proc rebel6_clear_trigger {n} {
    rebel6_dmi_write 0x20 $n
    rebel6_dmi_write 0x21 0
}

proc rebel6_reset_halt {} {
    # setresethaltreq, pulse ndmreset, deassert: halted at _start, nothing retired.
    rebel6_dmi_write 0x10 [expr {1 | (1 << 27)}]
    rebel6_dmi_write 0x10 [expr {1 | (1 << 27) | 2}]
    rebel6_dmi_write 0x10 1
}
