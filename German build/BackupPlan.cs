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
    public string? SourcePath { get; init; }
    public string? DestinationPath { get; init; }
    public string? AffectedPath { get; init; }

    public bool IsCopy => Kind is BackupPlanEntryKind.CopyToTarget or BackupPlanEntryKind.CopyToSource;
    public bool IsDelete => !IsCopy;

    public string ActionText => Kind switch
    {
        BackupPlanEntryKind.CopyToTarget => "Kopieren -> Ziel",
        BackupPlanEntryKind.CopyToSource => "Kopieren -> Quelle",
        BackupPlanEntryKind.DeleteFileFromTarget => "Datei löschen im Ziel",
        BackupPlanEntryKind.DeleteFileFromSource => "Datei löschen in Quelle",
        BackupPlanEntryKind.DeleteDirectoryFromTarget => "Ordner löschen im Ziel",
        BackupPlanEntryKind.DeleteDirectoryFromSource => "Ordner löschen in Quelle",
        _ => Kind.ToString()
    };

    public string DetailsText => Kind switch
    {
        BackupPlanEntryKind.CopyToTarget or BackupPlanEntryKind.CopyToSource
            => $"{SourcePath} -> {DestinationPath}",
        _ => AffectedPath ?? string.Empty
    };
}

public sealed class BackupPlan
{
    public BackupMode Mode { get; init; }
    public string SourceRoot { get; init; } = string.Empty;
    public string TargetRoot { get; init; } = string.Empty;
    public List<BackupPlanEntry> Entries { get; } = new();
    public List<string> Warnings { get; } = new();

    public int CopyCount => Entries.Count(x => x.IsCopy);
    public int DeleteCount => Entries.Count(x => x.IsDelete);
    public int TotalCount => Entries.Count;
}