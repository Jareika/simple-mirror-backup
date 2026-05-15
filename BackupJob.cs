using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleMirrorBackup;

public sealed class BackupJob : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private Guid _tileId = Guid.Empty;
    private string _name = "Neuer Job";
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;
    private string _folderPath = string.Empty;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public Guid TileId
    {
        get => _tileId;
        set => SetField(ref _tileId, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "Unbenannter Job" : value.Trim());
    }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetField(ref _sourcePath, value?.Trim() ?? string.Empty);
    }

    public string TargetPath
    {
        get => _targetPath;
        set => SetField(ref _targetPath, value?.Trim() ?? string.Empty);
    }

    public string FolderPath
    {
        get => _folderPath;
        set => SetField(ref _folderPath, value?.Trim() ?? string.Empty);
    }

    public List<string> ExcludedRelativePaths { get; set; } = new();
    public List<ComparisonSelectionPreference> ComparisonSelectionPreferences { get; set; } = new();

    public BackupJob Clone(bool reverseDirection = false)
    {
        return new BackupJob
        {
            Id = Guid.NewGuid(),
            TileId = TileId,
            Name = Name,
            SourcePath = reverseDirection ? TargetPath : SourcePath,
            TargetPath = reverseDirection ? SourcePath : TargetPath,
            FolderPath = FolderPath,
            ExcludedRelativePaths = ExcludedRelativePaths.ToList(),
            ComparisonSelectionPreferences = ComparisonSelectionPreferences
                .Select(x => x.Clone())
                .ToList()
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}