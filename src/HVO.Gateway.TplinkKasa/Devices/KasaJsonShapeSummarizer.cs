using System.Text.Json;

namespace HVO.Gateway.TplinkKasa.Devices;

public static class KasaJsonShapeSummarizer
{
    public static IReadOnlyList<KasaJsonFieldShape> Summarize(JsonElement element)
    {
        var fields = new List<KasaJsonFieldShape>();
        Visit(element, string.Empty, fields);
        return fields
            .GroupBy(x => x.Path, StringComparer.Ordinal)
            .Select(group => new KasaJsonFieldShape(
                group.Key,
                string.Join("|", group.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))))
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Visit(JsonElement element, string path, List<KasaJsonFieldShape> fields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (!string.IsNullOrWhiteSpace(path))
                {
                    fields.Add(new KasaJsonFieldShape(path, "object"));
                }

                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value, Join(path, property.Name), fields);
                }
                break;

            case JsonValueKind.Array:
                fields.Add(new KasaJsonFieldShape(path, element.GetArrayLength() == 0 ? "array(empty)" : "array"));
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, path + "[]", fields);
                }
                break;

            case JsonValueKind.String:
                fields.Add(new KasaJsonFieldShape(path, "string"));
                break;

            case JsonValueKind.Number:
                fields.Add(new KasaJsonFieldShape(path, "number"));
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                fields.Add(new KasaJsonFieldShape(path, "boolean"));
                break;

            case JsonValueKind.Null:
                fields.Add(new KasaJsonFieldShape(path, "null"));
                break;

            case JsonValueKind.Undefined:
                fields.Add(new KasaJsonFieldShape(path, "undefined"));
                break;
        }
    }

    private static string Join(string prefix, string propertyName) =>
        string.IsNullOrWhiteSpace(prefix) ? propertyName : prefix + "." + propertyName;
}

public sealed record KasaJsonFieldShape(string Path, string Kind);
