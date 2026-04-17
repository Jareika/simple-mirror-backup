namespace SimpleMirrorBackup;

public sealed class BackupService
{
    private enum EntryPresenceType
    {
        None,
        File,
        Directory
    }

    private readonly record struct ValidatedJob(
        string Source,
        string Target,
        HashSet<string> Excluded);

    public Task<BackupPlan> BuildMirrorPlanAsync(BackupJob job, CancellationToken cancellationToken = default)
        => BuildPlanAsync(job, BackupMode.Mirror, cancellationToken);

    public Task<BackupPlan> BuildSynchronizePlanAsync(BackupJob job, CancellationToken cancellationToken = default)
        => BuildPlanAsync(job, BackupMode.Synchronize, cancellationToken);

    public Task<BackupPlan> BuildBackupPlanAsync(BackupJob job, CancellationToken cancellationToken = default)
        => BuildPlanAsync(job, BackupMode.Backup, cancellationToken);

    public Task RunMirrorAsync(BackupJob job, IProgress<string> progress, CancellationToken cancellationToken = default)
        => RunAsync(job, BackupMode.Mirror, progress, cancellationToken);

    public Task RunSynchronizeAsync(BackupJob job, IProgress<string> progress, CancellationToken cancellationToken = default)
        => RunAsync(job, BackupMode.Synchronize, progress, cancellationToken);

    public Task RunBackupAsync(BackupJob job, IProgress<string> progress, CancellationToken cancellationToken = default)
        => RunAsync(job, BackupMode.Backup, progress, cancellationToken);

    private static Task<BackupPlan> BuildPlanAsync(BackupJob job, BackupMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Task.Run(() => BuildPlan(job, mode, cancellationToken), cancellationToken);
    }

    private static Task RunAsync(BackupJob job, BackupMode mode, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(progress);

        return Task.Run(() =>
        {
            var plan = BuildPlan(job, mode, cancellationToken);

            progress.Report(GetStartMessage(plan));

            foreach (var warning in plan.Warnings)
                progress.Report("Hinweis: " + warning);

            ExecutePlan(plan, progress, cancellationToken);
            progress.Report("Fertig.");
        }, cancellationToken);
    }

    private static BackupPlan BuildPlan(BackupJob job, BackupMode mode, CancellationToken cancellationToken)
    {
        var validated = ValidateJob(job);

        var plan = new BackupPlan
        {
            Mode = mode,
            SourceRoot = validated.Source,
            TargetRoot = validated.Target
        };

        switch (mode)
        {
            case BackupMode.Mirror:
                BuildOneWayPlan(validated.Source, validated.Target, validated.Excluded, plan, cancellationToken, "", deleteExtras: true);
                break;

            case BackupMode.Backup:
                BuildOneWayPlan(validated.Source, validated.Target, validated.Excluded, plan, cancellationToken, "", deleteExtras: false);
                break;

            case BackupMode.Synchronize:
                BuildSynchronizePlan(validated.Source, validated.Target, validated.Excluded, plan, cancellationToken, "");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return plan;
    }

    private static void BuildOneWayPlan(
        string sourceDir,
        string targetDir,
        HashSet<string> excluded,
        BackupPlan plan,
        CancellationToken ct,
        string relativePath,
        bool deleteExtras)
    {
        ct.ThrowIfCancellationRequested();

        if (IsExcluded(relativePath, excluded))
            return;

        if (!TryEnumerateFiles(sourceDir, plan, out var sourceFiles) ||
            !TryEnumerateDirectories(sourceDir, plan, out var sourceDirectories))
        {
            plan.Warnings.Add($"Ordner übersprungen wegen Lesefehler: {sourceDir}");
            return;
        }

        var targetFiles = new List<string>();
        var targetDirectories = new List<string>();

        if (Directory.Exists(targetDir))
        {
            if (TryEnumerateFiles(targetDir, plan, out var tmpTargetFiles))
                targetFiles = tmpTargetFiles;

            if (TryEnumerateDirectories(targetDir, plan, out var tmpTargetDirectories))
                targetDirectories = tmpTargetDirectories;
        }

        var sourceFileMap = sourceFiles.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);
        var sourceDirectoryMap = sourceDirectories.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);
        var targetFileMap = targetFiles.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);
        var targetDirectoryMap = targetDirectories.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);

        var allNames = sourceFileMap.Keys
            .Concat(sourceDirectoryMap.Keys)
            .Concat(targetFileMap.Keys)
            .Concat(targetDirectoryMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
        {
            ct.ThrowIfCancellationRequested();

            var sourceType = GetEntryPresenceType(name, sourceFileMap, sourceDirectoryMap);
            var targetType = GetEntryPresenceType(name, targetFileMap, targetDirectoryMap);
            var rel = CombineRelative(relativePath, name);

            if ((sourceType == EntryPresenceType.Directory || targetType == EntryPresenceType.Directory) &&
                IsExcluded(rel, excluded))
            {
                continue;
            }

            switch (sourceType)
            {
                case EntryPresenceType.File:
                {
                    var sourceFile = sourceFileMap[name];
                    var targetFile = Path.Combine(targetDir, name);

                    if (targetType == EntryPresenceType.Directory)
                    {
                        plan.Entries.Add(new BackupPlanEntry
                        {
                            Kind = BackupPlanEntryKind.DeleteDirectoryFromTarget,
                            RelativePath = NormalizeRelative(rel),
                            AffectedPath = targetDirectoryMap[name]
                        });
                    }

                    if (targetType != EntryPresenceType.File || NeedsCopy(sourceFile, targetFile))
                    {
                        plan.Entries.Add(new BackupPlanEntry
                        {
                            Kind = BackupPlanEntryKind.CopyToTarget,
                            RelativePath = NormalizeRelative(rel),
                            SourcePath = sourceFile,
                            DestinationPath = targetFile
                        });
                    }

                    break;
                }

                case EntryPresenceType.Directory:
                {
                    if (targetType == EntryPresenceType.File)
                    {
                        plan.Entries.Add(new BackupPlanEntry
                        {
                            Kind = BackupPlanEntryKind.DeleteFileFromTarget,
                            RelativePath = NormalizeRelative(rel),
                            AffectedPath = targetFileMap[name]
                        });
                    }

                    BuildOneWayPlan(
                        sourceDirectoryMap[name],
                        Path.Combine(targetDir, name),
                        excluded,
                        plan,
                        ct,
                        rel,
                        deleteExtras);

                    break;
                }

                case EntryPresenceType.None:
                {
                    if (!deleteExtras)
                        break;

                    if (targetType == EntryPresenceType.File)
                    {
                        plan.Entries.Add(new BackupPlanEntry
                        {
                            Kind = BackupPlanEntryKind.DeleteFileFromTarget,
                            RelativePath = NormalizeRelative(rel),
                            AffectedPath = targetFileMap[name]
                        });
                    }
                    else if (targetType == EntryPresenceType.Directory)
                    {
                        plan.Entries.Add(new BackupPlanEntry
                        {
                            Kind = BackupPlanEntryKind.DeleteDirectoryFromTarget,
                            RelativePath = NormalizeRelative(rel),
                            AffectedPath = targetDirectoryMap[name]
                        });
                    }

                    break;
                }
            }
        }
    }

    private static void BuildSynchronizePlan(
        string sourceDir,
        string targetDir,
        HashSet<string> excluded,
        BackupPlan plan,
        CancellationToken ct,
        string relativePath)
    {
        ct.ThrowIfCancellationRequested();

        if (IsExcluded(relativePath, excluded))
            return;

        var sourceExists = Directory.Exists(sourceDir);
        var targetExists = Directory.Exists(targetDir);

        var sourceFiles = new List<string>();
        var sourceDirectories = new List<string>();
        var targetFiles = new List<string>();
        var targetDirectories = new List<string>();

        if (sourceExists)
        {
            if (!TryEnumerateFiles(sourceDir, plan, out sourceFiles) ||
                !TryEnumerateDirectories(sourceDir, plan, out sourceDirectories))
            {
                plan.Warnings.Add($"Synchronisierung übersprungen wegen Lesefehler: {sourceDir}");
                return;
            }
        }

        if (targetExists)
        {
            if (!TryEnumerateFiles(targetDir, plan, out targetFiles) ||
                !TryEnumerateDirectories(targetDir, plan, out targetDirectories))
            {
                plan.Warnings.Add($"Synchronisierung übersprungen wegen Lesefehler: {targetDir}");
                return;
            }
        }

        var sourceFileMap = sourceFiles.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);
        var sourceDirectoryMap = sourceDirectories.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);
        var targetFileMap = targetFiles.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);
        var targetDirectoryMap = targetDirectories.ToDictionary(x => Path.GetFileName(x), x => x, StringComparer.OrdinalIgnoreCase);

        var allNames = sourceFileMap.Keys
            .Concat(sourceDirectoryMap.Keys)
            .Concat(targetFileMap.Keys)
            .Concat(targetDirectoryMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
        {
            ct.ThrowIfCancellationRequested();

            var sourceType = GetEntryPresenceType(name, sourceFileMap, sourceDirectoryMap);
            var targetType = GetEntryPresenceType(name, targetFileMap, targetDirectoryMap);
            var rel = CombineRelative(relativePath, name);

            if ((sourceType == EntryPresenceType.Directory || targetType == EntryPresenceType.Directory) &&
                IsExcluded(rel, excluded))
            {
                continue;
            }

            switch (sourceType, targetType)
            {
                case (EntryPresenceType.File, EntryPresenceType.None):
                    plan.Entries.Add(new BackupPlanEntry
                    {
                        Kind = BackupPlanEntryKind.CopyToTarget,
                        RelativePath = NormalizeRelative(rel),
                        SourcePath = sourceFileMap[name],
                        DestinationPath = Path.Combine(targetDir, name)
                    });
                    break;

                case (EntryPresenceType.None, EntryPresenceType.File):
                    plan.Entries.Add(new BackupPlanEntry
                    {
                        Kind = BackupPlanEntryKind.CopyToSource,
                        RelativePath = NormalizeRelative(rel),
                        SourcePath = targetFileMap[name],
                        DestinationPath = Path.Combine(sourceDir, name)
                    });
                    break;

                case (EntryPresenceType.File, EntryPresenceType.File):
                {
                    var sourceFile = sourceFileMap[name];
                    var targetFile = targetFileMap[name];

                    if (!FilesEqual(sourceFile, targetFile))
                    {
                        if (SourceWinsSynchronization(sourceFile, targetFile))
                        {
                            plan.Entries.Add(new BackupPlanEntry
                            {
                                Kind = BackupPlanEntryKind.CopyToTarget,
                                RelativePath = NormalizeRelative(rel),
                                SourcePath = sourceFile,
                                DestinationPath = Path.Combine(targetDir, name)
                            });
                        }
                        else
                        {
                            plan.Entries.Add(new BackupPlanEntry
                            {
                                Kind = BackupPlanEntryKind.CopyToSource,
                                RelativePath = NormalizeRelative(rel),
                                SourcePath = targetFile,
                                DestinationPath = Path.Combine(sourceDir, name)
                            });
                        }
                    }

                    break;
                }

                case (EntryPresenceType.Directory, EntryPresenceType.None):
                case (EntryPresenceType.Directory, EntryPresenceType.Directory):
                case (EntryPresenceType.None, EntryPresenceType.Directory):
                    BuildSynchronizePlan(
                        Path.Combine(sourceDir, name),
                        Path.Combine(targetDir, name),
                        excluded,
                        plan,
                        ct,
                        rel);
                    break;

                case (EntryPresenceType.File, EntryPresenceType.Directory):
                case (EntryPresenceType.Directory, EntryPresenceType.File):
                    plan.Warnings.Add(
                        $"Konflikt übersprungen: '{NormalizeRelative(rel)}' ist auf einer Seite eine Datei und auf der anderen ein Ordner.");
                    break;
            }
        }
    }

    private static EntryPresenceType GetEntryPresenceType(
        string name,
        Dictionary<string, string> files,
        Dictionary<string, string> directories)
    {
        if (files.ContainsKey(name))
            return EntryPresenceType.File;

        if (directories.ContainsKey(name))
            return EntryPresenceType.Directory;

        return EntryPresenceType.None;
    }

    private static void ExecutePlan(BackupPlan plan, IProgress<string> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(plan.TargetRoot);

        if (plan.Mode == BackupMode.Synchronize)
            Directory.CreateDirectory(plan.SourceRoot);

        foreach (var entry in plan.Entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                switch (entry.Kind)
                {
                    case BackupPlanEntryKind.CopyToTarget:
                    case BackupPlanEntryKind.CopyToSource:
                        ExecuteCopy(entry.SourcePath!, entry.DestinationPath!, progress);
                        break;

                    case BackupPlanEntryKind.DeleteFileFromTarget:
                    case BackupPlanEntryKind.DeleteFileFromSource:
                        ExecuteDeleteFile(entry.AffectedPath!, progress);
                        break;

                    case BackupPlanEntryKind.DeleteDirectoryFromTarget:
                    case BackupPlanEntryKind.DeleteDirectoryFromSource:
                        ExecuteDeleteDirectory(entry.AffectedPath!, progress);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception ex)
            {
                progress.Report($"Fehler bei '{entry.RelativePath}': {ex.Message}");
            }
        }
    }

    private static void ExecuteCopy(string sourceFile, string targetFile, IProgress<string> progress)
    {
        var sourceInfo = new FileInfo(sourceFile);
        var targetExisted = File.Exists(targetFile);

        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

        if (targetExisted)
            File.SetAttributes(targetFile, FileAttributes.Normal);

        File.Copy(sourceFile, targetFile, overwrite: true);
        File.SetLastWriteTimeUtc(targetFile, sourceInfo.LastWriteTimeUtc);

        progress.Report(targetExisted
            ? $"Aktualisiert: {targetFile}"
            : $"Kopiert: {targetFile}");
    }

    private static void ExecuteDeleteFile(string path, IProgress<string> progress)
    {
        if (!File.Exists(path))
            return;

        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        progress.Report($"Gelöscht: {path}");
    }

    private static void ExecuteDeleteDirectory(string path, IProgress<string> progress)
    {
        if (!Directory.Exists(path))
            return;

        RemoveDirectoryTree(path);
        progress.Report($"Ordner gelöscht: {path}");
    }

    private static bool NeedsCopy(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile))
            return true;

        var sourceInfo = new FileInfo(sourceFile);
        var targetInfo = new FileInfo(targetFile);

        return sourceInfo.Length != targetInfo.Length ||
               sourceInfo.LastWriteTimeUtc != targetInfo.LastWriteTimeUtc;
    }

    private static bool FilesEqual(string leftFile, string rightFile)
    {
        var leftInfo = new FileInfo(leftFile);
        var rightInfo = new FileInfo(rightFile);

        return leftInfo.Length == rightInfo.Length &&
               leftInfo.LastWriteTimeUtc == rightInfo.LastWriteTimeUtc;
    }

    private static bool SourceWinsSynchronization(string sourceFile, string targetFile)
    {
        var sourceInfo = new FileInfo(sourceFile);
        var targetInfo = new FileInfo(targetFile);

        if (sourceInfo.LastWriteTimeUtc != targetInfo.LastWriteTimeUtc)
            return sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc;

        return true;
    }

    private static ValidatedJob ValidateJob(BackupJob job)
    {
        var source = NormalizeAbsolutePath(job.SourcePath);
        var target = NormalizeAbsolutePath(job.TargetPath);

        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("Quellordner fehlt.");

        if (File.Exists(source))
            throw new InvalidOperationException("Quellpfad ist eine Datei, kein Ordner.");

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("Quellordner nicht gefunden.");

        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("Zielordner fehlt.");

        if (File.Exists(target))
            throw new InvalidOperationException("Zielpfad ist eine Datei, kein Ordner.");

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Quelle und Ziel dürfen nicht identisch sein.");

        var excluded = new HashSet<string>(
            (job.ExcludedRelativePaths ?? new List<string>())
                .Select(NormalizeRelative)
                .Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        return new ValidatedJob(source, target, excluded);
    }

    private static string GetStartMessage(BackupPlan plan)
    {
        return plan.Mode switch
        {
            BackupMode.Mirror => $"Starte Spiegelung: {plan.SourceRoot} -> {plan.TargetRoot}",
            BackupMode.Synchronize => $"Starte Synchronisierung: {plan.SourceRoot} <-> {plan.TargetRoot}",
            BackupMode.Backup => $"Starte Backup: {plan.SourceRoot} -> {plan.TargetRoot}",
            _ => "Starte Auftrag."
        };
    }

    private static void RemoveDirectoryTree(string path)
    {
        if (IsReparsePoint(path))
        {
            Directory.Delete(path, recursive: false);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            if (IsReparsePoint(dir))
            {
                Directory.Delete(dir, recursive: false);
                continue;
            }

            RemoveDirectoryTree(dir);
        }

        Directory.Delete(path, recursive: false);
    }

    private static bool TryEnumerateFiles(string path, BackupPlan plan, out List<string> files)
    {
        try
        {
            files = Directory.EnumerateFiles(path)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return true;
        }
        catch (Exception ex)
        {
            files = new List<string>();
            plan.Warnings.Add($"Dateien konnten nicht gelesen werden in '{path}': {ex.Message}");
            return false;
        }
    }

    private static bool TryEnumerateDirectories(string path, BackupPlan plan, out List<string> directories)
    {
        try
        {
            directories = Directory.EnumerateDirectories(path)
                .Where(x => !IsReparsePoint(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return true;
        }
        catch (Exception ex)
        {
            directories = new List<string>();
            plan.Warnings.Add($"Ordner konnten nicht gelesen werden in '{path}': {ex.Message}");
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExcluded(string relativePath, HashSet<string> excluded)
    {
        var rel = NormalizeRelative(relativePath);
        if (string.IsNullOrWhiteSpace(rel))
            return false;

        foreach (var excludedPath in excluded)
        {
            if (rel.Equals(excludedPath, StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith(excludedPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CombineRelative(string baseRelative, string name)
    {
        return string.IsNullOrWhiteSpace(baseRelative)
            ? NormalizeRelative(name)
            : NormalizeRelative(baseRelative + "/" + name);
    }

    private static string NormalizeRelative(string relativePath)
    {
        return (relativePath ?? string.Empty)
            .Replace('\\', '/')
            .Trim('/');
    }

    private static string NormalizeAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch
        {
            return path.Trim().TrimEnd('\\', '/');
        }
    }
}