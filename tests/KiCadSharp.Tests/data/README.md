# Test fixtures

Real KiCad 10 files, copied verbatim from the `orbion-kicad` repository. They are not samples: the
numbers in the assertions are measurements of *these* bytes, so replacing a file changes what the
tests mean. None of them is modified in place — a test that writes copies first.

| File | Where it came from | Why it is here |
|---|---|---|
| `orbion.kicad_sym` | `libs/orbion.kicad_sym` | 108,583 bytes, 35 symbols, 67 sub-units, 112 pins. The KiCad 6+ sub-unit layout the document layer used to ignore. |
| `LED_0603_1608Metric.kicad_mod` | `libs/orbion.pretty/` | A standalone footprint whose root token is `footprint`, not the KiCad 5 `module`. It also carries `descr`, `tags`, `property` and `fp_rect` — four of the tokens a rebuild-from-model `Save` dropped. |
| `orbion-rs485-bridge.kicad_sch` | `templates/orbion-rs485-bridge/` | 170,001 bytes of one flat KiCad 10 sheet: 161 wires, 22 labels, 18 junctions, 8 texts, 8 rectangles, 4 bus aliases, 74 placed symbols and the 19 they were cached from in `lib_symbols`. The schematic-side tokens the document layer had no view for. |
| `duplicate-refs/` | `tests/design-rules/duplicate-refs/` | One child sheet instantiated twice, so every designator in it is used twice. Unannotated, its netlist has 7 distinct references for 14 components and `GND` with 4 nodes instead of 8. |

`duplicate-refs/` is a deliberately broken fixture in its home repository, where a gate asserts that
it still fails. **The copy here is the input to the annotation tests and must stay unannotated**;
annotating the original would silently disable that gate.

The RS485 sheet is flat and drawn by `eeschema`, so it carries no bus, bus entry, no-connect,
global or hierarchical label, net-class flag, text box, image or Bézier — and no `.kicad_sch` in
`orbion-kicad` does. Those forms are covered by a sheet written out in KiCad's own spelling inside
`SchematicViewTests`, not by a hand-edited fixture here: a fixture nobody's KiCad ever wrote would
look like a measurement and would not be one.
