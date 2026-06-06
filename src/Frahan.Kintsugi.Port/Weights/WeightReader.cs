#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace Frahan.Kintsugi.Port.Weights;

/// <summary>
/// Binary weight-file reader for the Kintsugi port.
///
/// Format (Phase 7 design, subject to revision once we actually run
/// PyTorch state-dict export):
///
///   Header (32 bytes):
///     magic "FRKINTSU"  (8 bytes ASCII)
///     version uint32     (currently 1)
///     count   uint32     (number of named tensors)
///     reserved uint64
///
///   Repeated N times:
///     name_len uint16, name (utf8, name_len bytes)
///     dtype   uint8     (1=float32, 2=float16, 3=int32, 4=uint8)
///     rank    uint8     (number of dimensions, &lt;= 8)
///     shape[rank] uint32
///     data    (rank-product * dtype_size bytes)
///
/// Reader API:
///   var reader = new WeightReader("kintsugi.bin");
///   float[] w = reader.GetFloat32("denoiser.blocks.0.attn.wq");
///
/// File generation (PyTorch side, Phase 7):
///   - Python script walks the state_dict + writes the binary above.
///   - Names match Frahan-side accessor strings exactly.
///   - The PyTorch checkpoint sits on Google Drive; the conversion
///     script is run once locally per release.
/// </summary>
public sealed class WeightReader
{
    private const string Magic = "FRKINTSU";
    private const int Version = 1;

    private readonly Dictionary<string, float[]> _tensors = new();
    private readonly Dictionary<string, int[]> _shapes = new();

    public WeightReader(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Weight file not found.", path);
        Load(path);
    }

    public float[] GetFloat32(string name)
    {
        if (!_tensors.TryGetValue(name, out var t))
            throw new KeyNotFoundException($"Tensor '{name}' not found in weight file.");
        return t;
    }

    public int[] GetShape(string name)
    {
        if (!_shapes.TryGetValue(name, out var s))
            throw new KeyNotFoundException($"Tensor '{name}' not found in weight file.");
        return s;
    }

    public IEnumerable<string> Names => _tensors.Keys;

    private void Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        // Header
        var magicBytes = reader.ReadBytes(8);
        var magicStr = System.Text.Encoding.ASCII.GetString(magicBytes);
        if (magicStr != Magic)
            throw new InvalidDataException($"Bad magic: expected '{Magic}', got '{magicStr}'.");
        int version = reader.ReadInt32();
        if (version != Version)
            throw new NotSupportedException($"Unsupported version {version}; this reader only handles v{Version}.");
        int count = reader.ReadInt32();
        // Skip reserved 8 bytes
        reader.ReadInt64();

        // Tensors
        for (int t = 0; t < count; t++)
        {
            int nameLen = reader.ReadUInt16();
            var nameBytes = reader.ReadBytes(nameLen);
            var name = System.Text.Encoding.UTF8.GetString(nameBytes);
            int dtype = reader.ReadByte();
            int rank = reader.ReadByte();
            if (rank > 8) throw new NotSupportedException($"Tensor '{name}' rank {rank} > 8.");
            var shape = new int[rank];
            long total = 1;
            for (int r = 0; r < rank; r++)
            {
                shape[r] = (int)reader.ReadUInt32();
                total *= shape[r];
            }
            if (total > int.MaxValue / 4)
                throw new NotSupportedException($"Tensor '{name}' element count exceeds 32-bit int budget.");
            float[] data;
            switch (dtype)
            {
                case 1: // float32
                    data = new float[total];
                    for (int e = 0; e < total; e++) data[e] = reader.ReadSingle();
                    break;
                case 2: // float16 -> upcast
                    data = new float[total];
                    for (int e = 0; e < total; e++)
                    {
                        ushort raw = reader.ReadUInt16();
                        data[e] = HalfToFloat(raw);
                    }
                    break;
                default:
                    throw new NotSupportedException($"dtype {dtype} not supported (only float32, float16).");
            }
            _tensors[name] = data;
            _shapes[name] = shape;
        }
    }

    /// <summary>IEEE-754 binary16 -> binary32 conversion.</summary>
    private static float HalfToFloat(ushort h)
    {
        int sign = (h >> 15) & 0x1;
        int exp = (h >> 10) & 0x1F;
        int frac = h & 0x3FF;
        int floatBits;
        if (exp == 0)
        {
            if (frac == 0) floatBits = sign << 31;
            else
            {
                while ((frac & 0x400) == 0) { frac <<= 1; exp--; }
                exp++;
                frac &= ~0x400;
                exp += 127 - 15;
                floatBits = (sign << 31) | (exp << 23) | (frac << 13);
            }
        }
        else if (exp == 0x1F)
        {
            floatBits = (sign << 31) | (0xFF << 23) | (frac << 13);
        }
        else
        {
            exp += 127 - 15;
            floatBits = (sign << 31) | (exp << 23) | (frac << 13);
        }
        return BitConverter.ToSingle(BitConverter.GetBytes(floatBits), 0);
    }
}
