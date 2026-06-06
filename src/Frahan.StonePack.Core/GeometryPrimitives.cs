using System;

namespace Frahan.Core;

public readonly struct Vec3
{
    public Vec3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public override string ToString() => $"{X:0.###}, {Y:0.###}, {Z:0.###}";
}

public readonly struct Size3
{
    public Size3(double width, double depth, double height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Depth = depth;
        Height = height;
    }

    public double Width { get; }
    public double Depth { get; }
    public double Height { get; }
    public double Volume => Width * Depth * Height;

    public Size3 RotatedYaw90() => new Size3(Depth, Width, Height);
}

public readonly struct Box3
{
    public Box3(Vec3 min, Size3 size)
    {
        Min = min;
        Size = size;
    }

    public Vec3 Min { get; }
    public Size3 Size { get; }
    public Vec3 Max => new Vec3(Min.X + Size.Width, Min.Y + Size.Depth, Min.Z + Size.Height);
}
