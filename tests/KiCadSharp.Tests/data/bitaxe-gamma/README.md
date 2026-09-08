# Bitaxe Gamma

A third-party board, vendored verbatim.

| | |
|---|---|
| Source | <https://github.com/bitaxeorg/bitaxeGamma> |
| File | `bitaxeGamma.kicad_pcb`, at the repository root |
| Commit | `6b28568938d06701afc37d6cb2c2ba2dd9bc1013` (2025-03-16) |
| Licence | **CERN-OHL-S-2.0** — the full CERN Open Hardware Licence v2 (strongly reciprocal) text is `LICENSE` in that repository, copied here beside the board. CERN-OHL-S grants the right to copy and distribute the source; the repository is hardware only (the firmware lives separately, in `skot/ESP-Miner`), so the licence covers this file with nothing to disentangle. |
| Written by | `(generator "pcbnew")`, `(generator_version "9.0")`, `(version 20241229)` — KiCad 9, **read from the file** |
| Size | 1,221,219 bytes, 49,122 nodes |

The Bitaxe is one of the better-known open-hardware Bitcoin miners; this repository has 414 stars
and the family around it several thousand.

## What it covers that nothing else here did

- **Density**: 147 footprints, 479 pads, 913 properties, 825 segments, 200 vias, 102 net rows.
- **Seventeen zones**, sixteen of them poured — 13,083 filled-polygon points — and **one keepout
  spanning five layers**, `F.Cu B.Cu In1.Cu In2.Cu Edge.Cuts`, in the file's own order rather than
  stackup order.
- **Zone priorities that are actually used**, 0 through 12. Every other fixture leaves them at 0.
- **Every pad type and shape KiCad writes**: `smd`, `thru_hole`, `np_thru_hole` and `connect`;
  `roundrect`, `circle`, `rect` and `oval`. 77 pads are drilled and 4 of those are slotted, which
  is the only place `KiCadDrill.IsOval` is exercised against a real board.
- **97 3D models**, some footprints carrying two.
