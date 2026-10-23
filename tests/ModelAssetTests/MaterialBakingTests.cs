using System.Buffers.Binary;
using System.Text.Json.Nodes;
using DolphinDotNet.Assets;

internal static class MaterialBakingTests
{
    private static int I(byte[] b, int at) => BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at, 4));
    private static float F(byte[] b, int at) => BitConverter.Int32BitsToSingle(I(b, at));

    internal static void Run(JsonObject source, string directory)
    {
        string input = Path.Combine(directory, "secondary-only.gltf"), output = input + ".dndmodel";
        foreach (string contribution in new[] { "emission", "occlusion", "both" })
        {
            var root = source.DeepClone().AsObject();
            var material = root["materials"]![0]!.AsObject();
            material.Remove("extensions"); material["alphaMode"] = "BLEND";
            var pbr = material["pbrMetallicRoughness"]!.AsObject();
            pbr.Remove("baseColorTexture");
            pbr["baseColorFactor"] = new JsonArray(.4, .6, .8, .4);
            var attributes = root["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject();
            attributes["TEXCOORD_1"] = attributes["TEXCOORD_0"]!.DeepClone();
            JsonObject Map() => new() { ["index"] = 0, ["texCoord"] = 1,
                ["extensions"] = new JsonObject { ["KHR_texture_transform"] = new JsonObject {
                    ["offset"] = new JsonArray(.25, .5), ["scale"] = new JsonArray(.5, .25),
                    ["rotation"] = Math.PI / 2 } } };
            if (contribution != "occlusion")
            {
                material["emissiveTexture"] = Map();
                material["emissiveFactor"] = new JsonArray(0, 0, .5);
            }
            if (contribution != "emission")
            {
                material["occlusionTexture"] = Map();
                material["occlusionTexture"]!["strength"] = .5;
            }
            File.WriteAllText(input, root.ToJsonString());
            ModelConverter.Convert(input, output, new() { ApproximateMaterials = true });
            byte[] data = File.ReadAllBytes(output);
            int mesh = I(data, 36) + 4, record = I(data, 52) + 4 + I(data, mesh + 8) * 32;
            int textureIndex = I(data, record + 16), texture = I(data, 68) + 4;
            for (int i = 0; i < textureIndex; i++) texture += 28 + I(data, texture + 24);
            int pixels = texture + 28;
            int[] expected = contribution switch {
                "emission" => [102, 153, 229, 102],
                "occlusion" => [51, 77, 102, 102],
                _ => [51, 77, 127, 102] };
            int[] actual = [data[pixels + 1], data[pixels + 32], data[pixels + 33], data[pixels]];
            if (actual.Where((value, i) => Math.Abs(value - expected[i]) > 1).Any())
                throw new Exception($"Secondary-only {contribution} color/alpha was lost: {string.Join(",", actual)}.");
            int uv = mesh + 44 + 24;
            if (Math.Abs(F(data, uv) - .25f) > 1e-5 || Math.Abs(F(data, uv + 4) - .5f) > 1e-5 ||
                Math.Abs(F(data, uv + 80) - .25f) > 1e-5 || Math.Abs(F(data, uv + 84) - 1f) > 1e-5)
                throw new Exception("Secondary-only UV1 rotation/scale/offset was not retained.");
            // Missing the selected UV stream must fail, even without a base map.
            attributes.Remove("TEXCOORD_1");
            File.WriteAllText(input, root.ToJsonString());
            foreach (bool gx in new[] { false, true })
            {
                try {
                    ModelConverter.Convert(input, output + ".invalid", new() {
                        ApproximateMaterials = !gx, CompileGxMaterials = gx });
                    throw new Exception("Secondary-only texture accepted a missing UV set.");
                }
                catch (InvalidDataException) { }
            }
        }
        // Constant emission has no source texture and therefore needs no UVs.
        var constant = source.DeepClone().AsObject();
        var mat = constant["materials"]![0]!.AsObject(); mat.Remove("extensions");
        mat["pbrMetallicRoughness"]!.AsObject().Remove("baseColorTexture");
        mat["emissiveFactor"] = new JsonArray(.2, .1, 0);
        constant["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject().Remove("TEXCOORD_0");
        File.WriteAllText(input, constant.ToJsonString());
        ModelConverter.Convert(input, output, new() { ApproximateMaterials = true });
        Console.WriteLine("Secondary-only baking UV1 transforms, RGBA pixels, missing UVs and constant emission passed.");
    }
}
