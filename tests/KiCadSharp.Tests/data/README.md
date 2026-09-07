# Test fixtures

Real files, copied verbatim from the `orbion-kicad` repository unless the table says otherwise. They
are not samples: the numbers in the assertions are measurements of *these* bytes, so replacing a
file changes what the tests mean. None of them is modified in place — a test that writes copies
first.

**Provenance is not the same as authorship.** Every `.kicad_pcb` from `orbion-kicad` was written by
that repository's own generators, which stamp `(version 20241229)` — the KiCad 9 board format, with
a `(net n "NAME")` table and numbered nets on copper. KiCad 10 writes `(version 20260206)`, names
its nets, and has no table (MEASURED: pcbnew 10.0.6 rewrote `probe-4layer` that way). A suite made
only of orbion files therefore measures one spelling and reads as if it measured both, which is why
`kicad10-pcbnew.kicad_pcb` is here.

| File | Where it came from | Why it is here |
|---|---|---|
| `orbion.kicad_sym` | `libs/orbion.kicad_sym` | 108,583 bytes, 35 symbols, 67 sub-units, 112 pins. The KiCad 6+ sub-unit layout the document layer used to ignore. |
| `LED_0603_1608Metric.kicad_mod` | `libs/orbion.pretty/` | A standalone footprint whose root token is `footprint`, not the KiCad 5 `module`. It also carries `descr`, `tags`, `property` and `fp_rect` — four of the tokens a rebuild-from-model `Save` dropped. |
| `orbion-rs485-bridge.kicad_sch` | `templates/orbion-rs485-bridge/` | 170,001 bytes of one flat KiCad 10 sheet: 161 wires, 22 labels, 18 junctions, 8 texts, 8 rectangles, 4 bus aliases, 74 placed symbols and the 19 they were cached from in `lib_symbols`. The schematic-side tokens the document layer had no view for. |
| `duplicate-refs/` | `tests/design-rules/duplicate-refs/` | One child sheet instantiated twice, so every designator in it is used twice. Unannotated, its netlist has 7 distinct references for 14 components and `GND` with 4 nodes instead of 8. |
| `power-input.kicad_pcb` | `blocks/orbion-power.kicad_blocks/power-input.kicad_block/` | 29,733 bytes. A placed-but-unrouted board: 7 footprints with 15 pads between them, 16 layers, one net, one `gr_text`, and no copper at all. The board whose footprints must read as the same `KiCadFootprint` a `.kicad_mod` gives. |
| `probe-4layer.kicad_pcb` | `tests/design-rules/probe-4layer/` | 12,454 bytes. The only fixture with copper on it: 56 nets, 65 segments in 8 widths across `F.Cu` and `In1.Cu`, 3 vias, a keepout zone with a 4-point outline, 2 footprints, 22 layers of which 4 are copper. |
| `kicad10-pcbnew.kicad_pcb` | **pcbnew 10.0.6 wrote it** | 5,986 bytes. The only fixture in this repository any KiCad wrote: `(version 20260206)`, `(generator "pcbnew")`, no board-level net table, every net named — `(segment … (net "GND"))` — a zone the filler filled into one 16-point `filled_polygon`, and KiCad's own spelling of a track `arc`, a `dimension` and a `group`. Regenerate by loading a board in `pcbnew`, running `ZONE_FILLER`, and `SaveBoard`. |
| `orbion-4layer.kicad_pcb` | `templates/orbion-4layer/` | 2,539 bytes. The only fixture with a `setup`: a 13-layer JLC04161H stackup summing to 1.5908 mm inside a 1.6 mm board, plus a title block. Empty of everything else, which is what makes the stackup readable on its own. |

`duplicate-refs/` is a deliberately broken fixture in its home repository, where a gate asserts that
it still fails. **The copy here is the input to the annotation tests and must stay unannotated**;
annotating the original would silently disable that gate.

The RS485 sheet is flat and drawn by `eeschema`, so it carries no bus, bus entry, no-connect,
global or hierarchical label, net-class flag, text box, image or Bézier — and no `.kicad_sch` in
`orbion-kicad` does. Those forms are covered by a sheet written out in KiCad's own spelling inside
`SchematicViewTests`, not by a hand-edited fixture here: a fixture nobody's KiCad ever wrote would
look like a measurement and would not be one.
