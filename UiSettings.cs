namespace SimpleMirrorBackup;

public sealed class UiSettings
{
    public string LanguageCode { get; set; } = "en";
    public ButtonColorSettings Buttons { get; set; } = new();
    public WindowLayoutSettings WindowLayout { get; set; } = new();
    public List<TileFolderExpansionState> ExpandedJobFolders { get; set; } = new();

    public UiSettings Clone()
    {
        return new UiSettings
        {
            LanguageCode = LanguageCode,
            Buttons = Buttons?.Clone() ?? new ButtonColorSettings(),
            WindowLayout = WindowLayout?.Clone() ?? new WindowLayoutSettings(),
            ExpandedJobFolders = (ExpandedJobFolders ?? new List<TileFolderExpansionState>())
                .Select(x => x.Clone())
                .ToList()
        };
    }
}

public sealed class WindowLayoutSettings
{
    public int Left { get; set; } = -1;
    public int Top { get; set; } = -1;
    public int Width { get; set; } = 1360;
    public int Height { get; set; } = 860;
    public bool Maximized { get; set; }
    public int MainSplitterDistance { get; set; } = -1;
    public int LeftPaneSplitterDistance { get; set; } = -1;

    public WindowLayoutSettings Clone()
    {
        return new WindowLayoutSettings
        {
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height,
            Maximized = Maximized,
            MainSplitterDistance = MainSplitterDistance,
            LeftPaneSplitterDistance = LeftPaneSplitterDistance
        };
    }
}

public sealed class TileFolderExpansionState
{
    public Guid TileId { get; set; }
    public List<string> ExpandedFolderPaths { get; set; } = new();

    public TileFolderExpansionState Clone()
    {
        return new TileFolderExpansionState
        {
            TileId = TileId,
            ExpandedFolderPaths = (ExpandedFolderPaths ?? new List<string>()).ToList()
        };
    }
}

public sealed class ButtonColorSettings
{
    public string New { get; set; } = string.Empty;
    public string Copy { get; set; } = string.Empty;
    public string NewFolder { get; set; } = string.Empty;
    public string NewRootFolder { get; set; } = string.Empty;
    public string CopyReverse { get; set; } = string.Empty;
    public string Rename { get; set; } = string.Empty;
    public string Delete { get; set; } = string.Empty;
    public string Compare { get; set; } = "#FFD9D9";

    public string WakeOnLan { get; set; } = "#DDEEFF";
    public string ShutdownDevice { get; set; } = "#DDEEFF";
    public string SshConsole { get; set; } = "#DDEEFF";
    public string RemoteSettings { get; set; } = "#DDEEFF";

    public string Mirror { get; set; } = "#FFF4BF";
    public string Synchronize { get; set; } = "#FFF4BF";
    public string Backup { get; set; } = "#FFF4BF";
    public string Save { get; set; } = string.Empty;

    public ButtonColorSettings Clone()
    {
        return new ButtonColorSettings
        {
            New = New,
            Copy = Copy,
            NewFolder = NewFolder,
            NewRootFolder = NewRootFolder,
            CopyReverse = CopyReverse,
            Rename = Rename,
            Delete = Delete,
            Compare = Compare,
            WakeOnLan = WakeOnLan,
            ShutdownDevice = ShutdownDevice,
            SshConsole = SshConsole,
            RemoteSettings = RemoteSettings,
            Mirror = Mirror,
            Synchronize = Synchronize,
            Backup = Backup,
            Save = Save
        };
    }
}