using System.Text.Json.Nodes;

namespace CareerLiveryManager.Core.Tests.Support;

/// <summary>Builds and inspects the tiny glTF JSON documents the backfill steps rewrite.</summary>
internal static class Gltf
{
    public const double InvisibleAlpha = 0.01;

    /// <summary>A material with a textured base color and, optionally, the self-illumination
    /// extension that made 737 MAX employer decals show through near-zero alpha.</summary>
    public static JsonObject Material(string name, bool withPbr = true, bool emissiveExtension = false)
    {
        var material = new JsonObject { ["name"] = name };

        if (withPbr)
        {
            material["pbrMetallicRoughness"] = new JsonObject
            {
                ["baseColorTexture"] = new JsonObject { ["index"] = 0 },
                ["metallicFactor"] = 0.5,
            };
        }

        if (emissiveExtension)
        {
            material["extensions"] = new JsonObject
            {
                ["ASOBO_material_emissive"] = new JsonObject { ["factor"] = 1 },
                ["ASOBO_material_draw_order"] = new JsonObject { ["drawOrder"] = 3 },
            };
        }

        return material;
    }

    public static string Make(params JsonObject[] materials) => new JsonObject
    {
        ["asset"] = new JsonObject { ["version"] = "2.0" },
        ["images"] = new JsonArray(new JsonObject { ["uri"] = @"..\MODEL.PART\SOME_ALBD.PNG.KTX2" }),
        ["materials"] = new JsonArray(materials.Cast<JsonNode?>().ToArray()),
    }.ToJsonString();

    public static JsonNode Parse(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    public static JsonNode Material(JsonNode gltf, string name) =>
        gltf["materials"]!.AsArray().Single(m => (string?)m!["name"] == name)!;

    public static IReadOnlyList<string> MaterialNames(JsonNode gltf) =>
        gltf["materials"]!.AsArray().Select(m => (string)m!["name"]!).ToList();

    public static void AssertInvisible(JsonNode material)
    {
        var pbr = material["pbrMetallicRoughness"];
        Assert.NotNull(pbr);
        Assert.Equal(InvisibleAlpha, (double)pbr!["baseColorFactor"]![3]!, precision: 6);
        Assert.Null(pbr["baseColorTexture"]);
        Assert.Null(material["extensions"]?["ASOBO_material_emissive"]);
        Assert.Null(material["emissiveFactor"]);
        Assert.Null(material["emissiveTexture"]);
    }
}
