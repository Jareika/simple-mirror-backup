using System.Text.Json;

namespace SimpleMirrorBackup;

public sealed class JobRepository
{
    public sealed class JobStore
    {
        public List<BackupJob> Jobs { get; set; } = new();
        public List<string> Folders { get; set; } = new();
    }

    private readonly string _filePath;

    public JobRepository()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "jobs.json");
    }

    public string FilePath => _filePath;

    public JobStore Load()
    {
        if (!File.Exists(_filePath))
            return new JobStore();

        try
        {
            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var jobs = JsonSerializer.Deserialize<List<BackupJob>>(json, options) ?? new List<BackupJob>();
                return new JobStore
                {
                    Jobs = jobs,
                    Folders = ExtractFolders(jobs)
                };
            }

            var store = JsonSerializer.Deserialize<JobStore>(json, options) ?? new JobStore();
            store.Jobs ??= new List<BackupJob>();
            store.Folders ??= new List<string>();

            store.Folders = store.Folders
                .Concat(ExtractFolders(store.Jobs))
                .SelectMany(ExpandFolderPath)
                .Select(NormalizeFolderPath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return store;
        }
        catch
        {
            return new JobStore();
        }
    }

    public void Save(List<BackupJob> jobs, IEnumerable<string> folders)
    {
        var store = new JobStore
        {
            Jobs = jobs,
            Folders = folders
                .Concat(ExtractFolders(jobs))
                .SelectMany(ExpandFolderPath)
                .Select(NormalizeFolderPath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_filePath, json);
    }

    private static List<string> ExtractFolders(IEnumerable<BackupJob> jobs)
    {
        return jobs
            .Select(x => x.FolderPath)
            .SelectMany(ExpandFolderPath)
            .Select(NormalizeFolderPath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ExpandFolderPath(string? folderPath)
    {
        var normalized = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;

        foreach (var part in parts)
        {
            current = string.IsNullOrWhiteSpace(current) ? part : current + "/" + part;
            yield return current;
        }
    }

    private static string NormalizeFolderPath(string? folderPath)
    {
        return (folderPath ?? string.Empty).Replace('\\', '/').Trim('/');
    }
}