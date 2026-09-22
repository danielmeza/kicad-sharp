# Oracles: what KiCad itself answers

Every file here was written by **pcbnew 10.0.6** — the `pcbnew` Python module inside
`localhost/orbion/kicad-release:10.0.6`, host version `10.0.6-10.0.6~ubuntu26.04.1` — or, for the
one session, by **Freerouting 2.4.1** from a design pcbnew wrote. They are the other side of the
comparisons in `Geometry/` and `Specctra/`: this library's answer is asserted against KiCad's, never
against its own.

The scripts that produced them are not committed: they are three to ten calls into `pcbnew`, which is
Python-only, and this repository ships none. What they called is recorded here instead, so an oracle
can be regenerated and a changed number traced to a changed KiCad.

| File | Produced by | From |
|---|---|---|
| `SNEdge.dsn`, `bitaxeGamma.dsn`, `m2-pcie-adapter.dsn` | `pcbnew.ExportSpecctraDSN(pcbnew.LoadBoard(board), path)` | the vendored boards in `../snedge/`, `../bitaxe-gamma/`, `../antmicro-m2-pcie-adapter/`, with no project file beside them, so KiCad's own default net class |
| `SNEdge-unrouted.kicad_pcb` | `LoadBoard(SNEdge)`; every track, arc and via `board.Remove`d except the `VCC` net's, which were `SetLocked(True)`; `SaveBoard` | `../snedge/SNEdge.kicad_pcb` |
| `SNEdge-unrouted.dsn` | `ExportSpecctraDSN` of the board above | — |
| `SNEdge-unrouted.ses` | `java -jar freerouting-2.4.1.jar --gui.enabled=false -de SNEdge-unrouted.dsn -do SNEdge-unrouted.ses -mp 6 -mt 8 --router.automatic_neckdown=false --router.plane_nets=GND` in `eclipse-temurin:25-jre`; the jar is SHA-256 `251101c3…afc6aa9` | the design above |
| `SNEdge-kicad-import.kicad_pcb` | `pcbnew.ImportSpecctraSES(LoadBoard(SNEdge-unrouted), SNEdge-unrouted.ses)`; `SaveBoard` — 71 tracks, 13 of them the locked ones kept, 8 vias | the board and session above |
| `pad-shapes.kicad_pcb` | `pcbnew.NewBoard`, then 36 footprints of one pad each — trapezoid, chamfered, chamfered with rounding, custom (a filled polygon and a stroked one), oval and rectangle offset from their holes — at 0°, 30° and 90°, on both sides, each ringed by eight 0.2 mm tracks and a via | — |
| `pad-shapes.dsn` | `ExportSpecctraDSN` of the board above | — |
| `*.distances.json` | for every pair of copper items on a shared copper layer whose boxes come within 1 mm: `a.GetEffectiveShape(layer).Collide(b.GetEffectiveShape(layer), clearance)` bisected on `clearance` to 0.1 µm. Up to 2,000 / 3,000 / 5,000 pairs, shuffled with seed 7 | `SNEdge`, `bitaxeGamma`, `pad-shapes` |
| `custom-primitives.kicad_pcb` | in `ghcr.io/danielmeza/orbion-kicad-release:10.0.6`, same host version: `pcbnew.CreateEmptyBoard`, then `pcbnew.FootprintLoad` of twelve hand-written footprints of one custom pad each, whose primitives are the forms 10.0.6 writes — four Béziers (an arch, an S, a loop, a hairpin), a rounded rectangle filled, filled with a pen, stroked, with a radius past half its shorter side, and stroked into a circle, an arc, filled and stroked circles, a stroked rectangle and a line — at 0°, 30° and 90° on both sides (`Rotate`, then `Flip` left-right), each ringed by 0.2 mm vias and 0.1 mm tracks placed 0.02 to 0.18 mm off a primitive's edge along its normal, on both sides of a stroke; `SaveBoard` | — |
| `custom-primitives.dsn` | `ExportSpecctraDSN` of the board above | — |
| `custom-primitives.distances.json` | every custom pad against every via and track whose box comes within 1 mm of it: the same `Collide`, bisected to 1 nm. 1,830 pairs | `custom-primitives` |
| `outline-curves.kicad_pcb` | in `ghcr.io/danielmeza/orbion-kicad-release:10.0.6`, same host version: `pcbnew.CreateEmptyBoard`, then on `Edge.Cuts` three `PCB_SHAPE` segments closed by a Bézier (`SetBezierC1`, `SetBezierC2`), a rectangle inside them with `SetCornerRadius` past half its shorter side, a rounded rectangle of its own, and `pcbnew.FootprintLoad` of four hand-written footprints — a lone pad, twice, on one net and joined by a `PCB_TRACK`; an `fp_rect (radius 2)` around a pad, at 30° and again at 90°; an `fp_line` closed by an `fp_curve` around a pad at −30°; two `fp_curve`s closing a pillow at 45°, inside the first outline; `SaveBoard` | — |
| `outline-curves.dsn` | `ExportSpecctraDSN` of the board above | — |

What KiCad itself does that the tests had to learn, all measured on these files:

- **KiCad reads an arc, and a chamfered pad's rounded corner, up to 5 µm too far away.** Its
  effective shape for both is a polygon at `ARC_HIGH_DEF` / the pad's `GetMaxError()` of 5 µm with
  `ERROR_INSIDE`. `CopperGeometryOracleTests` says how that was established.
- **KiCad 10 writes a chamfered pad as `roundrect`**, with `(chamfer_ratio …)` and `(chamfer …)`
  beside it. `chamfered_rect` appears nowhere in `pad-shapes.kicad_pcb`, which holds twelve chamfered
  pads.
- **`Padstack().AddPrimitive(PCB_SHAPE, layer)` segfaults `SaveBoard` in 10.0.6.** The stroked
  primitive on the custom pads was added with `AddPrimitivePoly(layer, polygon, width, filled=False)`
  instead.
- **KiCad cuts a Bézier into chords within its max error**, 5 µm by default (`BEZIER_POLY::GetPoly`),
  for DRC as for plotting. The chords cut the inside of each bend, so KiCad reads a neighbour inside a
  bend up to 5 µm too close and one outside it up to 5 µm too far. `CopperGeometryOracleTests` says
  how that was established.
- **KiCad writes a custom pad to a design as a convex hull**, of the polygon
  `PAD::MergePrimitivesAsPolygon` draws (`EXPORT_CUSTOM_PADS_CONVEX_HULL`, `specctra_export.cpp`).
  All 3 custom padstacks in `pad-shapes.dsn` and all 35 in `custom-primitives.dsn` are convex, to
  within 4 nm.
- **KiCad 10.0.6 collides a track with a full circle as if the circle had no pen.** A stroked
  `gr_circle`, a filled one with a pen, and a rounded rectangle that is a circle all become a
  360° `SHAPE_ARC`, and `SHAPE_ARC::Collide(const SEG&)` takes a full-circle branch that never adds
  the arc's width (`shape_arc.cpp` L301-313). Measured: tracks read 50 or 75 µm too far — half the
  pen — and a track inside a ring was not seen at all, while vias read right. So the pads in
  `custom-primitives.kicad_pcb` that hold a full circle are ringed by vias only.
- **KiCad 10.0.6 draws a stroked rectangle whose corner radius makes it a circle as a dot** the width
  of the pen. `ROUNDRECT::TransformToPolygon` makes its outline one 360° arc, and
  `TransformArcToPolygon` finds no chord between its equal ends (`ARC_CHORD_PARAMS::Compute`) and
  draws a pen stroke from the start to the start. `MergePrimitivesAsPolygon` returns that dot, the
  design's hull is of it, and a Gerber of `F.Cu` plotted by `kicad-cli` 10.0.6 flashes it; its DRC
  shape is the whole ring.
- **KiCad clamps a rectangle's corner radius to half its shorter side** as it loads it
  (`EDA_SHAPE::SetCornerRadius`), and writes the clamped value back.
- **KiCad draws a Bézier on `Edge.Cuts` within the board's max error**, 5 µm by default, with
  vertices on the curve and chords cutting the inside of each bend
  (`RebuildBezierToSegmentsPointsList(aErrorMax)`, `convert_shape_list_to_polygon.cpp`). Measured
  on `outline-curves.dsn`, whose boundary is `BOARD::GetBoardPolygonOutlines`: 71 vertices on the S
  curve, and the curve sags at most 4.98 µm from them.
- **KiCad draws a rectangle's corner radius on `Edge.Cuts`** (`ROUNDRECT::TransformToPolygon` at
  the shape's max error, same file), with vertices *outside* the true arc: 0 to 0.99 µm on the
  108 vertices of the `fp_rect` of radius 2, 0 to 0.55 µm on the 164 of the `gr_rect` of radius 5
  and on the 126 of the stadium hole of radius 3. That hole's box runs 9.9995 to 22.0005 for a
  rectangle drawn 10 to 22. The arcs of the polygon it made of the rectangle it turned by 30° come
  out −0.05 to 0.55 µm from the arc they were.
- **pcbnew saves a rectangle inside a footprint placed off-axis as a polygon.** A rectangle is
  axis-aligned by definition, so turning the footprint by 30° turned `RR1`'s `fp_rect (radius 2)`
  into an `fp_poly` whose `pts` are four `(arc (start …) (mid …) (end …))` entries and nothing
  else, the straight sides implied between them; the same footprint at 90°, `RR2`, kept its
  `fp_rect` and its `(radius 2)`. Measured in `outline-curves.kicad_pcb`.
- **KiCad's design export infers an outline when none closes.** `SPECCTRA_DB::BuiltBoardOutlines`
  calls `GetBoardPolygonOutlines(…, aInferOutlineIfNecessary = true)`, which falls back to the box
  around `Edge.Cuts`, then around everything (`specctra_export.cpp` L240,
  `BuildBoardPolygonOutlines`). This library refuses such a board instead. Read in the source, not
  measured: every outline here closes.
- **KiCad writes a footprint's `fp_line` on `Edge.Cuts` into its image as an `outline`, and not
  its `fp_curve`.** Measured on `outline-curves.dsn`: image `DEE` carries
  `(outline (path signal 100 -5000 5000 -5000 -5000))` for its line and nothing for its curve;
  `PILLOW`, two curves, carries none. The tests compare no image outline.
