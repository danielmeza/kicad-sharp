# SNEdge

A third-party board, vendored verbatim, and **the only fixture here written by KiCad 10 that this
repository did not produce**.

| | |
|---|---|
| Source | <https://github.com/ThunderTechnologies/SNEdge> |
| File | `SNEdge PCB/SNEdge.kicad_pcb` |
| Commit | `7b8732e361441b88de7704dc26d391d40cd33d73` (2026-06-07) |
| Licence | **CERN-OHL-W-2.0** — the full CERN Open Hardware Licence v2 (weakly reciprocal) text is `LICENSE` in that repository, copied here beside the board. It grants the right to copy and distribute the source; the repository is a hardware design with no separate firmware licence to conflict with it. |
| Written by | `(generator "pcbnew")`, `(generator_version "10.0")`, `(version 20260206)` — the KiCad 10 board format, **read from the file** |
| Size | 333,929 bytes, 13,683 nodes |

An open-source edge sharpener for the SNES. It is the smallest of the three, and it is here for its
format rather than its size: at the time it was vendored no larger open-hardware project had yet
re-saved a board in KiCad 10.

## What it covers that nothing else here did

- **A real KiCad 10 board.** `kicad10-pcbnew.kicad_pcb` is 5,986 bytes that this repository fed
  through `pcbnew` to obtain the format; this is 334 KB of a design someone actually built.
- **No board-level net table at all**, and every net named on the item that carries it:
  `(segment … (net "Net-(U1-CH4_OUT)"))`. All 257 segments, all 88 vias, all 37 routed arcs and all
  88 pads read their net by name and report net code 0. This is the spelling that once made 65
  tracks read as belonging to no net, and it is now measured on a board KiCad wrote rather than on
  one shaped to prove the point.
- **Six `(generated …)` forms**, `(type tuning_pattern)` — length-tuning meanders, including
  KiCad 10's time-domain fields (`is_time_domain`, `target_delay`, `target_skew`). No typed view
  models them; they are here to prove a form with no view still survives a read and a save.
- **A zone the filler broke into fourteen polygons** across `F.Cu` and `B.Cu`, 2,599 points in all,
  beside a keepout with a seven-point outline and a `(placement …)` form.
- **KiCad 10 spellings inside a footprint** that nothing models: `(units …)`,
  `(duplicate_pad_numbers_are_jumpers …)`, `(path …)`, `(sheetname …)`, `(sheetfile …)`, and three
  footprints that carry a `(zone …)` of their own.
