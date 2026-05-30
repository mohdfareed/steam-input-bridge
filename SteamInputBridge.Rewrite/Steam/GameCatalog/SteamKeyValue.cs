using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ValveKeyValue;

namespace SteamInputBridge.Steam.GameCatalog;

internal sealed class SteamKeyValue
{
    public Dictionary<string, SteamKeyValue> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Value { get; set; }

    public SteamKeyValue? GetChild(string key)
    {
        return Children.TryGetValue(key, out SteamKeyValue? child) ? child : null;
    }

    public string? GetValue(string key)
    {
        return GetChild(key)?.Value;
    }
}

internal static class SteamKeyValueParser
{
    // MARK: Publics
    // ========================================================================

    public static SteamKeyValue ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(text));
        return Parse(stream, KVSerializationFormat.KeyValues1Text);
    }

    public static SteamKeyValue ParseBinary(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using MemoryStream stream = new(data, writable: false);
        return Parse(stream, KVSerializationFormat.KeyValues1Binary);
    }

    // MARK: Privates
    // ========================================================================

    private static SteamKeyValue Parse(Stream stream, KVSerializationFormat format)
    {
        KVSerializer serializer = KVSerializer.Create(format);
        KVDocument document = serializer.Deserialize(stream);
        SteamKeyValue root = new();

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            Copy(document.Root, root);
            return root;
        }

        root.Children[document.Name] = Convert(document.Root);
        return root;
    }

    private static SteamKeyValue Convert(KVObject source)
    {
        SteamKeyValue target = new();
        Copy(source, target);
        return target;
    }

    private static void Copy(KVObject source, SteamKeyValue target)
    {
        if (source.IsCollection)
        {
            foreach (KeyValuePair<string, KVObject> child in source.Children)
            {
                target.Children[child.Key] = Convert(child.Value);
            }
            return;
        }

        if (source.IsArray)
        {
            for (int i = 0; i < source.Count; i++)
            {
                target.Children[i.ToString(CultureInfo.InvariantCulture)] = Convert(source[i]);
            }
            return;
        }

        target.Value = source.IsNull
            ? null
            : source.ToString(CultureInfo.InvariantCulture);
    }
}
