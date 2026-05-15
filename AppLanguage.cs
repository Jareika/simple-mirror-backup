using System.Globalization;
using System.Text.Json;

namespace SimpleMirrorBackup;

public sealed class AppLanguageInfo
{
    public string Code { get; init; } = "de";
    public string DisplayName { get; init; } = "Deutsch";
    public string FileName { get; init; } = string.Empty;
}

public static class AppLanguage
{
    private sealed class LanguageDefinition
    {
        public string Code { get; init; } = "de";
        public string DisplayName { get; init; } = "Deutsch";
        public string FileName { get; init; } = string.Empty;
        public Dictionary<string, string> Strings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object SyncRoot = new();
    private static Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private static List<AppLanguageInfo> _availableLanguages = new();

    public static string CurrentCode { get; private set; } = "en";

    public static IReadOnlyList<AppLanguageInfo> AvailableLanguages
    {
        get
        {
            lock (SyncRoot)
            {
                return _availableLanguages
                    .Select(x => new AppLanguageInfo
                    {
                        Code = x.Code,
                        DisplayName = x.DisplayName,
                        FileName = x.FileName
                    })
                    .ToList();
            }
        }
    }

    public static void Initialize(string? preferredCode = null)
    {
        lock (SyncRoot)
        {
            var languages = LoadLanguageDefinitions();

            _availableLanguages = languages
                .Select(x => new AppLanguageInfo
                {
                    Code = x.Code,
                    DisplayName = x.DisplayName,
                    FileName = x.FileName
                })
                .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var selected = SelectLanguage(languages, preferredCode)
                           ?? languages.FirstOrDefault()
                           ?? new LanguageDefinition
                           {
                               Code = "en",
                               DisplayName = "English",
                               Strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                           };

            CurrentCode = string.IsNullOrWhiteSpace(selected.Code) ? "de" : selected.Code.Trim();
            _strings = new Dictionary<string, string>(
                selected.Strings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string T(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key))
            return fallback;

        lock (SyncRoot)
        {
            if (_strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
    }

    public static string F(string key, string fallback, params object[] args)
    {
        var format = T(key, fallback);
        return string.Format(CultureInfo.CurrentCulture, format, args);
    }

    private static LanguageDefinition? SelectLanguage(IEnumerable<LanguageDefinition> languages, string? preferredCode)
    {
        var list = languages.ToList();
        if (list.Count == 0)
            return null;

        var wanted = string.IsNullOrWhiteSpace(preferredCode)
            ? CurrentCode
            : preferredCode.Trim();

        if (!string.IsNullOrWhiteSpace(wanted))
        {
            var match = list.FirstOrDefault(x =>
                string.Equals(x.Code, wanted, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match;
        }

        var english = list.FirstOrDefault(x =>
            string.Equals(x.Code, "en", StringComparison.OrdinalIgnoreCase));

        return english ?? list[0];
    }

    private static List<LanguageDefinition> LoadLanguageDefinitions()
    {
        var folderPath = Path.Combine(AppContext.BaseDirectory, "Language");
        Directory.CreateDirectory(folderPath);

        var result = new List<LanguageDefinition>();

        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                var root = document.RootElement;

                var code = GetStringProperty(root, "code");
                if (string.IsNullOrWhiteSpace(code))
                    code = Path.GetFileNameWithoutExtension(filePath);

                var displayName = GetStringProperty(root, "displayName");
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = code;

                var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (TryGetProperty(root, "strings", out var stringsElement) &&
                    stringsElement.ValueKind == JsonValueKind.Object)
                {
                    ReadStringObject(stringsElement, strings);
                }
                else
                {
                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.NameEquals("code") || property.NameEquals("displayName"))
                            continue;

                        if (property.Value.ValueKind == JsonValueKind.String)
                            strings[property.Name] = property.Value.GetString() ?? string.Empty;
                    }
                }

                result.Add(new LanguageDefinition
                {
                    Code = code!.Trim(),
                    DisplayName = displayName!.Trim(),
                    FileName = Path.GetFileName(filePath),
                    Strings = strings
                });
            }
            catch
            {
                // Ungültige Sprachdatei wird ignoriert.
            }
        }

        if (result.Count == 0)
        {
            result.Add(new LanguageDefinition
            {
                Code = "de",
                DisplayName = "Deutsch",
                FileName = "de.json",
                Strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            });
        }

        return result
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void ReadStringObject(JsonElement element, Dictionary<string, string> target)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                target[property.Name] = property.Value.GetString() ?? string.Empty;
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                target[property.Name] = property.Value.ToString();
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}