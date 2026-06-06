#nullable disable
using System.Collections.Generic;
using Frahan.Masonry.Fractures;
using Frahan.Masonry.Geometry;
using Rhino.Geometry;

namespace Frahan.Rhino;

// Duplicated from Frahan.StonePack.GH/BlockCutOptComponents.cs::GhBlockCutOptInterop
// to avoid coupling .rhp to Grasshopper.dll. Keep in sync if helpers there
// gain features. Lives in .rhp only because plug-in commands need them.

internal static class FrahanCommandInterop
{
    public static BoundingBox3 BoxToBbox(BoundingBox bb)
    {
        return new BoundingBox3(
            bb.Min.X, bb.Min.Y, bb.Min.Z,
            bb.Max.X, bb.Max.Y, bb.Max.Z);
    }

    public static PlyMesh RhinoMeshToPly(Mesh mesh)
    {
        var verts = new List<double>(mesh.Vertices.Count * 3);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            verts.Add(v.X); verts.Add(v.Y); verts.Add(v.Z);
        }
        var tris = new List<int>(mesh.Faces.Count * 6);
        for (int i = 0; i < mesh.Faces.Count; i++)
        {
            var f = mesh.Faces[i];
            if (f.IsQuad)
            {
                tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
                tris.Add(f.A); tris.Add(f.C); tris.Add(f.D);
            }
            else
            {
                tris.Add(f.A); tris.Add(f.B); tris.Add(f.C);
            }
        }
        return new PlyMesh(verts, tris, null);
    }

    public static PlyMesh CombineMeshesToPly(IReadOnlyList<Mesh> meshes)
    {
        var verts = new List<double>();
        var tris = new List<int>();
        int vertOffset = 0;
        foreach (var mesh in meshes)
        {
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                verts.Add(v.X); verts.Add(v.Y); verts.Add(v.Z);
            }
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                if (f.IsQuad)
                {
                    tris.Add(vertOffset + f.A); tris.Add(vertOffset + f.B); tris.Add(vertOffset + f.C);
                    tris.Add(vertOffset + f.A); tris.Add(vertOffset + f.C); tris.Add(vertOffset + f.D);
                }
                else
                {
                    tris.Add(vertOffset + f.A); tris.Add(vertOffset + f.B); tris.Add(vertOffset + f.C);
                }
            }
            vertOffset += mesh.Vertices.Count;
        }
        return new PlyMesh(verts, tris, null);
    }
}
