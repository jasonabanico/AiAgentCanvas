using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgentCanvas.Capabilities.StructuredOutput;

/// <summary>
/// Checks a JSON value against a JSON Schema. It supports the keywords that model output
/// and generated schemas use: <c>type</c> (including a list of types), <c>properties</c>,
/// <c>required</c>, <c>additionalProperties</c>, <c>items</c>, <c>enum</c>, <c>const</c>,
/// <c>minimum</c>, <c>maximum</c>, <c>exclusiveMinimum</c>, <c>exclusiveMaximum</c>,
/// <c>minLength</c>, <c>maxLength</c>, <c>pattern</c>, <c>minItems</c>, <c>maxItems</c>,
/// <c>anyOf</c>, <c>oneOf</c>, <c>allOf</c>, and local <c>$ref</c> into <c>$defs</c> or
/// <c>definitions</c>. A keyword outside that set, such as <c>format</c>, is ignored and not
/// treated as a failure.
/// </summary>
public static class JsonSchemaValidator
{
    private const int MaxErrors = 20;
    private const int MaxDepth = 64;

    public static IReadOnlyList<string> Validate(JsonElement schema, JsonElement value)
    {
        var errors = new List<string>();
        Check(schema, schema, value, "$", errors, 0);
        return errors;
    }

    private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors, int depth)
    {
        if (errors.Count >= MaxErrors)
            return;

        if (depth > MaxDepth)
        {
            errors.Add($"{path}: the schema nests deeper than {MaxDepth} levels.");
            return;
        }

        // A boolean schema accepts everything or nothing.
        if (schema.ValueKind == JsonValueKind.True)
            return;
        if (schema.ValueKind == JsonValueKind.False)
        {
            errors.Add($"{path}: no value is allowed here.");
            return;
        }
        if (schema.ValueKind != JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.String)
        {
            var target = Resolve(root, reference.GetString()!);
            if (target is null)
            {
                errors.Add($"{path}: the schema refers to '{reference.GetString()}', which it does not define.");
                return;
            }
            Check(root, target.Value, value, path, errors, depth + 1);
            return;
        }

        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in allOf.EnumerateArray())
                Check(root, part, value, path, errors, depth + 1);
        }

        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            if (!anyOf.EnumerateArray().Any(part => Passes(root, part, value, depth)))
                errors.Add($"{path}: the value matches none of the allowed alternatives.");
        }

        if (schema.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array)
        {
            var matches = oneOf.EnumerateArray().Count(part => Passes(root, part, value, depth));
            if (matches != 1)
                errors.Add($"{path}: the value must match exactly one alternative but matches {matches}.");
        }

        if (schema.TryGetProperty("type", out var type) && !TypeMatches(type, value))
        {
            errors.Add($"{path}: expected {Describe(type)} but found {KindName(value)}.");
            return;
        }

        if (schema.TryGetProperty("const", out var constant) && !Equal(constant, value))
            errors.Add($"{path}: the value must be {constant.GetRawText()}.");

        if (schema.TryGetProperty("enum", out var options) && options.ValueKind == JsonValueKind.Array
            && !options.EnumerateArray().Any(o => Equal(o, value)))
        {
            errors.Add($"{path}: the value must be one of {string.Join(", ", options.EnumerateArray().Select(o => o.GetRawText()))}.");
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(root, schema, value, path, errors, depth);
                break;
            case JsonValueKind.Array:
                CheckArray(root, schema, value, path, errors, depth);
                break;
            case JsonValueKind.String:
                CheckString(schema, value, path, errors);
                break;
            case JsonValueKind.Number:
                CheckNumber(schema, value, path, errors);
                break;
        }
    }

    private static void CheckObject(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors, int depth)
    {
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()).Where(n => n is not null))
            {
                if (!value.TryGetProperty(name!, out _))
                    errors.Add($"{path}: the required property '{name}' is missing.");
            }
        }

        var declared = schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
            ? properties
            : (JsonElement?)null;

        foreach (var property in value.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";

            if (declared is { } d && d.TryGetProperty(property.Name, out var propertySchema))
            {
                Check(root, propertySchema, property.Value, propertyPath, errors, depth + 1);
                continue;
            }

            if (!schema.TryGetProperty("additionalProperties", out var additional))
                continue;

            if (additional.ValueKind == JsonValueKind.False)
                errors.Add($"{path}: the property '{property.Name}' is not allowed.");
            else if (additional.ValueKind == JsonValueKind.Object)
                Check(root, additional, property.Value, propertyPath, errors, depth + 1);
        }
    }

    private static void CheckArray(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors, int depth)
    {
        var count = value.GetArrayLength();

        if (schema.TryGetProperty("minItems", out var min) && min.TryGetInt32(out var minItems) && count < minItems)
            errors.Add($"{path}: needs at least {minItems} item(s) but has {count}.");
        if (schema.TryGetProperty("maxItems", out var max) && max.TryGetInt32(out var maxItems) && count > maxItems)
            errors.Add($"{path}: allows at most {maxItems} item(s) but has {count}.");

        if (schema.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False)
        {
            var index = 0;
            foreach (var element in value.EnumerateArray())
                Check(root, items, element, $"{path}[{index++}]", errors, depth + 1);
        }
    }

    private static void CheckString(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var text = value.GetString() ?? string.Empty;

        if (schema.TryGetProperty("minLength", out var min) && min.TryGetInt32(out var minLength) && text.Length < minLength)
            errors.Add($"{path}: must be at least {minLength} character(s).");
        if (schema.TryGetProperty("maxLength", out var max) && max.TryGetInt32(out var maxLength) && text.Length > maxLength)
            errors.Add($"{path}: must be at most {maxLength} character(s).");

        if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String)
        {
            try
            {
                if (!Regex.IsMatch(text, pattern.GetString()!, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                    errors.Add($"{path}: must match the pattern {pattern.GetString()}.");
            }
            catch (RegexParseException)
            {
                errors.Add($"{path}: the schema's pattern is not a valid expression.");
            }
            catch (RegexMatchTimeoutException)
            {
                errors.Add($"{path}: the schema's pattern took too long to check.");
            }
        }
    }

    private static void CheckNumber(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var number = value.GetDouble();

        if (Limit(schema, "minimum") is { } min && number < min)
            errors.Add($"{path}: must be at least {min.ToString(CultureInfo.InvariantCulture)}.");
        if (Limit(schema, "maximum") is { } max && number > max)
            errors.Add($"{path}: must be at most {max.ToString(CultureInfo.InvariantCulture)}.");
        if (Limit(schema, "exclusiveMinimum") is { } exMin && number <= exMin)
            errors.Add($"{path}: must be greater than {exMin.ToString(CultureInfo.InvariantCulture)}.");
        if (Limit(schema, "exclusiveMaximum") is { } exMax && number >= exMax)
            errors.Add($"{path}: must be less than {exMax.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static double? Limit(JsonElement schema, string name) =>
        schema.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool Passes(JsonElement root, JsonElement part, JsonElement value, int depth)
    {
        var errors = new List<string>();
        Check(root, part, value, "$", errors, depth + 1);
        return errors.Count == 0;
    }

    private static JsonElement? Resolve(JsonElement root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            return null;

        var current = root;
        foreach (var segment in reference[2..].Split('/'))
        {
            var name = segment.Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return null;
        }
        return current;
    }

    private static bool TypeMatches(JsonElement type, JsonElement value) => type.ValueKind switch
    {
        JsonValueKind.String => KindMatches(type.GetString(), value),
        JsonValueKind.Array => type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && KindMatches(t.GetString(), value)),
        _ => true,
    };

    private static bool KindMatches(string? name, JsonElement value) => name switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && IsWhole(value),
        _ => true,
    };

    private static bool IsWhole(JsonElement number)
    {
        if (number.TryGetInt64(out _))
            return true;
        var d = number.GetDouble();
        return !double.IsInfinity(d) && d == Math.Floor(d);
    }

    private static string KindName(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Number => "a number",
        JsonValueKind.String => "a string",
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        JsonValueKind.Null => "null",
        _ => "an unknown value",
    };

    private static string Describe(JsonElement type) => type.ValueKind == JsonValueKind.Array
        ? string.Join(" or ", type.EnumerateArray().Select(t => t.GetString()))
        : type.GetString() ?? "a value";

    private static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
            return false;

        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDouble() == b.GetDouble(),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText(),
        };
    }
}
