using System.Buffers.Binary;
using DolphinDotNet.Assets;
using System.Text.Json.Nodes;

internal static class ModelCompatibilityTests
{
    private static byte[] Floats(params float[] values)
    {
        using var stream = new MemoryStream();
        foreach (float value in values) stream.Write(BitConverter.GetBytes(value));
        return stream.ToArray();
    }

    private static void Fixture(string dir, string name, int mode, bool sparse, bool invalid = false,
        byte[]? indices = null, int[]? expectedIndices = null)
    {
        using var buffer = new MemoryStream();
        var views = new JsonArray();
        var accessors = new JsonArray();
        if (sparse)
        {
            // Four vertices, with vertex 0 implicitly zero and the rest supplied
            // by sparse replacement. This exercises accessors without bufferView.
            var ids = new byte[] { 1, 2, invalid ? (byte)2 : (byte)3 };
            buffer.Write(ids);
            while (buffer.Length % 4 != 0) buffer.WriteByte(0);
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = 3 });
            int offset = (int)buffer.Position;
            var values = Floats(1,0,0, 1,1,0, 0,1,0);
            buffer.Write(values);
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = values.Length });
            accessors.Add(new JsonObject {
                ["componentType"] = 5126, ["count"] = 4, ["type"] = "VEC3",
                ["sparse"] = new JsonObject {
                    ["count"] = 3,
                    ["indices"] = new JsonObject { ["bufferView"] = 0, ["componentType"] = 5121 },
                    ["values"] = new JsonObject { ["bufferView"] = 1 }
                }
            });
        }
        else
        {
            var values = Floats(0,0,0, 1,0,0, 1,1,0, 0,1,0);
            buffer.Write(values);
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = values.Length });
            accessors.Add(new JsonObject { ["bufferView"] = 0, ["componentType"] = 5126, ["count"] = 4, ["type"] = "VEC3" });
        }
        var root = new JsonObject {
            ["asset"] = new JsonObject { ["version"] = "2.0" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(0) }),
            ["nodes"] = new JsonArray(new JsonObject { ["mesh"] = 0 }),
            ["meshes"] = new JsonArray(new JsonObject { ["primitives"] = new JsonArray(new JsonObject {
                ["mode"] = mode, ["attributes"] = new JsonObject { ["POSITION"] = 0 }
            }) }),
            ["bufferViews"] = views, ["accessors"] = accessors,
            ["buffers"] = new JsonArray(new JsonObject {
                ["byteLength"] = (int)buffer.Length,
                ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray())
            })
        };
        if (indices != null)
        {
            int offset = (int)buffer.Position;
            buffer.Write(indices);
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = indices.Length });
            accessors.Add(new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5121,
                ["count"] = indices.Length, ["type"] = "SCALAR" });
            root["meshes"]![0]!["primitives"]![0]!["indices"] = accessors.Count - 1;
            root["buffers"]![0]!["byteLength"] = (int)buffer.Length;
            root["buffers"]![0]!["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray());
        }
        string source = Path.Combine(dir, name + ".gltf");
        string target = Path.Combine(dir, name + ".dndmodel");
        File.WriteAllText(source, root.ToJsonString());
        if (invalid)
        {
            try { ModelConverter.Convert(source, target); throw new Exception("Duplicate sparse index accepted."); }
            catch (InvalidDataException) { }
        }
        else
        {
            ModelConverter.Convert(source, target);
            var package=File.ReadAllBytes(target);
            if (package.Length < 160) throw new Exception("Missing converted model.");
            int meshSection=checked((int)BinaryPrimitives.ReadUInt32BigEndian(package.AsSpan(36,4)));
            int primitive=meshSection+4;
            int vertexCount=checked((int)BinaryPrimitives.ReadUInt32BigEndian(package.AsSpan(primitive+12,4)));
            int triangleIndexCount=checked((int)BinaryPrimitives.ReadUInt32BigEndian(package.AsSpan(primitive+16,4)));
            int start=primitive+44+80*vertexCount;
            var actual=Enumerable.Range(0,triangleIndexCount)
                .Select(i=>checked((int)BinaryPrimitives.ReadUInt32BigEndian(package.AsSpan(start+i*4,4)))).ToArray();
            int[] expected=expectedIndices ?? (mode switch
            {
                5=>[0,1,2,2,1,3],
                6=>[0,1,2,0,2,3],
                _=>[0,1,2]
            });
            if(!actual.SequenceEqual(expected))throw new Exception(
                $"Incorrect triangle winding for mode {mode}: {string.Join(",",actual)}");
        }
    }

    private static void SparseVariants(string dir)
    {
        // Exercise index zero, all three sparse index widths, nonzero offsets,
        // and replacements over an interleaved base buffer rather than only
        // checking topology (which cannot detect lost position replacements).
        foreach (int component in new[] { 5121, 5123, 5125 })
        foreach (bool hasBase in new[] { false, true })
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "sparse-strip.gltf")))!.AsObject();
            using var buffer = new MemoryStream();
            var views = new JsonArray();
            if (hasBase)
            {
                for (int i = 0; i < 4; i++) buffer.Write(Floats(9, 9, 9, 123));
                views.Add(new JsonObject { ["buffer"] = 0, ["byteLength"] = 64, ["byteStride"] = 16 });
            }
            int size = component == 5121 ? 1 : component == 5123 ? 2 : 4;
            int indexStart = (int)buffer.Position;
            buffer.Write(new byte[size]);
            foreach (uint index in new uint[] { 0, 1, 2, 3 })
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, index);
                buffer.Write(bytes, 0, size);
            }
            int indexView = views.Count;
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = indexStart, ["byteLength"] = size * 5 });
            while (buffer.Length % 4 != 0) buffer.WriteByte(0);
            int valuesStart = (int)buffer.Position;
            buffer.Write(new byte[4]);
            float[] positions = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0];
            buffer.Write(Floats(positions));
            int valuesView = views.Count;
            views.Add(new JsonObject { ["buffer"] = 0, ["byteOffset"] = valuesStart, ["byteLength"] = 52 });
            var accessor = root["accessors"]![0]!.AsObject();
            if (hasBase) accessor["bufferView"] = 0;
            accessor["sparse"] = new JsonObject { ["count"] = 4,
                ["indices"] = new JsonObject { ["bufferView"] = indexView, ["componentType"] = component, ["byteOffset"] = size },
                ["values"] = new JsonObject { ["bufferView"] = valuesView, ["byteOffset"] = 4 } };
            root["bufferViews"] = views;
            root["buffers"]![0]!["byteLength"] = (int)buffer.Length;
            root["buffers"]![0]!["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray());
            string input = Path.Combine(dir, $"sparse-{component}-{hasBase}.gltf"), output = input + ".dndmodel";
            File.WriteAllText(input, root.ToJsonString());
            ModelConverter.Convert(input, output);
            byte[] package = File.ReadAllBytes(output);
            int vertexStart = BinaryPrimitives.ReadInt32BigEndian(package.AsSpan(36, 4)) + 4 + 44;
            for (int i = 0; i < positions.Length; i++)
            {
                float actual = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(
                    package.AsSpan(vertexStart + (i / 3) * 80 + (i % 3) * 4, 4)));
                if (actual != positions[i]) throw new Exception("Sparse replacement position was lost.");
            }
            void Reject(Action change)
            {
                var saved = root.DeepClone().AsObject();
                change(); File.WriteAllText(input, root.ToJsonString());
                try { ModelConverter.Convert(input, output + ".invalid"); throw new Exception("Invalid sparse bounds accepted."); }
                catch (InvalidDataException) { }
                root = saved;
            }
            Reject(() => root["bufferViews"]![indexView]!["byteLength"] = size * 5 - 1);
            Reject(() => root["bufferViews"]![valuesView]!["byteLength"] = 51);
            Reject(() => root["accessors"]![0]!["sparse"]!["count"] = 5);
            Reject(() => {
                byte[] bad = buffer.ToArray(); bad[indexStart + size * 4] = 4;
                root["buffers"]![0]!["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bad);
            });
        }
    }

    public static void Run(string directory)
    {
        Fixture(directory, "strip", 5, false);
        Fixture(directory, "fan", 6, false);
        Fixture(directory, "sparse-strip", 5, true);
        Fixture(directory, "sparse-invalid", 5, true, invalid: true);
        Fixture(directory, "degenerate-strip", 5, false, indices: [0, 1, 2, 2, 1, 3],
            expectedIndices: [0, 1, 2, 1, 2, 3]);
        Fixture(directory, "degenerate-fan", 6, false, indices: [0, 1, 1, 2, 3],
            expectedIndices: [0, 1, 2, 0, 2, 3]);
        SparseVariants(directory);
        Console.WriteLine("Strip/fan winding, degenerate indices, sparse replacement values and bounds passed.");
    }
}
