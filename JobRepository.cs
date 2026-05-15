using System.Text.Json;

namespace SimpleMirrorBackup;

public sealed class JobRepository
{
    public sealed class JobFolderEntry
    {
        public Guid TileId { get; set; }
        public string Path { get; set; } = string.Empty;
    }

    public sealed class JobStore
    {
        public List<BackupJob> Jobs { get; set; } = new();

        // Legacy
        public List<string> Folders { get; set; } = new();

        // Neu
        public List<BackupTile> Tiles { get; set; } = new();
        public List<JobFolderEntry> TileFolders { get; set; } = new();

        public RemoteDeviceSettings RemoteDevice { get; set; } = new();
        public UiSettings UiSettings { get; set; } = new();
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
            return NormalizeStore(new JobStore());

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
                return NormalizeStore(MigrateLegacyStore(jobs, Array.Empty<string>(), new RemoteDeviceSettings(), new UiSettings()));
            }

            var store = JsonSerializer.Deserialize<JobStore>(json, options) ?? new JobStore();
            store.Jobs ??= new List<BackupJob>();
            store.Folders ??= new List<string>();
            store.Tiles ??= new List<BackupTile>();
            store.TileFolders ??= new List<JobFolderEntry>();
            store.RemoteDevice ??= new RemoteDeviceSettings();
            store.UiSettings ??= new UiSettings();
            store.UiSettings.Buttons ??= new ButtonColorSettings();
            store.UiSettings.WindowLayout ??= new WindowLayoutSettings();
            store.UiSettings.ExpandedJobFolders ??= new List<TileFolderExpansionState>();		

            if (store.Tiles.Count == 0)
            {
                store = MigrateLegacyStore(
                    store.Jobs,
                    store.Folders,
                    store.RemoteDevice,
                    store.UiSettings);
            }

            return NormalizeStore(store);
        }
        catch
        {
            return NormalizeStore(new JobStore());
        }
    }

    public void Save(
        List<BackupJob> jobs,
        IEnumerable<BackupTile> tiles,
        IEnumerable<JobFolderEntry> tileFolders,
        RemoteDeviceSettings remoteDevice,
        UiSettings uiSettings)
    {
        var tileList = (tiles ?? Enumerable.Empty<BackupTile>())
            .Select(x => x.Clone())
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tileList.Count == 0)
            tileList.Add(new BackupTile { Title = "Standard", Order = 0 });

        for (var i = 0; i < tileList.Count; i++)
            tileList[i].Order = i;

        var fallbackTileId = tileList[0].Id;
        var validTileIds = tileList.Select(x => x.Id).ToHashSet();

        foreach (var job in jobs)
        {
            if (job.Id == Guid.Empty)
                job.Id = Guid.NewGuid();

            if (job.TileId == Guid.Empty || !validTileIds.Contains(job.TileId))
                job.TileId = fallbackTileId;

            job.FolderPath = NormalizeFolderPath(job.FolderPath);
            job.ExcludedRelativePaths = (job.ExcludedRelativePaths ?? new List<string>())
                .Select(NormalizeRelativePath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            job.ComparisonSelectionPreferences =
                NormalizeComparisonSelectionPreferences(job.ComparisonSelectionPreferences);
        }

        var store = new JobStore
        {
            Jobs = jobs,
            Tiles = tileList,
            TileFolders = (tileFolders ?? Enumerable.Empty<JobFolderEntry>())
                .Where(x => x is not null && validTileIds.Contains(x.TileId))
                .SelectMany(x => ExpandTileFolder(x.TileId, x.Path))
                .Concat(ExtractFolders(jobs))
                .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                .GroupBy(x => x.TileId.ToString("D") + "|" + NormalizeFolderPath(x.Path).ToUpperInvariant())
                .Select(g => new JobFolderEntry
                {
                    TileId = g.First().TileId,
                    Path = NormalizeFolderPath(g.First().Path)
                })
                .OrderBy(x => tileList.FindIndex(t => t.Id == x.TileId))
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RemoteDevice = remoteDevice?.Clone() ?? new RemoteDeviceSettings(),
            UiSettings = NormalizeUiSettings(uiSettings?.Clone() ?? new UiSettings(), tileList)
        };

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_filePath, json);
    }

    private static JobStore NormalizeStore(JobStore store)
    {
        store.Jobs ??= new List<BackupJob>();
        store.Folders ??= new List<string>();
        store.Tiles ??= new List<BackupTile>();
        store.TileFolders ??= new List<JobFolderEntry>();
        store.RemoteDevice ??= new RemoteDeviceSettings();
        store.UiSettings ??= new UiSettings();
        store.UiSettings.Buttons ??= new ButtonColorSettings();
        store.UiSettings.WindowLayout ??= new WindowLayoutSettings();
        store.UiSettings.ExpandedJobFolders ??= new List<TileFolderExpansionState>();

        var normalizedTiles = new List<BackupTile>();
        var usedIds = new HashSet<Guid>();

        foreach (var tile in store.Tiles.OrderBy(x => x.Order).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (tile is null)
                continue;

            var clone = tile.Clone();

            if (clone.Id == Guid.Empty || usedIds.Contains(clone.Id))
                clone.Id = CreateUniqueTileId(usedIds);

            usedIds.Add(clone.Id);
            clone.Title = string.IsNullOrWhiteSpace(clone.Title) ? "Standard" : clone.Title.Trim();
            normalizedTiles.Add(clone);
        }

        if (normalizedTiles.Count == 0)
            normalizedTiles.Add(new BackupTile { Title = "Standard", Order = 0 });

        for (var i = 0; i < normalizedTiles.Count; i++)
            normalizedTiles[i].Order = i;

        store.Tiles = normalizedTiles;

        var tileIds = store.Tiles.Select(x => x.Id).ToHashSet();
        var fallbackTileId = store.Tiles[0].Id;

        foreach (var job in store.Jobs)
        {
            if (job.Id == Guid.Empty)
                job.Id = Guid.NewGuid();

            if (job.TileId == Guid.Empty || !tileIds.Contains(job.TileId))
                job.TileId = fallbackTileId;

            job.FolderPath = NormalizeFolderPath(job.FolderPath);
            job.ExcludedRelativePaths = (job.ExcludedRelativePaths ?? new List<string>())
                .Select(NormalizeRelativePath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            job.ComparisonSelectionPreferences =
                NormalizeComparisonSelectionPreferences(job.ComparisonSelectionPreferences);
        }

        store.TileFolders = store.TileFolders
            .Where(x => x is not null && tileIds.Contains(x.TileId))
            .SelectMany(x => ExpandTileFolder(x.TileId, x.Path))
            .Concat(ExtractFolders(store.Jobs))
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .GroupBy(x => x.TileId.ToString("D") + "|" + NormalizeFolderPath(x.Path).ToUpperInvariant())
            .Select(g => new JobFolderEntry
            {
                TileId = g.First().TileId,
                Path = NormalizeFolderPath(g.First().Path)
            })
            .OrderBy(x => store.Tiles.FindIndex(t => t.Id == x.TileId))
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
			
        store.UiSettings = NormalizeUiSettings(store.UiSettings, store.Tiles);

        return store;
    }

    private static JobStore MigrateLegacyStore(
        List<BackupJob> jobs,
        IEnumerable<string> legacyFolders,
        RemoteDeviceSettings remoteDevice,
        UiSettings uiSettings)
    {
        var store = new JobStore
        {
            Jobs = jobs ?? new List<BackupJob>(),
            RemoteDevice = remoteDevice?.Clone() ?? new RemoteDeviceSettings(),
            UiSettings = uiSettings?.Clone() ?? new UiSettings()
        };

        store.UiSettings.Buttons ??= new ButtonColorSettings();

        var tileMap = new Dictionary<string, BackupTile>(StringComparer.OrdinalIgnoreCase);
        var nextOrder = 0;

        BackupTile GetOrCreateTile(string? title)
        {
            var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Allgemein" : title.Trim();

            if (tileMap.TryGetValue(normalizedTitle, out var existing))
                return existing;

            var tile = new BackupTile
            {
                Title = normalizedTitle,
                Order = nextOrder++
            };

            tileMap.Add(normalizedTitle, tile);
            store.Tiles.Add(tile);
            return tile;
        }

        var legacyFolderList = (legacyFolders ?? Enumerable.Empty<string>()).ToList();

        foreach (var path in legacyFolderList.Concat(store.Jobs.Select(x => x.FolderPath)))
        {
            var parts = SplitPath(path);
            if (parts.Length > 0)
                GetOrCreateTile(parts[0]);
        }

        if (store.Tiles.Count == 0)
            GetOrCreateTile("Standard");

        var defaultTile = store.Tiles[0];

        foreach (var job in store.Jobs)
        {
            var parts = SplitPath(job.FolderPath);

            if (parts.Length == 0)
            {
                job.TileId = defaultTile.Id;
                job.FolderPath = string.Empty;
                continue;
            }

            var tile = GetOrCreateTile(parts[0]);
            job.TileId = tile.Id;
            job.FolderPath = NormalizeFolderPath(string.Join("/", parts.Skip(1)));
        }

        var tileFolders = new List<JobFolderEntry>();

        foreach (var path in legacyFolderList)
        {
            var parts = SplitPath(path);
            if (parts.Length <= 1)
                continue;

            var tile = GetOrCreateTile(parts[0]);
            var localPath = NormalizeFolderPath(string.Join("/", parts.Skip(1)));
            tileFolders.AddRange(ExpandTileFolder(tile.Id, localPath));
        }

        store.TileFolders = tileFolders
            .Concat(ExtractFolders(store.Jobs))
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .GroupBy(x => x.TileId.ToString("D") + "|" + NormalizeFolderPath(x.Path).ToUpperInvariant())
            .Select(g => new JobFolderEntry
            {
                TileId = g.First().TileId,
                Path = NormalizeFolderPath(g.First().Path)
            })
            .OrderBy(x => store.Tiles.FindIndex(t => t.Id == x.TileId))
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return store;
    }

    private static List<JobFolderEntry> ExtractFolders(IEnumerable<BackupJob> jobs)
    {
        return jobs
            .SelectMany(job => ExpandTileFolder(job.TileId, job.FolderPath))
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .GroupBy(x => x.TileId.ToString("D") + "|" + NormalizeFolderPath(x.Path).ToUpperInvariant())
            .Select(g => new JobFolderEntry
            {
                TileId = g.First().TileId,
                Path = NormalizeFolderPath(g.First().Path)
            })
            .ToList();
    }

    private static IEnumerable<JobFolderEntry> ExpandTileFolder(Guid tileId, string? folderPath)
    {
        if (tileId == Guid.Empty)
            yield break;

        var normalized = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;

        foreach (var part in parts)
        {
            current = string.IsNullOrWhiteSpace(current) ? part : current + "/" + part;
            yield return new JobFolderEntry
            {
                TileId = tileId,
                Path = current
            };
        }
    }

    private static string[] SplitPath(string? path)
    {
        return NormalizeFolderPath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Guid CreateUniqueTileId(HashSet<Guid> usedIds)
    {
        Guid id;

        do
        {
            id = Guid.NewGuid();
        }
        while (usedIds.Contains(id));

        return id;
    }

    private static string NormalizeFolderPath(string? folderPath)
    {
        return (folderPath ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private static string NormalizeRelativePath(string? relativePath)
    {
        return (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private static string NormalizeComparisonEntryKey(string? entryKey)
    {
        return (entryKey ?? string.Empty).Trim();
    }

    private static List<ComparisonSelectionPreference> NormalizeComparisonSelectionPreferences(
        IEnumerable<ComparisonSelectionPreference>? preferences)
    {
        return (preferences ?? Enumerable.Empty<ComparisonSelectionPreference>())
            .Where(x => x is not null)
            .Select(x => new ComparisonSelectionPreference
            {
                Mode = x.Mode,
                EntryKey = NormalizeComparisonEntryKey(x.EntryKey)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.EntryKey))
            .GroupBy(x => ((int)x.Mode).ToString() + "|" + x.EntryKey.ToUpperInvariant())
            .Select(g => g.First())
            .OrderBy(x => x.Mode)
            .ThenBy(x => x.EntryKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static UiSettings NormalizeUiSettings(UiSettings uiSettings, IReadOnlyList<BackupTile> tiles)
    {
        uiSettings ??= new UiSettings();
        uiSettings.LanguageCode = string.IsNullOrWhiteSpace(uiSettings.LanguageCode)
            ? "de"
            : uiSettings.LanguageCode.Trim();
        uiSettings.Buttons ??= new ButtonColorSettings();
        uiSettings.WindowLayout ??= new WindowLayoutSettings();
        uiSettings.ExpandedJobFolders ??= new List<TileFolderExpansionState>();

        var tileIndexById = tiles
            .Select((tile, index) => new { tile.Id, index })
            .ToDictionary(x => x.Id, x => x.index);

        var validTileIds = tileIndexById.Keys.ToHashSet();

        uiSettings.ExpandedJobFolders = uiSettings.ExpandedJobFolders
            .Where(x => x is not null && validTileIds.Contains(x.TileId))
            .GroupBy(x => x.TileId)
            .Select(g => new TileFolderExpansionState
            {
                TileId = g.Key,
                ExpandedFolderPaths = g
                    .SelectMany(x => x.ExpandedFolderPaths ?? new List<string>())
                    .Select(NormalizeFolderPath)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .Where(x => x.ExpandedFolderPaths.Count > 0)
            .OrderBy(x => tileIndexById.TryGetValue(x.TileId, out var index) ? index : int.MaxValue)
            .ToList();

        return uiSettings;
    }
}