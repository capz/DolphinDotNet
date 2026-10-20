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

    private static void Fixture(string dir, string name, int mode, bool sparse, bool invalid = false)
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
            if (new FileInfo(target).Length < 160) throw new Exception("Missing converted model.");
        }
    }

    public static void Run(string directory)
    {
        Fixture(directory, "strip", 5, false);
        Fixture(directory, "fan", 6, false);
        Fixture(directory, "sparse-strip", 5, true);
        Fixture(directory, "sparse-invalid", 5, true, invalid: true);
    }
}
