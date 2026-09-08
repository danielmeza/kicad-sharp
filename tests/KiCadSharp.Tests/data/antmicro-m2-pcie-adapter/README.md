# Antmicro M.2-to-PCIe x4 adapter

A third-party board, vendored verbatim. Nothing here was written by this repository or by the
project that supplies its other fixtures, which is the point of it.

| | |
|---|---|
| Source | <https://github.com/antmicro/m2-pcie-adapter> |
| File | `m2-pcie-adapter.kicad_pcb`, at the repository root |
| Commit | `9fb024743699f4951cbf2daa437f6e0a43f6e7a8` (2025-11-19) |
| Licence | **Apache-2.0** — `LICENSE` in that repository, copied here beside the board. The repository holds hardware only, and its README says "This project is licensed under the Apache-2.0 license" without carving the design files out. |
| Written by | `(generator "pcbnew")`, `(generator_version "9.0")`, `(version 20241229)` — the KiCad 9 board format, **read from the file**, not assumed |
| Size | 1,865,463 bytes, 79,356 nodes |

Antmicro publishes a large family of Apache-2.0 KiCad boards and keeps them current; this is the
extension card for their Jetson Nano Baseboard.

## What it covers that nothing else here did

- **A four-layer board that is actually routed**: 571 segments and 92 vias over `F.Cu`, `In1.Cu`,
  `In2.Cu` and `B.Cu`, with a 13-layer stackup under `(setup …)`.
- **216 routed `(arc …)` tracks.** `probe-4layer` has none, so `KiCadTrackArc` had never been read
  in bulk against a file KiCad wrote.
- **Five zones the filler filled**, 4,556 filled-polygon points between them. One spans both inner
  layers and so was written out as two `(filled_polygon …)` on two different layers under a single
  `(zone …)` — the case a one-layer zone cannot show.
- **17 footprints carrying 3D models** whose paths start with a `${…}` path variable, and 17 of the
  20 parts placed on the back of the board.
- **A PCIe x4 edge connector**, so the net names are differential pairs rather than a handful of
  power rails.
