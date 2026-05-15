namespace SimpleMirrorBackup;

public sealed class ComparisonSelectionPreference
{
    public BackupMode Mode { get; set; }
    public string EntryKey { get; set; } = string.Empty;

    public ComparisonSelectionPreference Clone()
    {
        return new ComparisonSelectionPreference
        {
            Mode = Mode,
            EntryKey = EntryKey
        };
    }
}