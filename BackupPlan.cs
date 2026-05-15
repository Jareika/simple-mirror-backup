namespace SimpleMirrorBackup;

public enum BackupMode
{
    Mirror,
    Synchronize,
    Backup
}

public enum BackupPlanEntryKind
{
    CopyToTarget,
    CopyToSource,
    DeleteFileFromTarget,
    DeleteFileFromSource,
    DeleteDirectoryFromTarget,
    DeleteDirectoryFromSource
}

public sealed class BackupPlanEntry
{
    public BackupPlanEntryKind Kind { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string EntryKey { get; init; } = string.Empty;
    public string? SourcePath { get; init; }
    public string? DestinationPath { get; init; }
    public string? AffectedPath { get; init; }

    public bool IsSelected { get; set; } = true;
    public bool WasRememberedDeselected { get; set; }

    public bool SourceExists { get; init; }
    public bool TargetExists { get; init; }
    public bool SourceIsDirectory { get; init; }
    public bool TargetIsDirectory { get; init; }
    public DateTime? SourceLastWriteTimeUtc { get; init; }
    public DateTime? TargetLastWriteTimeUtc { get; init; }

    public bool IsCopy => Kind is BackupPlanEntryKind.CopyToTarget or BackupPlanEntryKind.CopyToSource;
    public bool IsDelete => !IsCopy;

    public string ActionText => Kind switch
    {
        BackupPlanEntryKind.CopyToTarget => AppLanguage.T("Plan.Action.CopyToTarget", "Kopieren -> Ziel"),
        BackupPlanEntryKind.CopyToSource => AppLanguage.T("Plan.Action.CopyToSource", "Kopieren -> Quelle"),
        BackupPlanEntryKind.DeleteFileFromTarget => AppLanguage.T("Plan.Action.DeleteFileFromTarget", "Datei löschen im Ziel"),
        BackupPlanEntryKind.DeleteFileFromSource => AppLanguage.T("Plan.Action.DeleteFileFromSource", "Datei löschen in Quelle"),
        BackupPlanEntryKind.DeleteDirectoryFromTarget => AppLanguage.T("Plan.Action.DeleteDirectoryFromTarget", "Ordner löschen im Ziel"),
        BackupPlanEntryKind.DeleteDirectoryFromSource => AppLanguage.T("Plan.Action.DeleteDirectoryFromSource", "Ordner löschen in Quelle"),
        _ => Kind.ToString()
    };

    public string DetailsText => Kind switch
    {
        BackupPlanEntryKind.CopyToTarget or BackupPlanEntryKind.CopyToSource
            => $"{SourcePath} -> {DestinationPath}",
        _ => AffectedPath ?? string.Empty
    };

    public string SourceStateText => FormatSide(SourceExists, SourceIsDirectory, SourceLastWriteTimeUtc);
    public string TargetStateText => FormatSide(TargetExists, TargetIsDirectory, TargetLastWriteTimeUtc);

    private static string FormatSide(bool exists, bool isDirectory, DateTime? lastWriteTimeUtc)
    {
        if (!exists)
            return AppLanguage.T("Common.Missing", "fehlt");

        var kind = isDirectory
            ? AppLanguage.T("Common.Directory", "Ordner")
            : AppLanguage.T("Common.File", "Datei");

        return lastWriteTimeUtc.HasValue
            ? AppLanguage.F("Plan.Side.WithDate", "{0:yyyy-MM-dd HH:mm:ss} ({1})", lastWriteTimeUtc.Value.ToLocalTime(), kind)
            : AppLanguage.F("Plan.Side.PresentKind", "vorhanden ({0})", kind);
    }
}

public sealed class BackupPlan
{
    public BackupMode Mode { get; init; }
    public string SourceRoot { get; init; } = string.Empty;
    public string TargetRoot { get; init; } = string.Empty;
    public List<BackupPlanEntry> Entries { get; } = new();
    public List<string> Warnings { get; } = new();

    public IEnumerable<BackupPlanEntry> SelectedEntries => Entries.Where(x => x.IsSelected);

    public int CopyCount => Entries.Count(x => x.IsCopy);
    public int DeleteCount => Entries.Count(x => x.IsDelete);
    public int TotalCount => Entries.Count;

    public int SelectedCopyCount => Entries.Count(x => x.IsSelected && x.IsCopy);
    public int SelectedDeleteCount => Entries.Count(x => x.IsSelected && x.IsDelete);
    public int SelectedCount => Entries.Count(x => x.IsSelected);
}