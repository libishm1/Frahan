#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using Frahan.GH.Attributes;
using Frahan.Packing.TwoD;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace Frahan.GH.TwoD;

/// <summary>
/// Floor Tile (Boundary-Trimmed). Canvas wrapper for the Core FloorTileGridLayout
/// (Frahan.Packing.TwoD): divides a floor boundary into standard stone tiles on a
/// module grid (tile face + grout joint) by straight full-span (guillotine) lines,
/// trims the perimeter tiles to the boundary via Clipper2, applies the ANSI no-sliver
/// rule (>= half-tile perimeter cuts) with auto-centring, and carries a per-tile
/// GRAIN DIRECTION that is both a first-class feature (direction arrows) and the
/// source of the per-tile texture-mapping frame for a Rhino image material.
/// Synchronous: the grid+trim solves in milliseconds.
/// </summary>
[Algorithm("Floor setting-out: balanced/centred layout and the ANSI half-tile no-sliver rule",
    "ANSI A108.02 4.3.2 (centre and balance tile, no cuts smaller than half size); CTEF/TCNA tile layout practice",
    Note = "Standard floor-tiling setting-out; see outputs/2026-06-20/floor_tiling/FLOOR_TILING_DOSSIER.md")]
[Algorithm("Guillotine (straight full-span) module division + Clipper2 boundary trim",
    "Gilmore, P.C. & Gomory, R.E. (1965). Multistage cutting stock problems. Oper. Res. 13:94-120; Clipper2 (Johnson, BSL-1.0)")]
[Algorithm("Per-tile grain direction driving Rhino-native texture mapping",
    "Frahan floor-tiling workflow; grain vector -> UV frame so any scanned-stone image material aligns to the grain",
    Note = "Frahan-original; cues from the live-edge flooring example (29) edge-match + scribe")]
[RelatedComponent("Frahan > 2D Packing > Sheet Nest (Hole-Aware)",
    Reason = "Irregular-part nesting on a sheet; the floor tiler is its regular-grid, boundary-trimmed sibling.",
    ComponentGuid = "D5F10019-8A3C-4D17-B5E2-6C90F2A47D31")]
public sealed class FloorTileComponent : FrahanComponentBase
{
    public FloorTileComponent()
        : base("Floor Tile (Boundary-Trimmed)", "FloorTile",
            "Divide a floor boundary into standard stone tiles on a module grid (tile face + grout joint) by " +
            "straight full-span (guillotine) lines, trimming the perimeter tiles to the boundary. Choose the " +
            "start: a corner, a picked point, or a centred/symmetric layout that balances the border cuts " +
            "equally on opposite walls. The ANSI half-tile no-sliver rule is enforced by auto-centring (the " +
            "grid shifts by half a module to split a thin sliver into two larger border cuts). Each tile " +
            "carries a GRAIN DIRECTION, output both as a direction line (the feature) and as a texture-mapping " +
            "frame: feed the tile meshes to a Custom Preview Material with a scanned stone image and the grain " +
            "follows. Set Continuous for a slip-match (the floor reads as one slab). Deterministic.",
            "Frahan", "2D Packing")
    {
    }

    public override Guid ComponentGuid => new Guid("D5F1001A-7C3B-4E29-B1A6-0F2D9E4C8B57");
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected override Bitmap Icon => Frahan.GH.IconProvider.Load("FloorTile.png");

    // on-screen textured preview (set in SolveSafe when an Image path is supplied)
    private System.Collections.Generic.List<Mesh> _previewMeshes;
    private Rhino.Display.DisplayMaterial _previewMat;

    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    {
        if (_previewMeshes != null && _previewMat != null)
        {
            for (int i = 0; i < _previewMeshes.Count; i++)
                if (_previewMeshes[i] != null) args.Display.DrawMeshShaded(_previewMeshes[i], _previewMat);
        }
        else base.DrawViewportMeshes(args);
    }

    public override BoundingBox ClippingBox
    {
        get
        {
            if (_previewMeshes != null && _previewMeshes.Count > 0)
            {
                var bb = BoundingBox.Empty;
                for (int i = 0; i < _previewMeshes.Count; i++)
                    if (_previewMeshes[i] != null) bb.Union(_previewMeshes[i].GetBoundingBox(false));
                return bb;
            }
            return base.ClippingBox;
        }
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
        pManager.AddCurveParameter("Boundary", "B",
            "Closed planar floor outline curve (the room edge), in a WorldXY-parallel plane.", GH_ParamAccess.item);
        pManager.AddCurveParameter("Holes", "H",
            "Optional closed obstacle/hole curves inside the floor (columns, openings); tiles are trimmed around them.",
            GH_ParamAccess.list);
        pManager[1].Optional = true;
        pManager.AddNumberParameter("TileX", "Lx", "Tile face width (model units).", GH_ParamAccess.item, 600.0);
        pManager.AddNumberParameter("TileY", "Ly", "Tile face height (model units).", GH_ParamAccess.item, 600.0);
        pManager.AddNumberParameter("Joint", "Gr",
            "Grout joint width; module pitch = tile + joint. Stone 2-6, rectified 3.", GH_ParamAccess.item, 3.0);
        pManager.AddIntegerParameter("Start", "St",
            "Start mode: 0 = corner (full tile at the boundary min corner), 1 = picked point (Anchor), " +
            "2 = centred/symmetric (equal border cuts on opposite walls).", GH_ParamAccess.item, 2);
        pManager.AddPointParameter("Anchor", "Pt",
            "Lattice origin for Start = 1 (picked point); the corner of one full tile.", GH_ParamAccess.item);
        pManager[6].Optional = true;
        pManager.AddNumberParameter("Grain", "Ga",
            "Grain/vein direction in DEGREES (the grain feature; also rotates the texture mapping).",
            GH_ParamAccess.item, 0.0);
        pManager.AddIntegerParameter("GrainField", "Gf",
            "Grain pattern: 0 = monolithic (all one way), 1 = quarter-turn (checkerboard 0/90), 2 = random.",
            GH_ParamAccess.item, 0);
        pManager.AddNumberParameter("Sliver", "Sf",
            "No-sliver acceptance: perimeter cuts must be >= this fraction of the tile (0.5 ANSI, 0.333 fallback).",
            GH_ParamAccess.item, 0.5);
        pManager.AddIntegerParameter("Match", "Mt",
            "Texture continuity: 0 = per-tile (each tile shows the whole image, rotated to its grain), " +
            "1 = slip-match (UVs flow across the floor so it reads as one slab), 2 = book-match (adjacent " +
            "tiles mirror so the veins meet at the joints).", GH_ParamAccess.item, 0);
        pManager.AddIntegerParameter("Stagger", "Off",
            "Running-bond row offset: 0 = stack bond, 1 = 1/3 offset, 2 = 1/2 offset. Large-format tiles " +
            "(a side > 380) auto-cap at 1/3 to control lippage.", GH_ParamAccess.item, 0);
        pManager.AddTextParameter("Image", "Img",
            "Optional stone-texture image file path. When supplied, the floor is DRAWN on screen with the " +
            "image mapped to the grain (no extra wiring); also emitted as Material for Custom Preview / baking.",
            GH_ParamAccess.item);
        pManager[12].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
        pManager.AddCurveParameter("Tiles", "T",
            "Trimmed tile boundary polylines (full tiles + cut perimeter tiles).", GH_ParamAccess.list);
        pManager.AddLineParameter("Direction", "Dir",
            "Per-tile grain direction line from the tile centre (the grain feature; draw as arrows).",
            GH_ParamAccess.list);
        pManager.AddBooleanParameter("Full", "F",
            "True for a full module tile, false for a cut perimeter tile.", GH_ParamAccess.list);
        pManager.AddMeshParameter("TexMesh", "M",
            "Per-tile mesh carrying grain-aligned texture coordinates. Feed a Custom Preview Material with a " +
            "scanned stone image and the texture maps per the grain direction.", GH_ParamAccess.list);
        pManager.AddPlaneParameter("MapFrame", "Pl",
            "Per-tile texture-mapping plane (origin = tile centre, X rotated by the grain). Use with Rhino's " +
            "planar TextureMapping (CreatePlanarMapping + SetTextureMapping) for object-level mapping.",
            GH_ParamAccess.list);
        pManager.AddTextParameter("Report", "R",
            "Tile counts (full/cut), coverage, smallest perimeter cut vs the no-sliver threshold, grain field, " +
            "match mode and row offset.", GH_ParamAccess.item);
        pManager.AddParameter(new Grasshopper.Kernel.Parameters.Param_OGLShader(), "Material", "Mat",
            "The image material (when an Image path is supplied), for Custom Preview or baking.", GH_ParamAccess.item);
    }

    protected override void SolveSafe(IGH_DataAccess da)
    {
        Curve boundary = null;
        if (!da.GetData(0, ref boundary) || boundary == null) return;
        var holeCurves = new List<Curve>();
        da.GetDataList(1, holeCurves);

        var opt = new FloorTileOptions();
        da.GetData(2, ref opt.TileX);
        da.GetData(3, ref opt.TileY);
        da.GetData(4, ref opt.Joint);
        int start = 2; da.GetData(5, ref start);
        opt.StartMode = start == 0 ? FloorStartMode.CornerMin
            : (start == 1 ? FloorStartMode.PickedPoint : FloorStartMode.CentredSymmetric);
        var anchor = Point3d.Origin;
        if (da.GetData(6, ref anchor)) { opt.AnchorX = anchor.X; opt.AnchorY = anchor.Y; }
        double grainDeg = 0.0; da.GetData(7, ref grainDeg);
        opt.GrainAngleRad = grainDeg * Math.PI / 180.0;
        int gf = 0; da.GetData(8, ref gf);
        opt.GrainField = gf == 1 ? FloorGrainField.QuarterTurn : (gf == 2 ? FloorGrainField.Random : FloorGrainField.Monolithic);
        da.GetData(9, ref opt.SliverFraction);
        int matchMode = 0; da.GetData(10, ref matchMode);
        opt.Match = matchMode == 1 ? FloorMatchMode.Slip : (matchMode == 2 ? FloorMatchMode.Book : FloorMatchMode.PerTile);
        int stagMode = 0; da.GetData(11, ref stagMode);
        opt.RowStaggerFraction = stagMode == 1 ? 1.0 / 3.0 : (stagMode == 2 ? 0.5 : 0.0);
        string imgPath = null; da.GetData(12, ref imgPath);

        opt.TileX = Math.Max(1e-6, opt.TileX);
        opt.TileY = Math.Max(1e-6, opt.TileY);
        opt.Joint = Math.Max(0.0, opt.Joint);
        opt.SliverFraction = Math.Min(1.0, Math.Max(0.0, opt.SliverFraction));

        double z;
        var outer = CurveToLoop(boundary, "Boundary", out z);
        if (outer == null) return;
        var holes = new List<IReadOnlyList<(double X, double Y)>>();
        foreach (var hc in holeCurves)
        {
            var hl = CurveToLoop(hc, null, out _);
            if (hl != null) holes.Add(hl);
        }

        FloorTileResult r;
        try { r = FloorTileGridLayout.Pack(outer, holes.Count > 0 ? holes : null, opt); }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Floor tiling failed: " + ex.Message);
            return;
        }
        if (!r.SliverPass)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Smallest perimeter cut is below the no-sliver threshold; widen the room, change the tile size, " +
                "or push narrow cuts to a concealed edge.");

        var tiles = new List<Curve>(r.Tiles.Count);
        var dirs = new List<Line>(r.Tiles.Count);
        var full = new List<bool>(r.Tiles.Count);
        var meshes = new List<Mesh>(r.Tiles.Count);
        var frames = new List<Plane>(r.Tiles.Count);
        double arrowLen = Math.Min(opt.TileX, opt.TileY) / 3.0;
        foreach (var t in r.Tiles)
        {
            tiles.Add(LoopToCurve(t.Loop, z));
            double cdir = Math.Cos(t.DirectionRad), sdir = Math.Sin(t.DirectionRad);
            dirs.Add(new Line(new Point3d(t.Cx, t.Cy, z),
                              new Point3d(t.Cx + arrowLen * cdir, t.Cy + arrowLen * sdir, z)));
            full.Add(t.IsFull);
            frames.Add(new Plane(new Point3d(t.Cx, t.Cy, z),
                                 new Vector3d(cdir, sdir, 0), new Vector3d(-sdir, cdir, 0)));
            meshes.Add(BuildTexMesh(t, z, opt));
        }

        da.SetDataList(0, tiles);
        da.SetDataList(1, dirs);
        da.SetDataList(2, full);
        da.SetDataList(3, meshes);
        da.SetDataList(4, frames);
        da.SetData(5, r.Report);

        // image material + on-screen textured preview (draw the floor with the stone image, mapped to the grain)
        _previewMeshes = null; _previewMat = null;
        object ghMat = null;
        if (!string.IsNullOrEmpty(imgPath) && System.IO.File.Exists(imgPath))
        {
            var dm = new Rhino.Display.DisplayMaterial(System.Drawing.Color.FromArgb(206, 190, 160));
            try { dm.SetBitmapTexture(imgPath, true); } catch { }
            _previewMat = dm; _previewMeshes = meshes;
            ghMat = new Grasshopper.Kernel.Types.GH_Material(dm);
        }
        else if (!string.IsNullOrEmpty(imgPath))
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Image file not found: " + imgPath);
        da.SetData(6, ghMat);

        if (opt.LippageCapApplied)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "Running-bond offset capped at 1/3 for the large-format tile (lippage rule).");
    }

    // fan-triangulated tile mesh + grain-aligned per-vertex texture coordinates
    private static Mesh BuildTexMesh(FloorTile t, double z, FloorTileOptions opt)
    {
        var loop = t.Loop;
        int n = loop.Count;
        if (n < 3) return null;
        var m = new Mesh();
        double cgx = 0, cgy = 0;
        for (int i = 0; i < n; i++) { cgx += loop[i].X; cgy += loop[i].Y; }
        cgx /= n; cgy /= n;

        double c = Math.Cos(-t.DirectionRad), s = Math.Sin(-t.DirectionRad);
        bool slip = opt.Match == FloorMatchMode.Slip;
        bool flipU = opt.Match == FloorMatchMode.Book && ((t.Col & 1) != 0);
        bool flipV = opt.Match == FloorMatchMode.Book && ((t.Row & 1) != 0);
        void AddUv(double x, double y)
        {
            double u, v;
            if (slip)
            {
                double lx = x * c - y * s, ly = x * s + y * c; // floor-anchored flow (slip-match)
                u = lx / opt.TileX; v = ly / opt.TileY;
            }
            else
            {
                double dx = x - t.Cx, dy = y - t.Cy;
                double lx = dx * c - dy * s, ly = dx * s + dy * c;
                u = lx / opt.TileX + 0.5; v = ly / opt.TileY + 0.5;
                if (flipU) u = 1.0 - u;   // book-match: mirror across vertical joints
                if (flipV) v = 1.0 - v;   // book-match: mirror across horizontal joints
            }
            m.TextureCoordinates.Add((float)u, (float)v);
        }
        for (int i = 0; i < n; i++) { m.Vertices.Add(loop[i].X, loop[i].Y, z); AddUv(loop[i].X, loop[i].Y); }
        int cidx = m.Vertices.Add(cgx, cgy, z); AddUv(cgx, cgy);
        for (int i = 0; i < n; i++) m.Faces.AddFace(i, (i + 1) % n, cidx);
        m.Normals.ComputeNormals();
        m.Compact();
        return m;
    }

    private static Curve LoopToCurve(IReadOnlyList<(double X, double Y)> loop, double z)
    {
        var pts = new List<Point3d>(loop.Count + 1);
        for (int i = 0; i < loop.Count; i++) pts.Add(new Point3d(loop[i].X, loop[i].Y, z));
        pts.Add(pts[0]);
        return new PolylineCurve(pts);
    }

    private List<(double X, double Y)> CurveToLoop(Curve curve, string label, out double planeZ)
    {
        planeZ = 0.0;
        if (curve == null) return null;
        if (!curve.IsClosed)
        {
            if (label != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, label + " curve is open; rejected.");
            return null;
        }
        IList<Point3d> pts;
        if (curve.TryGetPolyline(out var pl)) pts = pl;
        else
        {
            var div = curve.DivideByCount(Math.Max(8, (int)(curve.GetLength() / 50.0)), true);
            if (div == null || div.Length < 3) return null;
            var tmp = new List<Point3d>(div.Length);
            foreach (var tt in div) tmp.Add(curve.PointAt(tt));
            pts = tmp;
        }
        int n = pts.Count;
        if (n > 1 && pts[0].DistanceTo(pts[n - 1]) < 1e-9) n--;
        if (n < 3) return null;
        double zMin = double.MaxValue, zMax = double.MinValue, span = 0;
        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            var p = pts[i];
            if (p.Z < zMin) zMin = p.Z; if (p.Z > zMax) zMax = p.Z;
            if (p.X < xMin) xMin = p.X; if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y; if (p.Y > yMax) yMax = p.Y;
        }
        span = Math.Max(xMax - xMin, yMax - yMin);
        if (zMax - zMin > 1e-6 * (1.0 + span))
        {
            if (label != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                label + " curve is not in a WorldXY-parallel plane; rejected.");
            return null;
        }
        planeZ = 0.5 * (zMin + zMax);
        var loop = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++) loop.Add((pts[i].X, pts[i].Y));
        double a = 0;
        for (int i = 0; i < n; i++) { var j = (i + 1) % n; a += loop[i].X * loop[j].Y - loop[j].X * loop[i].Y; }
        if (a < 0) loop.Reverse(); // CCW for the Core
        return loop;
    }
}
