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

Two things KiCad itself does that the tests had to learn, both measured on these files:

- **KiCad reads an arc, and a chamfered pad's rounded corner, up to 5 µm too far away.** Its
  effective shape for both is a polygon at `ARC_HIGH_DEF` / the pad's `GetMaxError()` of 5 µm with
  `ERROR_INSIDE`. `CopperGeometryOracleTests` says how that was established.
- **KiCad 10 writes a chamfered pad as `roundrect`**, with `(chamfer_ratio …)` and `(chamfer …)`
  beside it. `chamfered_rect` appears nowhere in `pad-shapes.kicad_pcb`, which holds twelve chamfered
  pads.
- **`Padstack().AddPrimitive(PCB_SHAPE, layer)` segfaults `SaveBoard` in 10.0.6.** The stroked
  primitive on the custom pads was added with `AddPrimitivePoly(layer, polygon, width, filled=False)`
  instead.
