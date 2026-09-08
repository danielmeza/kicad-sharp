# Test fixtures

Real files, copied verbatim. They are not samples: the numbers in the assertions are measurements of
*these* bytes, so replacing a file changes what the tests mean. None of them is modified in place —
a test that writes copies first.

The fixtures come in two kinds, and the difference matters more than any single file here.

## Third-party boards, in sub-directories

Three boards from three unrelated open-hardware projects, each in its own directory with the
upstream `LICENSE` beside it and a `README.md` recording the repository, the exact commit, the
licence, and the KiCad version **read out of the file** rather than assumed.

| Directory | Project | Licence | Format | Size |
|---|---|---|---|---|
| `antmicro-m2-pcie-adapter/` | [antmicro/m2-pcie-adapter](https://github.com/antmicro/m2-pcie-adapter) | Apache-2.0 | KiCad 9, `20241229` | 1,865,463 B |
| `bitaxe-gamma/` | [bitaxeorg/bitaxeGamma](https://github.com/bitaxeorg/bitaxeGamma) | CERN-OHL-S-2.0 | KiCad 9, `20241229` | 1,221,219 B |
| `snedge/` | [ThunderTechnologies/SNEdge](https://github.com/ThunderTechnologies/SNEdge) | CERN-OHL-W-2.0 | KiCad 10, `20260206` | 333,929 B |

**Why they are here.** Everything in the flat list below was produced by one project's own
generators or by this repository. A suite made of files we wrote measures one spelling and reads as
if it measured the format: the writer and the reader share every assumption, so an assumption that
is wrong is invisible in both. These three were written by `pcbnew` itself, by people with no
knowledge of this library, and they brought with them 1,653 routed segments, 380 vias, 253 routed
arcs, 24 zones of which 22 are filled, 730 pads, 142 3D models, and both spellings of a net.

**Licensing is a gate, not a footnote.** A board is vendored only when its licence permits
redistribution and covers the design files rather than only the firmware. Each directory's
`README.md` names the licence and quotes or cites what makes it apply. The Glasgow Interface
Explorer (`GlasgowEmbedded/glasgow`, 0BSD/Apache-2.0) was considered and **not** taken: the licence
is impeccable and the board is the richest of any candidate, but its file is `(version 20221018)` —
the KiCad 6/7 format, outside the range these fixtures are meant to cover.

## Files written here or by `orbion-kicad`

**Provenance is not the same as authorship.** Every `.kicad_pcb` from `orbion-kicad` was written by
that repository's own generators, which stamp `(version 20241229)` — the KiCad 9 board format, with
a `(net n "NAME")` table and numbered nets on copper. KiCad 10 writes `(version 20260206)`, names
its nets, and has no table (MEASURED: pcbnew 10.0.6 rewrote `probe-4layer` that way). That is why
`kicad10-pcbnew.kicad_pcb` is here — and why `snedge/` is, since a 5,986-byte file this repository
fed through `pcbnew` proves the format parses but not that a real board of it does.

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

## Line endings

`.gitattributes` marks this whole directory `-text`. The assertions name exact byte lengths and
compare saved copies byte for byte, so a clone with `core.autocrlf=true` rewriting every LF would
fail them with a diff nobody can see.

The RS485 sheet is flat and drawn by `eeschema`, so it carries no bus, bus entry, no-connect,
global or hierarchical label, net-class flag, text box, image or Bézier — and no `.kicad_sch` in
`orbion-kicad` does. Those forms are covered by a sheet written out in KiCad's own spelling inside
`SchematicViewTests`, not by a hand-edited fixture here: a fixture nobody's KiCad ever wrote would
look like a measurement and would not be one.
