using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace SimpleMirrorBackup;

public sealed class MainForm : Form
{
    private readonly JobRepository _repository = new();
    private readonly BackupService _backupService = new();
    private readonly BindingList<BackupJob> _jobs;
    private readonly List<BackupTile> _tiles;
    private readonly Dictionary<Guid, HashSet<string>> _foldersByTile = new();
    private readonly Dictionary<Guid, HashSet<string>> _expandedJobFoldersByTile = new();
    private Guid? _selectedTileId;

    private RemoteDeviceSettings _remoteDeviceSettings;
    private UiSettings _uiSettings;

    private readonly TreeView treeJobs = new();
    private readonly FlowLayoutPanel pnlTiles = new();
    private readonly ContextMenuStrip tileMenu = new();
    private readonly TextBox txtName = new();
    private readonly TextBox txtSource = new();
    private readonly TextBox txtTarget = new();
    private readonly Button btnBrowseSource = new();
    private readonly Button btnBrowseTarget = new();
    private readonly Button btnRefreshTree = new();
    private readonly Button btnSwapDirection = new();
    private readonly Button btnNew = new();
    private readonly Button btnCopy = new();
    private readonly Button btnCopyReverse = new();
    private readonly Button btnRename = new();
    private readonly Button btnDelete = new();
    private readonly Button btnCompare = new();
    private readonly Button btnWakeOnLan = new();
    private readonly Button btnShutdownDevice = new();
    private readonly Button btnSshConsole = new();
    private readonly Button btnRemoteSettings = new();
	private readonly Button btnNasStartupCountdown = new();
    private readonly Button btnRun = new();
    private readonly Button btnSync = new();
    private readonly Button btnBackup = new();
    private readonly Button btnNewFolder = new();
    private readonly Button btnNewRootFolder = new();
    private readonly Button btnSave = new();
    private readonly ContextMenuStrip compareMenu = new();
    private readonly TreeView treeFolders = new();
    private readonly TextBox txtLog = new();
    private readonly Label lblStatus = new();
    private readonly Label lblTilesCaption = new();
    private readonly Label lblNameCaption = new();
    private readonly Label lblSourceCaption = new();
    private readonly Label lblTargetCaption = new();
    private readonly Label lblTreeCaption = new();
    private readonly Label lblLogCaption = new();
    private readonly SplitContainer splitMain = new();
    private readonly SplitContainer splitLeftContent = new();

    private bool _updatingUi;
    private bool _handlingTreeChecks;
    private bool _suppressJobTreeExpansionStatePersistence;
    private string? _loadedTreeSourcePath;
    private CancellationTokenSource? _folderLoadCts;
    private BackupTile? _tileContextTarget;
    private BackupTileControl? _tileDragControl;
    private Point _tileDragStart;
    private readonly System.Windows.Forms.Timer _nasStartupTimer = new();
    private DateTime? _nasStartupCountdownUntilUtc;
    private bool _nasStartupReadyState;
	private bool _nasStartupOfflineState;

    private const int MinLeftPanelWidth = 360;
    private const int MinRightPanelWidth = 520;
    private const int DesiredRightPanelWidth = 560;
    private const int MinLeftTopPanelHeight = 170;
    private const int MinLeftJobTreeHeight = 160;
    private const int DefaultLeftPaneSplitterDistance = 258;

    private BackupJob? CurrentJob => treeJobs.SelectedNode?.Tag as BackupJob;
    private BackupTile? CurrentTile => _selectedTileId.HasValue
        ? _tiles.FirstOrDefault(x => x.Id == _selectedTileId.Value)
        : null;

    private static string T(string key, string fallback) => AppLanguage.T(key, fallback);
    private static string TF(string key, string fallback, params object[] args) => AppLanguage.F(key, fallback, args);

    private sealed class FolderNodeModel
    {
        public string Name { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public List<FolderNodeModel> Children { get; } = new();
    }

    public MainForm()
    {
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        TrySetWindowIcon();

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = T("App.Title", "Simple Mirror Backup");
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var store = _repository.Load();
        _jobs = new BindingList<BackupJob>(store.Jobs);
        _tiles = (store.Tiles ?? new List<BackupTile>())
            .Select(x => x.Clone())
            .OrderBy(x => x.Order)
            .ToList();

        _remoteDeviceSettings = store.RemoteDevice?.Clone() ?? new RemoteDeviceSettings();
        _uiSettings = store.UiSettings?.Clone() ?? new UiSettings();
        _uiSettings.LanguageCode = string.IsNullOrWhiteSpace(_uiSettings.LanguageCode) ? "en" : _uiSettings.LanguageCode.Trim();
        _uiSettings.Buttons ??= new ButtonColorSettings();
        _uiSettings.WindowLayout ??= new WindowLayoutSettings();
        _uiSettings.ExpandedJobFolders ??= new List<TileFolderExpansionState>();

        AppLanguage.Initialize(_uiSettings.LanguageCode);

        LoadExpandedJobFolderStateFromSettings();

        foreach (var tile in _tiles)
            _foldersByTile[tile.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderEntry in store.TileFolders ?? new List<JobRepository.JobFolderEntry>())
            EnsureFolderAndAncestors(folderEntry.TileId, folderEntry.Path);

        EnsureTileIntegrity();

        InitializeUi();
        WireEvents();
        ApplyButtonColors();
        ApplyLanguageToVisibleUi();
        UpdateRemoteActionButtons();
        ApplyWindowLayoutFromSettings();

        Shown += (_, _) => BeginInvoke((Action)ApplyDeferredWindowLayout);

        if (_jobs.Count == 0)
        {
            var firstJob = new BackupJob
            {
                Name = GetUniqueJobName(T("Main.Default.NewJobName", "Neuer Job")),
                TileId = CurrentTile?.Id ?? _tiles[0].Id
            };

            _jobs.Add(firstJob);
        }

        RefreshJobTree(selectedJobId: _jobs[0].Id);

        FormClosing += (_, _) =>
        {
            CancelFolderLoad();
            SaveAllJobs(false);
        };
    }

    private static void StylePrimaryButton(Button button)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(96, 34);
        button.Padding = new Padding(10, 4, 10, 4);
        button.Margin = new Padding(4);
    }

    private static void StyleSecondaryButton(Button button, int minWidth)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(minWidth, 32);
        button.Padding = new Padding(8, 4, 8, 4);
        button.Margin = new Padding(4);
    }

    private static Control CreateButtonGroupSpacer(int width)
    {
        return new Panel
        {
            Width = width,
            Height = 34,
            Margin = new Padding(10, 4, 10, 4)
        };
    }

    private void InitializeUi()
    {
        treeJobs.Dock = DockStyle.Fill;
        treeJobs.HideSelection = false;
        treeJobs.FullRowSelect = true;
        treeJobs.AllowDrop = true;

        pnlTiles.Dock = DockStyle.Fill;
        pnlTiles.AutoScroll = true;
        pnlTiles.WrapContents = true;
        pnlTiles.FlowDirection = FlowDirection.LeftToRight;
        pnlTiles.AllowDrop = true;
        pnlTiles.Padding = new Padding(0, 4, 0, 0);

        splitLeftContent.Dock = DockStyle.Fill;
        splitLeftContent.Orientation = Orientation.Horizontal;
        splitLeftContent.SplitterWidth = 6;

        txtName.Dock = DockStyle.Fill;
        txtName.PlaceholderText = T("Main.Placeholder.JobName", "Jobname");

        txtSource.Dock = DockStyle.Fill;
        txtSource.PlaceholderText = T("Main.Placeholder.Source", @"C:\Quelle oder \\Server\Freigabe\Quelle");

        txtTarget.Dock = DockStyle.Fill;
        txtTarget.PlaceholderText = T("Main.Placeholder.Target", @"D:\Ziel oder \\Server\Freigabe\Ziel");

        btnBrowseSource.Text = T("Main.Button.Browse", "...");
        btnBrowseSource.Width = 40;

        btnBrowseTarget.Text = T("Main.Button.Browse", "...");
        btnBrowseTarget.Width = 40;

        btnRefreshTree.Text = T("Main.Button.LoadFolders", "Ordner laden");
        btnSwapDirection.Text = T("Main.Button.SwapDirection", "Richtung tauschen");

        btnNew.Text = T("Main.Button.New", "Neu");
        btnCopy.Text = T("Main.Button.Copy", "Kopieren");
        btnCopyReverse.Text = T("Main.Button.CopyReverse", "Kopie ↔");
        btnRename.Text = T("Main.Button.Rename", "Umbenennen");
        btnDelete.Text = T("Main.Button.Delete", "Löschen");
        btnCompare.Text = T("Main.Button.Compare", "Vergleichen ▼");
        btnWakeOnLan.Text = T("Main.Button.WakeOnLan", "WoL");
        btnShutdownDevice.Text = T("Main.Button.Shutdown", "Shut Down");
        btnSshConsole.Text = T("Main.Button.Ssh", "SSH");
        btnRemoteSettings.Text = T("Main.Button.Settings", "Einstellungen");
		btnNasStartupCountdown.Text = T("Main.Button.NasCountdownReady", "NAS");
        btnRun.Text = T("Main.Button.RunMirror", "Spiegeln");
        btnSync.Text = T("Main.Button.RunSynchronize", "Synchronisieren");
        btnBackup.Text = T("Main.Button.RunBackup", "Backup");
        btnNewFolder.Text = T("Main.Button.NewFolder", "Neuer Ordner");
        btnNewRootFolder.Text = T("Main.Button.NewTile", "Neue Kachel");
        btnSave.Text = T("Main.Button.Save", "Speichern");

        StylePrimaryButton(btnNew);
        StylePrimaryButton(btnCopy);
        StylePrimaryButton(btnCopyReverse);
        StylePrimaryButton(btnRename);
        StylePrimaryButton(btnDelete);
        StylePrimaryButton(btnCompare);
        StylePrimaryButton(btnWakeOnLan);
        StylePrimaryButton(btnShutdownDevice);
        StylePrimaryButton(btnSshConsole);
        StylePrimaryButton(btnRemoteSettings);
		StylePrimaryButton(btnNasStartupCountdown);
        StylePrimaryButton(btnRun);
        StylePrimaryButton(btnSync);
        StylePrimaryButton(btnBackup);
        StylePrimaryButton(btnNewFolder);
        StylePrimaryButton(btnNewRootFolder);
        StylePrimaryButton(btnSave);
		
        btnNasStartupCountdown.Visible = false;
        btnNasStartupCountdown.Enabled = true;
        btnNasStartupCountdown.TabStop = false;
        btnNasStartupCountdown.Cursor = Cursors.Default;
        btnNasStartupCountdown.MinimumSize = new Size(88, 34);
        btnNasStartupCountdown.UseVisualStyleBackColor = false;
        btnNasStartupCountdown.FlatStyle = FlatStyle.Flat;
        btnNasStartupCountdown.FlatAppearance.BorderColor = Color.Silver;

        _nasStartupTimer.Interval = 250;

        compareMenu.ShowImageMargin = false;
        tileMenu.ShowImageMargin = false;

        StyleSecondaryButton(btnBrowseSource, 40);
        StyleSecondaryButton(btnBrowseTarget, 40);
        StyleSecondaryButton(btnRefreshTree, 120);
        StyleSecondaryButton(btnSwapDirection, 150);

        treeFolders.Dock = DockStyle.Fill;
        treeFolders.CheckBoxes = true;
        treeFolders.HideSelection = false;

        txtLog.Dock = DockStyle.Fill;
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        txtLog.ScrollBars = ScrollBars.Both;
        txtLog.WordWrap = false;

        lblStatus.AutoSize = true;
        lblStatus.Text = T("Main.Status.Ready", "Bereit.");

        splitMain.Dock = DockStyle.Fill;
        splitMain.SplitterWidth = 6;

        var leftTopLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 1,
            RowCount = 2
        };
        leftTopLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        leftTopLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = true,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0)
        };
        leftButtons.Controls.Add(btnNew);
        leftButtons.Controls.Add(btnCopy);
        leftButtons.Controls.Add(btnNewFolder);
        leftButtons.Controls.Add(btnNewRootFolder);
        leftButtons.Controls.Add(btnCopyReverse);
        leftButtons.Controls.Add(btnRename);
        leftButtons.Controls.Add(btnDelete);
        leftButtons.Controls.Add(btnCompare);
        leftButtons.Controls.Add(btnRun);
        leftButtons.Controls.Add(btnSync);
        leftButtons.Controls.Add(btnBackup);
        leftButtons.Controls.Add(CreateButtonGroupSpacer(28));
        leftButtons.Controls.Add(btnWakeOnLan);
        leftButtons.Controls.Add(btnShutdownDevice);
        leftButtons.Controls.Add(btnSshConsole);
        leftButtons.Controls.Add(btnRemoteSettings);
		leftButtons.Controls.Add(btnNasStartupCountdown);

        var tileHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        tileHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tileHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tileHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        lblTilesCaption.AutoSize = true;
        lblTilesCaption.Margin = new Padding(0, 0, 0, 6);
        lblTilesCaption.Text = T("Main.Label.Tiles", "Kacheln: filtern, per Drag & Drop sortieren, Jobs auf Kacheln ziehen zum Verschieben");

        tileHost.Controls.Add(lblTilesCaption, 0, 0);
        tileHost.Controls.Add(pnlTiles, 0, 1);

        leftTopLayout.Controls.Add(leftButtons, 0, 0);
        leftTopLayout.Controls.Add(tileHost, 0, 1);

        splitLeftContent.Panel1.Controls.Add(leftTopLayout);
        splitLeftContent.Panel2.Controls.Add(treeJobs);

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 4,
            RowCount = 7
        };

        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        lblNameCaption.Text = T("Main.Label.Name", "Name");
        lblNameCaption.AutoSize = true;
        lblNameCaption.Anchor = AnchorStyles.Left;

        lblSourceCaption.Text = T("Main.Label.Source", "Quelle");
        lblSourceCaption.AutoSize = true;
        lblSourceCaption.Anchor = AnchorStyles.Left;

        lblTargetCaption.Text = T("Main.Label.Target", "Ziel");
        lblTargetCaption.AutoSize = true;
        lblTargetCaption.Anchor = AnchorStyles.Left;

        lblTreeCaption.Text = T("Main.Label.FolderTree", "Unterordner: abgewählte Ordner werden im Lauf nicht gescannt");
        lblTreeCaption.AutoSize = true;
        lblTreeCaption.Anchor = AnchorStyles.Left;

        lblLogCaption.Text = T("Main.Label.Log", "Protokoll");
        lblLogCaption.AutoSize = true;
        lblLogCaption.Anchor = AnchorStyles.Left;

        rightLayout.Controls.Add(lblNameCaption, 0, 0);
        rightLayout.Controls.Add(txtName, 1, 0);
        rightLayout.SetColumnSpan(txtName, 3);

        rightLayout.Controls.Add(lblSourceCaption, 0, 1);
        rightLayout.Controls.Add(txtSource, 1, 1);
        rightLayout.Controls.Add(btnBrowseSource, 2, 1);
        rightLayout.Controls.Add(btnRefreshTree, 3, 1);

        rightLayout.Controls.Add(lblTargetCaption, 0, 2);
        rightLayout.Controls.Add(txtTarget, 1, 2);
        rightLayout.Controls.Add(btnBrowseTarget, 2, 2);
        rightLayout.Controls.Add(btnSwapDirection, 3, 2);

        rightLayout.Controls.Add(lblStatus, 0, 3);
        rightLayout.SetColumnSpan(lblStatus, 4);

        rightLayout.Controls.Add(lblTreeCaption, 0, 4);
        rightLayout.SetColumnSpan(lblTreeCaption, 4);

        rightLayout.Controls.Add(treeFolders, 0, 5);
        rightLayout.SetColumnSpan(treeFolders, 4);

        rightLayout.Controls.Add(lblLogCaption, 0, 6);
        rightLayout.SetColumnSpan(lblLogCaption, 4);

        var logHost = new Panel { Dock = DockStyle.Fill };
        logHost.Controls.Add(txtLog);

        rightLayout.RowCount = 8;
        rightLayout.RowStyles.Clear();
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        rightLayout.Controls.Add(logHost, 0, 7);
        rightLayout.SetColumnSpan(logHost, 4);

        splitMain.Panel1.Controls.Add(splitLeftContent);
        splitMain.Panel2.Controls.Add(rightLayout);

        Controls.Add(splitMain);
    }

    private void WireEvents()
    {
        treeJobs.AfterSelect += (_, _) => PopulateEditorFromCurrentJob();

        txtSource.TextChanged += (_, _) =>
        {
            if (_updatingUi || CurrentJob is null)
                return;

            var newSource = txtSource.Text.Trim();

            if (!PathsEquivalent(CurrentJob.SourcePath, newSource))
            {
                CancelFolderLoad();
                CurrentJob.ExcludedRelativePaths = new List<string>();
                ResetFolderTreeState(
                    string.IsNullOrWhiteSpace(newSource)
                        ? T("Main.Status.PleaseChooseSource", "Bitte Quellordner wählen.")
                        : T("Main.Status.SourceChangedReload", "Quellordner geändert. Bitte 'Ordner laden' klicken."));
            }

            CurrentJob.SourcePath = newSource;
        };

        txtTarget.TextChanged += (_, _) =>
        {
            if (_updatingUi || CurrentJob is null)
                return;

            CurrentJob.TargetPath = txtTarget.Text.Trim();
        };

        txtName.Leave += (_, _) =>
        {
            CommitNameEdit();
            SaveAllJobs(false);
        };

        txtName.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitNameEdit();
                SaveAllJobs(false);
                e.SuppressKeyPress = true;
            }
        };

        txtSource.Leave += (_, _) => SaveAllJobs(false);
        txtTarget.Leave += (_, _) => SaveAllJobs(false);

        btnBrowseSource.Click += (_, _) => ChooseFolder(txtSource);
        btnBrowseTarget.Click += (_, _) => ChooseFolder(txtTarget);
        btnRefreshTree.Click += async (_, _) => await LoadFolderTreeAsync();
        btnSwapDirection.Click += (_, _) => SwapDirection();

        btnNew.Click += (_, _) => CreateNewJob();
        btnCopy.Click += (_, _) => CopyCurrentJob(false);
        btnCopyReverse.Click += (_, _) => CopyCurrentJob(true);
        btnNewFolder.Click += (_, _) => CreateNewFolder();
        btnNewRootFolder.Click += (_, _) => CreateNewTile();
        btnRename.Click += (_, _) => RenameCurrentJob();
        btnDelete.Click += (_, _) => DeleteCurrentJob();

        btnCompare.Click += (_, _) => compareMenu.Show(btnCompare, new Point(0, btnCompare.Height));
        RebuildLocalizedMenus();

        btnWakeOnLan.Click += async (_, _) => await SendWakeOnLanAsync();
        btnShutdownDevice.Click += async (_, _) => await ShutdownRemoteDeviceAsync();
        btnSshConsole.Click += (_, _) => OpenSshConsole();
        btnRemoteSettings.Click += (_, _) => ConfigureRemoteDevice();

        btnRun.Click += async (_, _) => await RunCurrentJobAsync(BackupMode.Mirror);
        btnSync.Click += async (_, _) => await RunCurrentJobAsync(BackupMode.Synchronize);
        btnBackup.Click += async (_, _) => await RunCurrentJobAsync(BackupMode.Backup);

        treeJobs.ItemDrag += TreeJobs_ItemDrag;
        treeJobs.MouseDown += TreeJobs_MouseDown;
        treeJobs.DragEnter += TreeJobs_DragEnter;
        treeJobs.DragOver += TreeJobs_DragOver;
        treeJobs.DragDrop += TreeJobs_DragDrop;

        pnlTiles.DragEnter += Tiles_DragEnter;
        pnlTiles.DragOver += Tiles_DragOver;
        pnlTiles.DragDrop += Tiles_DragDrop;

        treeJobs.AfterExpand += TreeJobs_AfterExpand;
        treeJobs.AfterCollapse += TreeJobs_AfterCollapse;
        treeFolders.AfterCheck += TreeFolders_AfterCheck;
		_nasStartupTimer.Tick += (_, _) => UpdateNasStartupCountdownIndicator();
    }

    private void RebuildLocalizedMenus()
    {
        compareMenu.Items.Clear();
        compareMenu.Items.Add(T("Main.Menu.CompareMirror", "Spiegeln vergleichen"), null, async (_, _) => await CompareCurrentJobAsync(BackupMode.Mirror));
        compareMenu.Items.Add(T("Main.Menu.CompareSynchronize", "Synchronisieren vergleichen"), null, async (_, _) => await CompareCurrentJobAsync(BackupMode.Synchronize));
        compareMenu.Items.Add(T("Main.Menu.CompareBackup", "Backup vergleichen"), null, async (_, _) => await CompareCurrentJobAsync(BackupMode.Backup));

        tileMenu.Items.Clear();
        tileMenu.Items.Add(T("Main.Menu.TileRename", "Kachel umbenennen"), null, (_, _) => RenameContextTile());
        tileMenu.Items.Add(T("Main.Menu.TileDelete", "Kachel löschen"), null, (_, _) => DeleteContextTile());
    }

    private void ApplyLanguageToVisibleUi()
    {
        Text = T("App.Title", "Simple Mirror Backup");

        txtName.PlaceholderText = T("Main.Placeholder.JobName", "Jobname");
        txtSource.PlaceholderText = T("Main.Placeholder.Source", @"C:\Quelle oder \\Server\Freigabe\Quelle");
        txtTarget.PlaceholderText = T("Main.Placeholder.Target", @"D:\Ziel oder \\Server\Freigabe\Ziel");

        btnBrowseSource.Text = T("Main.Button.Browse", "...");
        btnBrowseTarget.Text = T("Main.Button.Browse", "...");
        btnRefreshTree.Text = T("Main.Button.LoadFolders", "Ordner laden");
        btnSwapDirection.Text = T("Main.Button.SwapDirection", "Richtung tauschen");
        btnNew.Text = T("Main.Button.New", "Neu");
        btnCopy.Text = T("Main.Button.Copy", "Kopieren");
        btnCopyReverse.Text = T("Main.Button.CopyReverse", "Kopie ↔");
        btnRename.Text = T("Main.Button.Rename", "Umbenennen");
        btnDelete.Text = T("Main.Button.Delete", "Löschen");
        btnCompare.Text = T("Main.Button.Compare", "Vergleichen ▼");
        btnWakeOnLan.Text = T("Main.Button.WakeOnLan", "WoL");
        btnShutdownDevice.Text = T("Main.Button.Shutdown", "Shut Down");
        btnSshConsole.Text = T("Main.Button.Ssh", "SSH");
        btnRemoteSettings.Text = T("Main.Button.Settings", "Einstellungen");
        btnRun.Text = T("Main.Button.RunMirror", "Spiegeln");
        btnSync.Text = T("Main.Button.RunSynchronize", "Synchronisieren");
        btnBackup.Text = T("Main.Button.RunBackup", "Backup");
        btnNewFolder.Text = T("Main.Button.NewFolder", "Neuer Ordner");
        btnNewRootFolder.Text = T("Main.Button.NewTile", "Neue Kachel");
        btnSave.Text = T("Main.Button.Save", "Speichern");

        lblTilesCaption.Text = T("Main.Label.Tiles", "Kacheln: filtern, per Drag & Drop sortieren, Jobs auf Kacheln ziehen zum Verschieben");
        lblNameCaption.Text = T("Main.Label.Name", "Name");
        lblSourceCaption.Text = T("Main.Label.Source", "Quelle");
        lblTargetCaption.Text = T("Main.Label.Target", "Ziel");
        lblTreeCaption.Text = T("Main.Label.FolderTree", "Unterordner: abgewählte Ordner werden im Lauf nicht gescannt");
        lblLogCaption.Text = T("Main.Label.Log", "Protokoll");

        RebuildLocalizedMenus();
        RefreshTileStrip();
		UpdateNasStartupCountdownIndicator();
        UpdateStatusFromCurrentContext();
    }

    private void StartNasStartupCountdown()
    {
        if (_remoteDeviceSettings.StartupDelaySeconds <= 0 || !_remoteDeviceSettings.CanSendWakeOnLan)
        {
            ResetNasStartupCountdownIndicator();
            return;
        }

        _nasStartupOfflineState = false;
        _nasStartupReadyState = false;
        _nasStartupCountdownUntilUtc = DateTime.UtcNow.AddSeconds(_remoteDeviceSettings.StartupDelaySeconds);
        _nasStartupTimer.Start();
        UpdateNasStartupCountdownIndicator();
    }
	
    private void MarkNasAsOffline()
    {
        if (!_remoteDeviceSettings.CanSendWakeOnLan)
        {
            ResetNasStartupCountdownIndicator();
            return;
        }

        _nasStartupTimer.Stop();
        _nasStartupCountdownUntilUtc = null;
        _nasStartupReadyState = false;
        _nasStartupOfflineState = true;
        UpdateNasStartupCountdownIndicator();
    }

    private void ResetNasStartupCountdownIndicator()
    {
        _nasStartupTimer.Stop();
        _nasStartupCountdownUntilUtc = null;
        _nasStartupReadyState = false;
		_nasStartupOfflineState = false;
        UpdateNasStartupCountdownIndicator();
    }

    private void UpdateNasStartupCountdownIndicator()
    {
        if (!_remoteDeviceSettings.CanSendWakeOnLan)
        {
            btnNasStartupCountdown.Visible = false;
            return;
        }

        if (_nasStartupCountdownUntilUtc.HasValue)
        {
            var remaining = _nasStartupCountdownUntilUtc.Value - DateTime.UtcNow;
            var remainingSeconds = (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));

            if (remainingSeconds <= 0)
            {
                _nasStartupTimer.Stop();
                _nasStartupCountdownUntilUtc = null;
                _nasStartupReadyState = true;
                remainingSeconds = 0;
            }
            else
            {
                btnNasStartupCountdown.Visible = true;
                btnNasStartupCountdown.BackColor = Color.FromArgb(255, 214, 214);
                btnNasStartupCountdown.ForeColor = Color.DarkRed;
                btnNasStartupCountdown.Text = TF("Main.Button.NasCountdownRunning", "NAS {0}s", remainingSeconds);
                return;
            }
        }

        if (_nasStartupReadyState)
        {
            btnNasStartupCountdown.Visible = true;
            btnNasStartupCountdown.BackColor = Color.FromArgb(217, 245, 224);
            btnNasStartupCountdown.ForeColor = Color.DarkGreen;
            btnNasStartupCountdown.Text = T("Main.Button.NasCountdownReady", "NAS");
            return;
        }
		
        if (_nasStartupOfflineState)
        {
            btnNasStartupCountdown.Visible = true;
            btnNasStartupCountdown.BackColor = Color.FromArgb(255, 214, 214);
            btnNasStartupCountdown.ForeColor = Color.DarkRed;
            btnNasStartupCountdown.Text = T("Main.Button.NasCountdownReady", "NAS");
            return;
        }

        btnNasStartupCountdown.Visible = false;
    }

    private void UpdateStatusFromCurrentContext()
    {
        var selectedNode = treeJobs.SelectedNode;
        var current = CurrentJob;

        if (current is not null)
        {
            if (string.IsNullOrWhiteSpace(current.SourcePath))
            {
                lblStatus.Text = T("Main.Status.PleaseChooseSource", "Bitte Quellordner wählen.");
                return;
            }

            if (_loadedTreeSourcePath is not null && PathsEquivalent(_loadedTreeSourcePath, current.SourcePath))
            {
                lblStatus.Text = T("Main.Status.FoldersLoaded", "Ordner geladen.");
                return;
            }

            lblStatus.Text = T("Main.Status.FolderTreeNotLoaded", "Ordnerbaum für diesen Job nicht geladen. Bitte 'Ordner laden' klicken.");
            return;
        }

        if (selectedNode?.Tag is string folderPath)
        {
            lblStatus.Text = TF("Main.Status.FolderSelected", "Ordner ausgewählt: {0}", folderPath);
            return;
        }

        if (CurrentTile is not null)
        {
            lblStatus.Text = TF("Main.Status.TileSelected", "Kachel ausgewählt: {0}", CurrentTile.Title);
            return;
        }

        lblStatus.Text = T("Main.Status.Ready", "Bereit.");
    }

    private void ApplyButtonColors()
    {
        _uiSettings ??= new UiSettings();
        _uiSettings.Buttons ??= new ButtonColorSettings();

        var buttons = _uiSettings.Buttons;

        ApplyButtonBackColor(btnNew, buttons.New);
        ApplyButtonBackColor(btnCopy, buttons.Copy);
        ApplyButtonBackColor(btnNewFolder, buttons.NewFolder);
        ApplyButtonBackColor(btnNewRootFolder, buttons.NewRootFolder);
        ApplyButtonBackColor(btnCopyReverse, buttons.CopyReverse);
        ApplyButtonBackColor(btnRename, buttons.Rename);
        ApplyButtonBackColor(btnDelete, buttons.Delete);
        ApplyButtonBackColor(btnCompare, buttons.Compare);

        ApplyButtonBackColor(btnRun, buttons.Mirror);
        ApplyButtonBackColor(btnSync, buttons.Synchronize);
        ApplyButtonBackColor(btnBackup, buttons.Backup);
        ApplyButtonBackColor(btnSave, buttons.Save);

        ApplyButtonBackColor(btnWakeOnLan, buttons.WakeOnLan);
        ApplyButtonBackColor(btnShutdownDevice, buttons.ShutdownDevice);
        ApplyButtonBackColor(btnSshConsole, buttons.SshConsole);
        ApplyButtonBackColor(btnRemoteSettings, buttons.RemoteSettings);
    }

    private static void ApplyButtonBackColor(Button button, string? colorValue)
    {
        if (TryGetConfiguredColor(colorValue, out var color))
        {
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.Silver;
            button.BackColor = color;
            button.ForeColor = SystemColors.ControlText;
        }
        else
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = true;
            button.BackColor = SystemColors.Control;
            button.ForeColor = SystemColors.ControlText;
        }
    }

    private static bool TryGetConfiguredColor(string? colorValue, out Color color)
    {
        color = Color.Empty;

        if (string.IsNullOrWhiteSpace(colorValue))
            return false;

        try
        {
            color = ColorTranslator.FromHtml(colorValue.Trim());
            return !color.IsEmpty;
        }
        catch
        {
            return false;
        }
    }

    private void LoadExpandedJobFolderStateFromSettings()
    {
        _expandedJobFoldersByTile.Clear();

        _uiSettings ??= new UiSettings();
        _uiSettings.ExpandedJobFolders ??= new List<TileFolderExpansionState>();

        foreach (var entry in _uiSettings.ExpandedJobFolders)
        {
            if (entry.TileId == Guid.Empty)
                continue;

            _expandedJobFoldersByTile[entry.TileId] = new HashSet<string>(
                (entry.ExpandedFolderPaths ?? new List<string>())
                    .Select(NormalizeFolderPath)
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private void EnsureTileIntegrity()
    {
        if (_tiles.Count == 0)
            _tiles.Add(new BackupTile { Title = "Standard", Order = 0 });

        NormalizeTileOrder();

        var fallbackTileId = _tiles.OrderBy(x => x.Order).First().Id;
        var tileIds = _tiles.Select(x => x.Id).ToHashSet();

        foreach (var tileId in _foldersByTile.Keys.Where(x => !tileIds.Contains(x)).ToList())
            _foldersByTile.Remove(tileId);

        foreach (var tileId in _expandedJobFoldersByTile.Keys.Where(x => !tileIds.Contains(x)).ToList())
            _expandedJobFoldersByTile.Remove(tileId);

        foreach (var tile in _tiles)
        {
            _foldersByTile.TryAdd(tile.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            _expandedJobFoldersByTile.TryAdd(tile.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        foreach (var job in _jobs)
        {
            if (job.TileId == Guid.Empty || !tileIds.Contains(job.TileId))
                job.TileId = fallbackTileId;

            EnsureFolderAndAncestors(job.TileId, job.FolderPath);
        }

        if (!_selectedTileId.HasValue || !tileIds.Contains(_selectedTileId.Value))
            _selectedTileId = fallbackTileId;
    }

    private void NormalizeTileOrder()
    {
        var ordered = _tiles
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
    }

    private HashSet<string> GetFolderSet(Guid tileId)
    {
        if (!_foldersByTile.TryGetValue(tileId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _foldersByTile[tileId] = set;
        }

        return set;
    }

    private HashSet<string> GetExpandedFolderSet(Guid tileId)
    {
        if (!_expandedJobFoldersByTile.TryGetValue(tileId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _expandedJobFoldersByTile[tileId] = set;
        }

        return set;
    }

    private void RefreshTileStrip()
    {
        EnsureTileIntegrity();

        pnlTiles.SuspendLayout();
        try
        {
            pnlTiles.Controls.Clear();

            foreach (var tile in _tiles.OrderBy(x => x.Order))
            {
                var tileRef = tile;
                var tileControl = new BackupTileControl
                {
                    Tag = tileRef,
                    AllowDrop = true,
                    TitleText = tileRef.Title,
                    InfoText = BuildTileInfoText(tileRef),
                    IsSelected = _selectedTileId == tileRef.Id
                };

                tileControl.Click += (_, _) => SelectTile(tileRef.Id);
                tileControl.MouseDown += TileControl_MouseDown;
                tileControl.MouseMove += TileControl_MouseMove;
                tileControl.MouseUp += TileControl_MouseUp;
                tileControl.DragEnter += TileControl_DragEnter;
                tileControl.DragOver += TileControl_DragOver;
                tileControl.DragDrop += TileControl_DragDrop;

                pnlTiles.Controls.Add(tileControl);
            }
        }
        finally
        {
            pnlTiles.ResumeLayout();
        }
    }

    private string BuildTileInfoText(BackupTile tile)
    {
        var jobCount = _jobs.Count(x => x.TileId == tile.Id);
        var folderCount = GetFolderSet(tile.Id).Count;
        return TF("Main.TileInfo", "{0} Jobs • {1} Ordner", jobCount, folderCount);
    }

    private void SelectTile(Guid tileId)
    {
        RefreshJobTree(selectedTileId: tileId, autoSelectFallback: false);
        lblStatus.Text = TF("Main.Status.TileSelected", "Kachel ausgewählt: {0}", CurrentTile?.Title ?? string.Empty);
    }

    private void CreateNewTile()
    {
        var initialName = GetUniqueTileTitle(T("Main.Default.NewTileName", "Neue Kachel"));
        var newName = PromptDialog.Show(
            this,
            T("Main.Prompt.CreateTile.Title", "Kachel anlegen"),
            T("Main.Prompt.TitleLabel", "Titel:"),
            initialName);

        if (string.IsNullOrWhiteSpace(newName))
            return;

        var title = GetUniqueTileTitle(newName.Trim());

        var tile = new BackupTile
        {
            Title = title,
            Order = _tiles.Count
        };

        _tiles.Add(tile);
        _foldersByTile.TryAdd(tile.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _expandedJobFoldersByTile.TryAdd(tile.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        RefreshJobTree(selectedTileId: tile.Id, autoSelectFallback: false);
        SaveAllJobs();

        lblStatus.Text = TF("Main.Status.TileCreated", "Kachel '{0}' angelegt.", tile.Title);
    }

    private void RenameContextTile()
    {
        if (_tileContextTarget is null)
            return;

        var tile = _tileContextTarget;
        var newName = PromptDialog.Show(
            this,
            T("Main.Prompt.RenameTile.Title", "Kachel umbenennen"),
            T("Main.Prompt.TitleLabel", "Titel:"),
            tile.Title);

        if (newName is null)
            return;

        newName = newName.Trim();
        if (newName.Length == 0)
            return;

        tile.Title = GetUniqueTileTitle(newName, tile.Id);
        RefreshJobTree(selectedTileId: tile.Id, autoSelectFallback: false);
        SaveAllJobs();

        lblStatus.Text = TF("Main.Status.TileRenamed", "Kachel umbenannt: {0}", tile.Title);
    }

    private void DeleteContextTile()
    {
        if (_tileContextTarget is null)
            return;

        var tile = _tileContextTarget;
        var jobsInTile = _jobs.Count(x => x.TileId == tile.Id);
        var foldersInTile = GetFolderSet(tile.Id).Count;

        var result = MessageBox.Show(
            this,
            TF(
                "Main.Confirm.DeleteTileMessage",
                "Kachel '{0}' wirklich löschen?{1}{1}Enthaltene Jobs: {2}{1}Enthaltene Ordner: {3}",
                tile.Title,
                Environment.NewLine,
                jobsInTile,
                foldersInTile),
            T("Main.Confirm.DeleteTileTitle", "Kachel löschen"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        foreach (var job in _jobs.Where(x => x.TileId == tile.Id).ToList())
            _jobs.Remove(job);

        _foldersByTile.Remove(tile.Id);
        _expandedJobFoldersByTile.Remove(tile.Id);
        _tiles.Remove(tile);

        EnsureTileIntegrity();
        RefreshJobTree(selectedTileId: CurrentTile?.Id, autoSelectFallback: false);
        SaveAllJobs(false);

        lblStatus.Text = T("Main.Status.TileDeleted", "Kachel gelöscht.");
    }

    private void MoveTileBefore(Guid draggedTileId, Guid targetTileId)
    {
        if (draggedTileId == targetTileId)
            return;

        var ordered = _tiles.OrderBy(x => x.Order).ToList();
        var dragged = ordered.FirstOrDefault(x => x.Id == draggedTileId);
        var target = ordered.FirstOrDefault(x => x.Id == targetTileId);

        if (dragged is null || target is null)
            return;

        ordered.Remove(dragged);
        var targetIndex = ordered.IndexOf(target);
        ordered.Insert(targetIndex, dragged);

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;

        RefreshJobTree(selectedTileId: draggedTileId, autoSelectFallback: false);
        SaveAllJobs(false);
    }

    private void MoveTileToEnd(Guid draggedTileId)
    {
        var ordered = _tiles.OrderBy(x => x.Order).ToList();
        var dragged = ordered.FirstOrDefault(x => x.Id == draggedTileId);

        if (dragged is null)
            return;

        ordered.Remove(dragged);
        ordered.Add(dragged);

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;

        RefreshJobTree(selectedTileId: draggedTileId, autoSelectFallback: false);
        SaveAllJobs(false);
    }

    private void MoveJobToTile(BackupJob job, Guid targetTileId)
    {
        if (job.TileId == targetTileId)
            return;

        job.TileId = targetTileId;
        EnsureFolderAndAncestors(targetTileId, job.FolderPath);

        RefreshJobTree(selectedJobId: job.Id);
        SaveAllJobs(false);

        lblStatus.Text = TF("Main.Status.JobMovedToTile", "Job nach Kachel '{0}' verschoben.", GetTileTitle(targetTileId));
    }

    private void TileControl_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _tileDragControl = sender as BackupTileControl;
        _tileDragStart = e.Location;
    }

    private void TileControl_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_tileDragControl is null || sender != _tileDragControl || e.Button != MouseButtons.Left)
            return;

        if (Math.Abs(e.X - _tileDragStart.X) < SystemInformation.DragSize.Width / 2 &&
            Math.Abs(e.Y - _tileDragStart.Y) < SystemInformation.DragSize.Height / 2)
        {
            return;
        }

        if (_tileDragControl.Tag is BackupTile tile)
            _tileDragControl.DoDragDrop(tile, DragDropEffects.Move);

        _tileDragControl = null;
    }

    private void TileControl_MouseUp(object? sender, MouseEventArgs e)
    {
        _tileDragControl = null;

        if (e.Button != MouseButtons.Right || sender is not BackupTileControl control || control.Tag is not BackupTile tile)
            return;

        _tileContextTarget = tile;
        tileMenu.Show(Cursor.Position);
    }

    private void TileControl_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect =
            e.Data?.GetDataPresent(typeof(BackupTile)) == true ||
            e.Data?.GetDataPresent(typeof(BackupJob)) == true
                ? DragDropEffects.Move
                : DragDropEffects.None;
    }

    private void TileControl_DragOver(object? sender, DragEventArgs e)
    {
        TileControl_DragEnter(sender, e);
    }

    private void TileControl_DragDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control control || control.Tag is not BackupTile targetTile)
            return;

        if (e.Data?.GetData(typeof(BackupTile)) is BackupTile draggedTile)
        {
            MoveTileBefore(draggedTile.Id, targetTile.Id);
            return;
        }

        if (e.Data?.GetData(typeof(BackupJob)) is BackupJob job)
            MoveJobToTile(job, targetTile.Id);
    }

    private void Tiles_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(BackupTile)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void Tiles_DragOver(object? sender, DragEventArgs e)
    {
        Tiles_DragEnter(sender, e);
    }

    private void Tiles_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(BackupTile)) is BackupTile tile)
            MoveTileToEnd(tile.Id);
    }

    private void RefreshJobTree(
        Guid? selectedJobId = null,
        string? selectedFolderPath = null,
        Guid? selectedTileId = null,
        bool autoSelectFallback = true)
    {
        var visibleTileIdBeforeRefresh = _selectedTileId;
        CaptureExpandedJobFoldersForVisibleTree(visibleTileIdBeforeRefresh);
        EnsureTileIntegrity();

        if (selectedJobId.HasValue)
        {
            var selectedJob = _jobs.FirstOrDefault(x => x.Id == selectedJobId.Value);
            if (selectedJob is not null)
                selectedTileId = selectedJob.TileId;
        }

        if (selectedTileId.HasValue)
            _selectedTileId = selectedTileId;

        EnsureTileIntegrity();
        RefreshTileStrip();

        var currentTile = CurrentTile;
        if (currentTile is null)
        {
            treeJobs.Nodes.Clear();
            PopulateEditorFromCurrentJob();
            return;
        }

        selectedFolderPath ??= treeJobs.SelectedNode?.Tag as string;

        _suppressJobTreeExpansionStatePersistence = true;
        try
        {
            treeJobs.BeginUpdate();
            try
            {
                treeJobs.Nodes.Clear();

                var folderMap = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
                var folderSet = GetFolderSet(currentTile.Id);

                foreach (var folderPath in folderSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    GetOrCreateFolderNode(folderMap, folderPath);

                foreach (var job in _jobs
                    .Where(x => x.TileId == currentTile.Id)
                    .OrderBy(x => NormalizeFolderPath(x.FolderPath), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var folderPath = NormalizeFolderPath(job.FolderPath);
                    var targetNodes = string.IsNullOrWhiteSpace(folderPath)
                        ? treeJobs.Nodes
                        : GetOrCreateFolderNode(folderMap, folderPath).Nodes;

                    targetNodes.Add(new TreeNode(job.Name)
                    {
                        Name = "job:" + job.Id.ToString("D"),
                        Tag = job
                    });
                }
            }
            finally
            {
                treeJobs.EndUpdate();
            }

            RestoreExpandedJobFoldersForTile(currentTile.Id);

            if (selectedJobId.HasValue && SelectJobNode(selectedJobId.Value))
                return;

            if (!string.IsNullOrWhiteSpace(selectedFolderPath) && SelectFolderNode(selectedFolderPath))
                return;

            if (autoSelectFallback)
            {
                var firstJob = _jobs
                    .Where(x => x.TileId == currentTile.Id)
                    .OrderBy(x => NormalizeFolderPath(x.FolderPath), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (firstJob is not null && SelectJobNode(firstJob.Id))
                    return;

                if (treeJobs.Nodes.Count > 0)
                {
                    treeJobs.SelectedNode = treeJobs.Nodes[0];
                    treeJobs.Nodes[0].EnsureVisible();
                    return;
                }
            }

            treeJobs.SelectedNode = null;
            PopulateEditorFromCurrentJob();
        }
        finally
        {
            _suppressJobTreeExpansionStatePersistence = false;
        }
    }

    private void CaptureExpandedJobFoldersForVisibleTree(Guid? tileId)
    {
        if (!tileId.HasValue || tileId.Value == Guid.Empty || treeJobs.Nodes.Count == 0)
            return;

        var set = GetExpandedFolderSet(tileId.Value);
        set.Clear();

        foreach (TreeNode node in treeJobs.Nodes)
            CaptureExpandedFolderNode(node, set);
    }

    private static void CaptureExpandedFolderNode(TreeNode node, HashSet<string> expandedPaths)
    {
        if (node.Tag is string folderPath)
        {
            if (!node.IsExpanded)
                return;

            expandedPaths.Add(NormalizeFolderPath(folderPath));
        }

        foreach (TreeNode child in node.Nodes)
            CaptureExpandedFolderNode(child, expandedPaths);
    }

    private void TreeJobs_AfterExpand(object? sender, TreeViewEventArgs e)
    {
        PersistExpandedFolderStateChange(e.Node, isExpanded: true);
    }

    private void TreeJobs_AfterCollapse(object? sender, TreeViewEventArgs e)
    {
        PersistExpandedFolderStateChange(e.Node, isExpanded: false);
    }

    private void PersistExpandedFolderStateChange(TreeNode? node, bool isExpanded)
    {
        if (_suppressJobTreeExpansionStatePersistence)
            return;

        var currentTile = CurrentTile;
        if (currentTile is null || node?.Tag is not string folderPath)
            return;

        folderPath = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        var expandedSet = GetExpandedFolderSet(currentTile.Id);
        var changed = false;

        if (isExpanded)
        {
            changed = expandedSet.Add(folderPath);
        }
        else
        {
            var pathsToRemove = expandedSet
                .Where(x => IsSameOrChildFolder(x, folderPath))
                .ToList();

            if (pathsToRemove.Count > 0)
            {
                changed = true;

                foreach (var path in pathsToRemove)
                    expandedSet.Remove(path);
            }
        }

        if (changed)
            SaveAllJobs(false);
    }

    private void RestoreExpandedJobFoldersForTile(Guid tileId)
    {
        if (!_expandedJobFoldersByTile.TryGetValue(tileId, out var expandedPaths) || expandedPaths.Count == 0)
            return;

        foreach (var folderPath in expandedPaths
                     .OrderBy(x => x.Count(c => c == '/'))
                     .ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var nodes = treeJobs.Nodes.Find("folder:" + NormalizeFolderPath(folderPath), true);
            if (nodes.Length > 0)
                ExpandNodeWithAncestors(nodes[0]);
        }
    }

    private static void ExpandNodeWithAncestors(TreeNode node)
    {
        var stack = new Stack<TreeNode>();
        TreeNode? current = node;

        while (current is not null)
        {
            stack.Push(current);
            current = current.Parent;
        }

        while (stack.Count > 0)
            stack.Pop().Expand();
    }

    private TreeNode GetOrCreateFolderNode(Dictionary<string, TreeNode> folderMap, string folderPath)
    {
        folderPath = NormalizeFolderPath(folderPath);

        if (folderMap.TryGetValue(folderPath, out var existing))
            return existing;

        var parentPath = GetParentFolderPath(folderPath);
        var targetNodes = string.IsNullOrWhiteSpace(parentPath)
            ? treeJobs.Nodes
            : GetOrCreateFolderNode(folderMap, parentPath).Nodes;

        var node = new TreeNode(GetFolderLeafName(folderPath))
        {
            Name = "folder:" + folderPath,
            Tag = folderPath
        };

        folderMap.Add(folderPath, node);
        targetNodes.Add(node);
        return node;
    }

    private bool SelectJobNode(Guid jobId)
    {
        var nodes = treeJobs.Nodes.Find("job:" + jobId.ToString("D"), true);
        if (nodes.Length == 0)
            return false;

        treeJobs.SelectedNode = nodes[0];
        nodes[0].EnsureVisible();
        return true;
    }

    private bool SelectFolderNode(string folderPath)
    {
        folderPath = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        var nodes = treeJobs.Nodes.Find("folder:" + folderPath, true);
        if (nodes.Length == 0)
            return false;

        treeJobs.SelectedNode = nodes[0];
        nodes[0].EnsureVisible();
        return true;
    }

    private void EnsureFolderAndAncestors(Guid tileId, string? folderPath)
    {
        if (tileId == Guid.Empty)
            return;

        var set = GetFolderSet(tileId);

        foreach (var path in ExpandFolderPath(folderPath))
            set.Add(path);
    }

    private void CreateNewFolder()
    {
        if (CurrentTile is null)
            return;

        CreateFolderUnder(CurrentTile.Id, GetFolderPathForSelection(treeJobs.SelectedNode));
    }

    private void CreateFolderUnder(Guid tileId, string parentPath)
    {
        parentPath = NormalizeFolderPath(parentPath);
        var initialName = GetUniqueFolderName(tileId, parentPath, T("Main.Default.NewFolderName", "Neuer Ordner"));

        var newName = PromptDialog.Show(
            this,
            T("Main.Prompt.CreateFolder.Title", "Ordner anlegen"),
            T("Main.Prompt.FolderNameLabel", "Ordnername:"),
            initialName);

        if (string.IsNullOrWhiteSpace(newName))
            return;

        var folderPath = CombineFolderPath(parentPath, newName.Trim());
        var set = GetFolderSet(tileId);

        if (set.Contains(folderPath))
            folderPath = CombineFolderPath(parentPath, GetUniqueFolderName(tileId, parentPath, newName.Trim()));

        EnsureFolderAndAncestors(tileId, folderPath);
        RefreshJobTree(selectedFolderPath: folderPath);
        SaveAllJobs();
    }

    private void RenameSelectedFolder(string folderPath)
    {
        var tile = CurrentTile;
        if (tile is null)
            return;

        folderPath = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        var parentPath = GetParentFolderPath(folderPath);
        var currentName = GetFolderLeafName(folderPath);

        var newName = PromptDialog.Show(
            this,
            T("Main.Prompt.RenameFolder.Title", "Ordner umbenennen"),
            T("Main.Prompt.NameLabel", "Name:"),
            currentName);
        if (newName is null)
            return;

        newName = newName.Trim();
        if (newName.Length == 0)
            return;

        var set = GetFolderSet(tile.Id);
        var newPath = CombineFolderPath(parentPath, newName);

        if (!string.Equals(folderPath, newPath, StringComparison.OrdinalIgnoreCase) && set.Contains(newPath))
            newPath = CombineFolderPath(parentPath, GetUniqueFolderName(tile.Id, parentPath, newName));

        if (string.Equals(folderPath, newPath, StringComparison.OrdinalIgnoreCase))
            return;

        var affectedFolders = set
            .Where(x => IsSameOrChildFolder(x, folderPath))
            .OrderBy(x => x.Length)
            .ToList();

        foreach (var oldPath in affectedFolders)
            set.Remove(oldPath);

        foreach (var oldPath in affectedFolders)
        {
            var suffix = oldPath.Length == folderPath.Length
                ? string.Empty
                : oldPath.Substring(folderPath.Length);

            EnsureFolderAndAncestors(tile.Id, newPath + suffix);
        }

        foreach (var job in _jobs.Where(x => x.TileId == tile.Id))
        {
            var jobFolder = NormalizeFolderPath(job.FolderPath);
            if (string.Equals(jobFolder, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                job.FolderPath = newPath;
            }
            else if (jobFolder.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                job.FolderPath = newPath + jobFolder.Substring(folderPath.Length);
            }
        }

        RefreshJobTree(selectedFolderPath: newPath);
        SaveAllJobs();
    }

    private void DeleteSelectedFolder(string folderPath)
    {
        var tile = CurrentTile;
        if (tile is null)
            return;

        folderPath = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        var jobsToDelete = _jobs
            .Where(x => x.TileId == tile.Id && IsSameOrChildFolder(NormalizeFolderPath(x.FolderPath), folderPath))
            .ToList();

        var set = GetFolderSet(tile.Id);
        var foldersToDelete = set
            .Where(x => IsSameOrChildFolder(x, folderPath))
            .ToList();

        var result = MessageBox.Show(
            this,
            $"Ordner '{folderPath}' wirklich löschen?{Environment.NewLine}{Environment.NewLine}" +
            $"Enthaltene Jobs: {jobsToDelete.Count}{Environment.NewLine}" +
            $"Enthaltene Unterordner: {Math.Max(0, foldersToDelete.Count - 1)}",
            "Ordner löschen",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        foreach (var job in jobsToDelete)
            _jobs.Remove(job);

        foreach (var path in foldersToDelete)
            set.Remove(path);

        RefreshJobTree(selectedTileId: tile.Id);
        SaveAllJobs(false);
    }

    private void TreeJobs_ItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is TreeNode node && node.Tag is BackupJob job)
            DoDragDrop(job, DragDropEffects.Move);
    }

    private void TreeJobs_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(BackupJob)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void TreeJobs_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var node = treeJobs.GetNodeAt(e.Location);
        if (node is not null)
            return;

        treeJobs.SelectedNode = null;
        PopulateEditorFromCurrentJob();
    }

    private void TreeJobs_DragOver(object? sender, DragEventArgs e)
    {
        var job = e.Data?.GetData(typeof(BackupJob)) as BackupJob;
        var targetFolderPath = GetDropTargetFolderPath(new Point(e.X, e.Y));

        e.Effect = job is not null && targetFolderPath is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void TreeJobs_DragDrop(object? sender, DragEventArgs e)
    {
        var job = e.Data?.GetData(typeof(BackupJob)) as BackupJob;
        if (job is null || CurrentTile is null)
            return;

        var targetFolderPath = GetDropTargetFolderPath(new Point(e.X, e.Y));
        if (targetFolderPath is null)
            return;

        targetFolderPath = NormalizeFolderPath(targetFolderPath);

        if (job.TileId == CurrentTile.Id &&
            string.Equals(NormalizeFolderPath(job.FolderPath), targetFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            SelectJobNode(job.Id);
            return;
        }

        job.TileId = CurrentTile.Id;
        job.FolderPath = targetFolderPath;
        EnsureFolderAndAncestors(CurrentTile.Id, targetFolderPath);

        RefreshJobTree(selectedJobId: job.Id);
        SaveAllJobs(false);

        lblStatus.Text = string.IsNullOrWhiteSpace(targetFolderPath)
            ? "Job in die Kachelwurzel verschoben."
            : $"Job nach '{targetFolderPath}' verschoben.";
    }

    private string? GetDropTargetFolderPath(Point screenPoint)
    {
        var clientPoint = treeJobs.PointToClient(screenPoint);
        if (!treeJobs.ClientRectangle.Contains(clientPoint))
            return null;

        var targetNode = treeJobs.GetNodeAt(clientPoint);

        if (targetNode?.Tag is string folderPath)
            return NormalizeFolderPath(folderPath);

        if (targetNode?.Tag is BackupJob targetJob)
            return NormalizeFolderPath(targetJob.FolderPath);

        return string.Empty;
    }

    private void CancelFolderLoad()
    {
        try
        {
            _folderLoadCts?.Cancel();
        }
        catch
        {
        }
    }

    private void ResetFolderTreeState(string statusText)
    {
        treeFolders.BeginUpdate();
        try
        {
            treeFolders.Nodes.Clear();
        }
        finally
        {
            treeFolders.EndUpdate();
        }

        _loadedTreeSourcePath = null;
        lblStatus.Text = statusText;
    }

    private async Task LoadFolderTreeAsync()
    {
        CancelFolderLoad();

        var job = CurrentJob;
        if (job is null)
        {
            ResetFolderTreeState(T("Main.Status.NoJobSelected", "Kein Job ausgewählt."));
            return;
        }

        var source = job.SourcePath.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            ResetFolderTreeState(T("Main.Status.PleaseChooseSource", "Bitte Quellordner wählen."));
            return;
        }

        var normalizedSource = NormalizeAbsolutePath(source);
        var excluded = new HashSet<string>(
            job.ExcludedRelativePaths
                .Select(NormalizeRelative)
                .Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        var cts = new CancellationTokenSource();
        _folderLoadCts = cts;

        btnRefreshTree.Enabled = false;
        treeFolders.Enabled = false;
        lblStatus.Text = T("Main.Status.LoadingFolders", "Ordner werden geladen...");

        try
        {
            var model = await Task.Run(
                () => BuildFolderTreeModel(normalizedSource, cts.Token),
                cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            if (CurrentJob is null || !PathsEquivalent(CurrentJob.SourcePath, normalizedSource))
                return;

            PopulateTreeFromModel(model, excluded, normalizedSource);
            lblStatus.Text = T("Main.Status.FoldersLoaded", "Ordner geladen.");
        }
        catch (DirectoryNotFoundException)
        {
            ResetFolderTreeState(T("Main.Status.SourceNotFound", "Quellordner nicht gefunden."));
        }
        catch (OperationCanceledException)
        {
            if (CurrentJob is not null)
                lblStatus.Text = T("Main.Status.LoadCanceled", "Ordnerladen abgebrochen.");
        }
        catch (Exception ex)
        {
            ResetFolderTreeState(T("Main.Status.FolderTreeLoadFailed", "Ordnerbaum konnte nicht geladen werden."));
            AppendLog(TF("Main.Log.FolderTreeError", "Ordnerbaum-Fehler: {0}", ex.Message));
        }
        finally
        {
            var isCurrentLoad = ReferenceEquals(_folderLoadCts, cts);
            if (isCurrentLoad)
            {
                _folderLoadCts = null;
                btnRefreshTree.Enabled = CurrentJob is not null;
                treeFolders.Enabled = CurrentJob is not null;
            }

            cts.Dispose();
        }
    }

    private void ApplyWindowLayoutFromSettings()
    {
        _uiSettings ??= new UiSettings();
        _uiSettings.WindowLayout ??= new WindowLayoutSettings();

        var layout = _uiSettings.WindowLayout;
        var savedBounds = new Rectangle(layout.Left, layout.Top, layout.Width, layout.Height);

        if (HasReasonableWindowBounds(savedBounds))
        {
            StartPosition = FormStartPosition.Manual;
            DesktopBounds = savedBounds;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void ApplyDeferredWindowLayout()
    {
        _uiSettings ??= new UiSettings();
        _uiSettings.WindowLayout ??= new WindowLayoutSettings();

        var layout = _uiSettings.WindowLayout;

        if (layout.Maximized)
            WindowState = FormWindowState.Maximized;

        var mainDistance = layout.MainSplitterDistance > 0
            ? ClampMainSplitterDistance(layout.MainSplitterDistance)
            : CalculateDefaultMainSplitterDistance();

        ApplyMainSplitterDistance(mainDistance);

        var leftPaneDistance = layout.LeftPaneSplitterDistance > 0
            ? ClampLeftPaneSplitterDistance(layout.LeftPaneSplitterDistance)
            : CalculateDefaultLeftPaneSplitterDistance();

        ApplyLeftPaneSplitterDistance(leftPaneDistance);
    }

    private void CaptureUiState()
    {
        _uiSettings ??= new UiSettings();
        _uiSettings.WindowLayout ??= new WindowLayoutSettings();
        _uiSettings.ExpandedJobFolders ??= new List<TileFolderExpansionState>();

        CaptureExpandedJobFoldersForVisibleTree(_selectedTileId);
        CaptureWindowLayout();

        _uiSettings.ExpandedJobFolders = _expandedJobFoldersByTile
            .Where(x => x.Key != Guid.Empty && x.Value.Count > 0)
            .OrderBy(x => _tiles.FindIndex(t => t.Id == x.Key))
            .Select(x => new TileFolderExpansionState
            {
                TileId = x.Key,
                ExpandedFolderPaths = x.Value
                    .Select(NormalizeFolderPath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();
    }

    private void CaptureWindowLayout()
    {
        _uiSettings ??= new UiSettings();
        _uiSettings.WindowLayout ??= new WindowLayoutSettings();

        var layout = _uiSettings.WindowLayout;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            layout.Left = bounds.Left;
            layout.Top = bounds.Top;
            layout.Width = bounds.Width;
            layout.Height = bounds.Height;
        }

        layout.Maximized = WindowState == FormWindowState.Maximized;

        if (splitMain.IsHandleCreated && splitMain.ClientSize.Width > 0)
            layout.MainSplitterDistance = splitMain.SplitterDistance;

        if (splitLeftContent.IsHandleCreated && splitLeftContent.ClientSize.Height > 0)
            layout.LeftPaneSplitterDistance = splitLeftContent.SplitterDistance;
    }

    private int ClampMainSplitterDistance(int distance)
    {
        var availableWidth = splitMain.ClientSize.Width - splitMain.SplitterWidth;
        if (availableWidth <= 0)
            return distance;

        if (availableWidth < MinLeftPanelWidth + MinRightPanelWidth)
            return Math.Clamp(distance, 0, Math.Max(0, availableWidth));

        var maxLeftWidth = availableWidth - MinRightPanelWidth;
        return Math.Clamp(distance, MinLeftPanelWidth, maxLeftWidth);
    }

    private int CalculateDefaultMainSplitterDistance()
    {
        var availableWidth = splitMain.ClientSize.Width - splitMain.SplitterWidth;
        if (availableWidth <= 0)
            return splitMain.SplitterDistance;

        if (availableWidth < MinLeftPanelWidth + MinRightPanelWidth)
            return Math.Max(0, availableWidth / 2);

        var maxLeftWidth = availableWidth - MinRightPanelWidth;
        var desiredLeftWidth = availableWidth - DesiredRightPanelWidth;

        return Math.Clamp(desiredLeftWidth, MinLeftPanelWidth, maxLeftWidth);
    }

    private void ApplyMainSplitterDistance(int distance)
    {
        if (!splitMain.IsHandleCreated)
            return;

        splitMain.FixedPanel = FixedPanel.None;
        splitMain.Panel1MinSize = 0;
        splitMain.Panel2MinSize = 0;

        var availableWidth = Math.Max(0, splitMain.ClientSize.Width - splitMain.SplitterWidth);
        splitMain.SplitterDistance = Math.Clamp(distance, 0, availableWidth);

        if (availableWidth >= MinLeftPanelWidth + MinRightPanelWidth)
        {
            splitMain.Panel1MinSize = MinLeftPanelWidth;
            splitMain.Panel2MinSize = MinRightPanelWidth;
            splitMain.FixedPanel = FixedPanel.Panel2;
        }
    }

    private int ClampLeftPaneSplitterDistance(int distance)
    {
        var availableHeight = splitLeftContent.ClientSize.Height - splitLeftContent.SplitterWidth;
        if (availableHeight <= 0)
            return distance;

        if (availableHeight < MinLeftTopPanelHeight + MinLeftJobTreeHeight)
            return Math.Clamp(distance, 0, availableHeight);

        var minTop = MinLeftTopPanelHeight;
        var maxTop = availableHeight - MinLeftJobTreeHeight;

        return Math.Clamp(distance, minTop, maxTop);
    }

    private int CalculateDefaultLeftPaneSplitterDistance()
    {
        return ClampLeftPaneSplitterDistance(DefaultLeftPaneSplitterDistance);
    }

    private void ApplyLeftPaneSplitterDistance(int distance)
    {
        if (!splitLeftContent.IsHandleCreated)
            return;

        splitLeftContent.Panel1MinSize = 0;
        splitLeftContent.Panel2MinSize = 0;

        var availableHeight = Math.Max(0, splitLeftContent.ClientSize.Height - splitLeftContent.SplitterWidth);
        splitLeftContent.SplitterDistance = Math.Clamp(distance, 0, availableHeight);

        if (availableHeight >= MinLeftTopPanelHeight + MinLeftJobTreeHeight)
        {
            splitLeftContent.Panel1MinSize = MinLeftTopPanelHeight;
            splitLeftContent.Panel2MinSize = MinLeftJobTreeHeight;
        }
    }

    private void ApplyDefaultLeftPaneSplitterDistance()
    {
        ApplyLeftPaneSplitterDistance(CalculateDefaultLeftPaneSplitterDistance());
    }

    private static bool HasReasonableWindowBounds(Rectangle bounds)
    {
        if (bounds.Width < 600 || bounds.Height < 400)
            return false;

        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
    }

    private void AdjustSplitLayout()
    {
        if (!splitMain.IsHandleCreated)
            return;

        ApplyMainSplitterDistance(CalculateDefaultMainSplitterDistance());
    }

    private void CommitNameEdit()
    {
        if (_updatingUi || CurrentJob is null)
            return;

        var newName = string.IsNullOrWhiteSpace(txtName.Text)
            ? T("Main.Default.UntitledJob", "Unbenannter Job")
            : txtName.Text.Trim();

        if (string.Equals(CurrentJob.Name, newName, StringComparison.Ordinal))
            return;

        var currentJobId = CurrentJob.Id;
        CurrentJob.Name = newName;
        RefreshJobTree(selectedJobId: currentJobId);
    }

    private void PopulateEditorFromCurrentJob()
    {
        var selectedNode = treeJobs.SelectedNode;
        var job = CurrentJob;
        CancelFolderLoad();

        _updatingUi = true;
        try
        {
            var hasJob = job is not null;
            var hasSelection = selectedNode is not null;

            txtName.Enabled = hasJob;
            txtSource.Enabled = hasJob;
            txtTarget.Enabled = hasJob;
            btnBrowseSource.Enabled = hasJob;
            btnBrowseTarget.Enabled = hasJob;
            btnRefreshTree.Enabled = hasJob;
            btnSwapDirection.Enabled = hasJob;
            btnCopy.Enabled = hasJob;
            btnCopyReverse.Enabled = hasJob;
            btnCompare.Enabled = hasJob;
            btnRun.Enabled = hasJob;
            btnSync.Enabled = hasJob;
            btnBackup.Enabled = hasJob;

            btnRename.Enabled = hasSelection;
            btnDelete.Enabled = hasSelection;
            btnNewFolder.Enabled = CurrentTile is not null;

            treeFolders.Enabled = hasJob;

            if (!hasJob)
            {
                txtName.Text = "";
                txtSource.Text = "";
                txtTarget.Text = "";
                treeFolders.Enabled = false;
                treeFolders.Nodes.Clear();
                _loadedTreeSourcePath = null;

                if (selectedNode?.Tag is string folderPath)
                    lblStatus.Text = TF("Main.Status.FolderSelected", "Ordner ausgewählt: {0}", folderPath);
                else if (CurrentTile is not null)
                    lblStatus.Text = TF("Main.Status.TileSelected", "Kachel ausgewählt: {0}", CurrentTile.Title);
                else
                    lblStatus.Text = T("Main.Status.NoJobSelected", "Kein Job ausgewählt.");

                return;
            }

            txtName.Text = job!.Name;
            txtSource.Text = job.SourcePath;
            txtTarget.Text = job.TargetPath;
        }
        finally
        {
            _updatingUi = false;
        }

        if (job is null)
            return;

        ResetFolderTreeState(
            string.IsNullOrWhiteSpace(job.SourcePath)
                ? T("Main.Status.PleaseChooseSource", "Bitte Quellordner wählen.")
                : T("Main.Status.FolderTreeNotLoaded", "Ordnerbaum für diesen Job nicht geladen. Bitte 'Ordner laden' klicken."));
    }

    private void CreateNewJob()
    {
        if (CurrentTile is null)
            return;

        var job = new BackupJob
        {
            Name = GetUniqueJobName(T("Main.Default.NewJobName", "Neuer Job")),
            TileId = CurrentTile.Id,
            FolderPath = GetFolderPathForSelection(treeJobs.SelectedNode)
        };

        _jobs.Add(job);
        EnsureFolderAndAncestors(job.TileId, job.FolderPath);
        RefreshJobTree(selectedJobId: job.Id);
        SaveAllJobs(false);
    }

    private void CopyCurrentJob(bool reverseDirection)
    {
        var current = CurrentJob;
        if (current is null)
            return;

        UpdateCurrentJobFromEditor();

        if (_loadedTreeSourcePath is not null && PathsEquivalent(_loadedTreeSourcePath, current.SourcePath))
            SaveExcludedFromTree();

        var clone = current.Clone(reverseDirection);
        clone.Name = reverseDirection
            ? GetUniqueJobName(current.Name + T("Main.CopyReverseSuffix", " (Kopie rückwärts)"))
            : GetUniqueJobName(current.Name + T("Main.CopySuffix", " (Kopie)"));

        EnsureFolderAndAncestors(clone.TileId, clone.FolderPath);
        _jobs.Add(clone);
        RefreshJobTree(selectedJobId: clone.Id);
        SaveAllJobs(false);
    }

    private void RenameCurrentJob()
    {
        var current = CurrentJob;
        if (current is null)
        {
            var selectedFolderPath = treeJobs.SelectedNode?.Tag as string;
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
                RenameSelectedFolder(selectedFolderPath);

            return;
        }

        var newName = PromptDialog.Show(
            this,
            T("Main.Prompt.RenameJob.Title", "Job umbenennen"),
            T("Main.Prompt.NameLabel", "Name:"),
            current.Name);
        if (newName is null)
            return;

        newName = newName.Trim();
        if (newName.Length == 0)
            return;

        current.Name = newName;
        RefreshJobTree(selectedJobId: current.Id);
        SaveAllJobs();
    }

    private void DeleteCurrentJob()
    {
        var current = CurrentJob;
        if (current is null)
        {
            var selectedFolderPath = treeJobs.SelectedNode?.Tag as string;
            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
                DeleteSelectedFolder(selectedFolderPath);

            return;
        }

        var folderPath = NormalizeFolderPath(current.FolderPath);
        var tileId = current.TileId;

        var result = MessageBox.Show(
            this,
            TF("Main.Confirm.DeleteJobMessage", "Job '{0}' wirklich löschen?", current.Name),
            T("Main.Confirm.DeleteJobTitle", "Job löschen"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        _jobs.Remove(current);
        RefreshJobTree(selectedTileId: tileId, selectedFolderPath: folderPath);
        SaveAllJobs(false);
    }

    private void SwapDirection()
    {
        var current = CurrentJob;
        if (current is null)
            return;

        (current.SourcePath, current.TargetPath) = (current.TargetPath, current.SourcePath);

        _updatingUi = true;
        try
        {
            txtSource.Text = current.SourcePath;
            txtTarget.Text = current.TargetPath;
        }
        finally
        {
            _updatingUi = false;
        }

        CancelFolderLoad();
        ResetFolderTreeState(T("Main.Status.DirectionSwappedReload", "Richtung getauscht. Bitte 'Ordner laden' klicken."));
        SaveAllJobs();
    }

    private void ConfigureRemoteDevice()
    {
        using var form = new RemoteDeviceSettingsForm(
            _remoteDeviceSettings,
            AppLanguage.AvailableLanguages,
            _uiSettings.LanguageCode);

        if (form.ShowDialog(this) != DialogResult.OK)
            return;

        var selectedLanguageCode = string.IsNullOrWhiteSpace(form.SelectedLanguageCode)
            ? _uiSettings.LanguageCode
            : form.SelectedLanguageCode.Trim();

        _remoteDeviceSettings = form.ResultSettings.Clone();
        _uiSettings.LanguageCode = selectedLanguageCode;

        AppLanguage.Initialize(_uiSettings.LanguageCode);
        ApplyLanguageToVisibleUi();
        UpdateRemoteActionButtons();

        if (_remoteDeviceSettings.StartupDelaySeconds <= 0 || !_remoteDeviceSettings.CanSendWakeOnLan)
            ResetNasStartupCountdownIndicator();
        else
            UpdateNasStartupCountdownIndicator();

        SaveAllJobs();

        lblStatus.Text = TF("Main.Status.RemoteConfigured", "{0} konfiguriert.", GetRemoteDeviceName());
        AppendLog(TF("Main.Log.RemoteSettingsSaved", "{0}: Einstellungen gespeichert.", GetRemoteDeviceName()));
    }

    private async Task SendWakeOnLanAsync()
    {
        if (!_remoteDeviceSettings.CanSendWakeOnLan)
        {
            MessageBox.Show(
                this,
                T("Main.Remote.WolNotConfigured", "Wake-on-LAN ist noch nicht konfiguriert."),
                T("Main.Remote.TitleWol", "WoL"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ToggleRemoteActionsEnabled(false);
        UseWaitCursor = true;

        try
        {
            lblStatus.Text = TF("Main.Status.WolSending", "{0}: Wake-on-LAN wird gesendet...", GetRemoteDeviceName());
            AppendLog(TF("Main.Log.WolSending", "{0}: sende Wake-on-LAN an {1}.", GetRemoteDeviceName(), _remoteDeviceSettings.MacAddress));

            await RemoteDeviceService.SendWakeOnLanAsync(_remoteDeviceSettings);

            AppendLog(TF("Main.Log.WolSent", "{0}: Wake-on-LAN gesendet.", GetRemoteDeviceName()));
            lblStatus.Text = TF("Main.Status.WolSent", "{0}: WoL gesendet.", GetRemoteDeviceName());
			StartNasStartupCountdown();
        }
        catch (Exception ex)
        {
            AppendLog(TF("Main.Log.WolError", "{0} WoL-Fehler: {1}", GetRemoteDeviceName(), ex.Message));
            lblStatus.Text = T("Main.Status.WolFailed", "Wake-on-LAN fehlgeschlagen.");

            MessageBox.Show(
                this,
                ex.Message,
                T("Main.Remote.TitleWol", "WoL"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            ToggleRemoteActionsEnabled(true);
        }
    }

    private async Task ShutdownRemoteDeviceAsync()
    {
        if (!_remoteDeviceSettings.CanUseSsh ||
            string.IsNullOrWhiteSpace(_remoteDeviceSettings.ShutdownCommand))
        {
            MessageBox.Show(
                this,
                T("Main.Remote.ShutdownNotConfigured", "SSH oder der Shut-Down-Befehl ist noch nicht vollständig konfiguriert."),
                T("Main.Remote.TitleShutdown", "Shut Down"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            TF("Main.Remote.ConfirmShutdown", "{0} wirklich herunterfahren?", GetRemoteDeviceName()),
            T("Main.Remote.TitleShutdown", "Shut Down"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        ToggleRemoteActionsEnabled(false);
        UseWaitCursor = true;

        try
        {
            lblStatus.Text = TF("Main.Status.ShutdownTriggering", "{0}: Shut Down wird ausgelöst...", GetRemoteDeviceName());
            AppendLog(TF("Main.Log.ShutdownConnecting", "{0}: verbinde per SSH für Shut Down...", GetRemoteDeviceName()));

            var output = await RemoteDeviceService.ExecuteSshCommandAsync(
                _remoteDeviceSettings,
                _remoteDeviceSettings.ShutdownCommand);

            AppendLog(TF("Main.Log.ShutdownSent", "{0}: Shut-Down-Befehl gesendet.", GetRemoteDeviceName()));
            AppendCommandOutput(output);
			MarkNasAsOffline();

            lblStatus.Text = TF("Main.Status.ShutdownTriggered", "{0}: Shut Down ausgelöst.", GetRemoteDeviceName());
        }
        catch (Exception ex) when (LooksLikeExpectedShutdownDisconnect(ex))
        {
            AppendLog(TF("Main.Log.ShutdownDisconnectNormal", "{0}: Verbindung wurde beendet. Das ist beim Herunterfahren oft normal.", GetRemoteDeviceName()));
            MarkNasAsOffline();
			lblStatus.Text = TF("Main.Status.ShutdownProbablyTriggered", "{0}: Shut Down wahrscheinlich ausgelöst.", GetRemoteDeviceName());
        }
        catch (Exception ex)
        {
            AppendLog(TF("Main.Log.ShutdownError", "{0} Shut-Down-Fehler: {1}", GetRemoteDeviceName(), ex.Message));
            lblStatus.Text = T("Main.Status.ShutdownFailed", "Shut Down fehlgeschlagen.");

            MessageBox.Show(
                this,
                ex.Message,
                T("Main.Remote.TitleShutdown", "Shut Down"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            ToggleRemoteActionsEnabled(true);
        }
    }

    private void OpenSshConsole()
    {
        if (!_remoteDeviceSettings.CanUseSsh)
        {
            MessageBox.Show(
                this,
                T("Main.Remote.SshNotConfigured", "SSH ist noch nicht konfiguriert."),
                T("Main.Remote.TitleSsh", "SSH"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var sshExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "OpenSSH",
            "ssh.exe");

        if (!File.Exists(sshExe))
        {
            MessageBox.Show(
                this,
                T(
                    "Main.Remote.SshClientNotFound",
                    "Der Windows OpenSSH-Client (ssh.exe) wurde nicht gefunden.\r\nBitte in den optionalen Windows-Features 'OpenSSH Client' installieren."),
                T("Main.Remote.TitleSsh", "SSH"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var psCommand = BuildPowerShellSshCommand(sshExe, _remoteDeviceSettings);
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psCommand));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -EncodedCommand {encodedCommand}",
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            Process.Start(startInfo);

            lblStatus.Text = TF("Main.Status.SshOpened", "{0}: PowerShell mit SSH geöffnet.", GetRemoteDeviceName());
            AppendLog(TF(
                "Main.Log.SshStartedFor",
                "{0}: PowerShell gestartet für SSH nach {1}@{2}:{3}.",
                GetRemoteDeviceName(),
                _remoteDeviceSettings.SshUsername,
                _remoteDeviceSettings.SshHost,
                _remoteDeviceSettings.SshPort));
        }
        catch (Exception ex)
        {
            AppendLog(TF("Main.Log.SshStartError", "{0} SSH-Startfehler: {1}", GetRemoteDeviceName(), ex.Message));
            lblStatus.Text = T("Main.Status.SshStartFailed", "SSH-Start fehlgeschlagen.");

            MessageBox.Show(
                this,
                ex.Message,
                T("Main.Remote.TitleSsh", "SSH"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string BuildPowerShellSshCommand(string sshExe, RemoteDeviceSettings settings)
    {
        var host = settings.SshHost.Trim();
        var user = settings.SshUsername.Trim();
        var target = string.IsNullOrWhiteSpace(user)
            ? host
            : $"{user}@{host}";

        var escapedSshExe = sshExe.Replace("'", "''");
        var escapedTarget = target.Replace("'", "''");

        return $"& '{escapedSshExe}' -p {settings.SshPort} '{escapedTarget}'";
    }

    private void ToggleRemoteActionsEnabled(bool enabled)
    {
        btnRemoteSettings.Enabled = enabled;
        btnWakeOnLan.Enabled = enabled && _remoteDeviceSettings.CanSendWakeOnLan;
        btnShutdownDevice.Enabled = enabled &&
                                   _remoteDeviceSettings.CanUseSsh &&
                                   !string.IsNullOrWhiteSpace(_remoteDeviceSettings.ShutdownCommand);
        btnSshConsole.Enabled = enabled && _remoteDeviceSettings.CanUseSsh;
    }

    private void UpdateRemoteActionButtons()
    {
        ToggleRemoteActionsEnabled(true);
		UpdateNasStartupCountdownIndicator();
    }

    private static bool LooksLikeExpectedShutdownDisconnect(Exception ex)
    {
        var text = ex.ToString();

        return text.Contains("connection", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("aborted", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("forcibly", StringComparison.OrdinalIgnoreCase));
    }

    private string GetRemoteDeviceName()
    {
        return string.IsNullOrWhiteSpace(_remoteDeviceSettings.DisplayName)
            ? "NestDisk"
            : _remoteDeviceSettings.DisplayName.Trim();
    }

    private void AppendCommandOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        using var reader = new StringReader(output.Replace("\r\n", "\n").Replace('\r', '\n'));
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                AppendLog(line);
        }
    }

    private void ChooseFolder(TextBox targetBox)
    {
        using var dialog = new FolderBrowserDialog
        {
            ShowNewFolderButton = true,
            Description = T("Main.Dialog.ChooseFolder", "Ordner wählen")
        };

        if (Directory.Exists(targetBox.Text))
            dialog.SelectedPath = targetBox.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            targetBox.Text = dialog.SelectedPath;
    }

    private FolderNodeModel BuildFolderTreeModel(string sourcePath, CancellationToken cancellationToken)
    {
        var root = new FolderNodeModel
        {
            Name = NormalizeAbsolutePath(sourcePath),
            RelativePath = ""
        };

        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException(T("Main.Status.SourceNotFound", "Quellordner nicht gefunden."));

        BuildFolderTreeChildren(root, sourcePath, "", cancellationToken);

        return root;
    }

    private void BuildFolderTreeChildren(
        FolderNodeModel parent,
        string fullPath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<string> directories;

        try
        {
            directories = Directory.EnumerateDirectories(fullPath)
                .Where(x => !IsReparsePoint(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            AppendLog(TF("Main.Log.FolderTreeReadError", "Ordnerbaum: '{0}' konnte nicht gelesen werden: {1}", fullPath, ex.Message));
            return;
        }

        foreach (var dir in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(dir);
            var rel = CombineRelative(relativePath, name);

            var child = new FolderNodeModel
            {
                Name = name,
                RelativePath = rel
            };

            parent.Children.Add(child);
            BuildFolderTreeChildren(child, dir, rel, cancellationToken);
        }
    }

    private void PopulateTreeFromModel(FolderNodeModel model, HashSet<string> excluded, string normalizedSource)
    {
        treeFolders.BeginUpdate();
        _updatingUi = true;
        _handlingTreeChecks = true;

        try
        {
            treeFolders.Nodes.Clear();

            var root = new TreeNode(model.Name)
            {
                Tag = "",
                Checked = true
            };

            treeFolders.Nodes.Add(root);
            AddModelNodes(root, model.Children, excluded);
            root.Expand();

            _loadedTreeSourcePath = normalizedSource;
        }
        finally
        {
            treeFolders.EndUpdate();
            _handlingTreeChecks = false;
            _updatingUi = false;
        }
    }

    private void AddModelNodes(TreeNode parentNode, List<FolderNodeModel> children, HashSet<string> excluded)
    {
        foreach (var child in children)
        {
            var node = new TreeNode(child.Name)
            {
                Tag = child.RelativePath,
                Checked = !IsExcluded(child.RelativePath, excluded)
            };

            parentNode.Nodes.Add(node);
            AddModelNodes(node, child.Children, excluded);
        }
    }

    private void TreeFolders_AfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_handlingTreeChecks || _updatingUi)
            return;

        var node = e.Node;
        if (node is null)
            return;

        if (node.Parent is null && !node.Checked)
        {
            _handlingTreeChecks = true;
            try
            {
                node.Checked = true;
            }
            finally
            {
                _handlingTreeChecks = false;
            }

            return;
        }

        _handlingTreeChecks = true;
        try
        {
            SetChildNodesChecked(node, node.Checked);

            if (node.Checked)
                EnsureParentsChecked(node.Parent);

            SaveExcludedFromTree();
        }
        finally
        {
            _handlingTreeChecks = false;
        }

        SaveAllJobs(false);
    }

    private void SetChildNodesChecked(TreeNode node, bool isChecked)
    {
        foreach (TreeNode child in node.Nodes)
        {
            child.Checked = isChecked;
            SetChildNodesChecked(child, isChecked);
        }
    }

    private void EnsureParentsChecked(TreeNode? node)
    {
        while (node is not null)
        {
            if (!node.Checked)
                node.Checked = true;

            node = node.Parent;
        }
    }

    private void SaveExcludedFromTree()
    {
        var job = CurrentJob;
        if (job is null)
            return;

        var excluded = new List<string>();

        if (treeFolders.Nodes.Count > 0)
        {
            foreach (TreeNode child in treeFolders.Nodes[0].Nodes)
                CollectUncheckedNodes(child, parentChecked: true, excluded);
        }

        job.ExcludedRelativePaths = excluded;
    }

    private void CollectUncheckedNodes(TreeNode node, bool parentChecked, List<string> excluded)
    {
        if (node.Tag is not string rel)
            return;

        if (!node.Checked)
        {
            if (parentChecked && !string.IsNullOrWhiteSpace(rel))
                excluded.Add(NormalizeRelative(rel));

            return;
        }

        foreach (TreeNode child in node.Nodes)
            CollectUncheckedNodes(child, node.Checked, excluded);
    }

    private void PrepareCurrentJobForAction()
    {
        UpdateCurrentJobFromEditor();

        var current = CurrentJob;
        if (current is null)
            return;

        if (_loadedTreeSourcePath is not null && PathsEquivalent(_loadedTreeSourcePath, current.SourcePath))
            SaveExcludedFromTree();
    }

    private async Task CompareCurrentJobAsync(BackupMode mode)
    {
        var current = CurrentJob;
        if (current is null)
            return;

        PrepareCurrentJobForAction();
        SaveAllJobs(false);

        ToggleEditorEnabled(false);
        lblStatus.Text = T("Main.Status.CompareRunning", "Vergleich läuft...");

        try
        {
            var plan = await BuildPlanAsync(current, mode);
            ApplyRememberedComparisonSelections(current, plan);

            lblStatus.Text = TF(
                "Main.Status.CompareSummary",
                "{0}: {1} Kopieren, {2} Löschen.",
                GetModeDisplayName(mode),
                plan.CopyCount,
                plan.DeleteCount);

            ToggleEditorEnabled(true);

            using var preview = new PlanPreviewForm(plan);
            preview.ShowDialog(this);

            PersistRememberedComparisonSelections(current, plan);
            SaveAllJobs(false);

            if (preview.RunRequested)
                await RunPreparedPlanAsync(plan, current.Name);
        }
        catch (Exception ex)
        {
            AppendLog(T("Main.Log.ErrorPrefix", "FEHLER: ") + ex.Message);
            lblStatus.Text = T("Main.Status.CompareFailed", "Vergleich fehlgeschlagen.");

            MessageBox.Show(
                this,
                ex.Message,
                T("Main.Error.CompareTitle", "Vergleichsfehler"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            ToggleEditorEnabled(true);
        }
    }

    private void ApplyRememberedComparisonSelections(BackupJob job, BackupPlan plan)
    {
        var rememberedKeys = new HashSet<string>(
            (job.ComparisonSelectionPreferences ?? new List<ComparisonSelectionPreference>())
                .Where(x => x.Mode == plan.Mode)
                .Select(x => NormalizeComparisonEntryKey(x.EntryKey))
                .Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        if (rememberedKeys.Count == 0)
            return;

        foreach (var entry in plan.Entries)
        {
            if (!rememberedKeys.Contains(entry.EntryKey))
                continue;

            entry.IsSelected = false;
            entry.WasRememberedDeselected = true;
        }

        var reordered = plan.Entries
            .Select((entry, index) => new
            {
                Entry = entry,
                Index = index,
                SortGroup = rememberedKeys.Contains(entry.EntryKey) ? 0 : 1
            })
            .OrderBy(x => x.SortGroup)
            .ThenBy(x => x.Index)
            .Select(x => x.Entry)
            .ToList();

        plan.Entries.Clear();
        plan.Entries.AddRange(reordered);
    }

    private void PersistRememberedComparisonSelections(BackupJob job, BackupPlan plan)
    {
        job.ComparisonSelectionPreferences ??= new List<ComparisonSelectionPreference>();

        var currentPlanKeys = plan.Entries
            .Select(x => NormalizeComparisonEntryKey(x.EntryKey))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        job.ComparisonSelectionPreferences.RemoveAll(x =>
            x.Mode == plan.Mode &&
            currentPlanKeys.Contains(NormalizeComparisonEntryKey(x.EntryKey)));

        foreach (var key in plan.Entries
                     .Where(x => !x.IsSelected)
                     .Select(x => NormalizeComparisonEntryKey(x.EntryKey))
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            job.ComparisonSelectionPreferences.Add(new ComparisonSelectionPreference
            {
                Mode = plan.Mode,
                EntryKey = key
            });
        }
    }

    private static string NormalizeComparisonEntryKey(string? key)
    {
        return (key ?? string.Empty).Trim();
    }

    private async Task RunPreparedPlanAsync(BackupPlan plan, string jobName)
    {
        txtLog.Clear();
        AppendLog(TF("Main.Log.Job", "Job: {0}", jobName));
        AppendLog(TF("Main.Log.Mode", "Modus: {0}", GetModeDisplayName(plan.Mode)));
        AppendLog(TF("Main.Log.SelectedActions", "Ausgewählt: {0} von {1}", plan.SelectedCount, plan.TotalCount));

        ToggleEditorEnabled(false);
        lblStatus.Text = TF("Main.Status.ModeRunning", "{0} läuft...", GetModeDisplayName(plan.Mode));

        var progress = new Progress<string>(AppendLog);

        try
        {
            await _backupService.RunPlanAsync(plan, progress);
            lblStatus.Text = TF("Main.Status.ModeDone", "{0} fertig.", GetModeDisplayName(plan.Mode));
        }
        catch (Exception ex)
        {
            AppendLog(T("Main.Log.ErrorPrefix", "FEHLER: ") + ex.Message);
            lblStatus.Text = T("Main.Status.Error", "Fehler.");

            MessageBox.Show(
                this,
                ex.Message,
                T("Main.Error.JobTitle", "Auftragsfehler"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            ToggleEditorEnabled(true);
        }
    }

    private async Task RunCurrentJobAsync(BackupMode mode)
    {
        var current = CurrentJob;
        if (current is null)
            return;

        PrepareCurrentJobForAction();
        SaveAllJobs(false);

        txtLog.Clear();
        AppendLog(TF("Main.Log.Job", "Job: {0}", current.Name));
        AppendLog(TF("Main.Log.Mode", "Modus: {0}", GetModeDisplayName(mode)));

        ToggleEditorEnabled(false);
        lblStatus.Text = TF("Main.Status.ModeRunning", "{0} läuft...", GetModeDisplayName(mode));

        var progress = new Progress<string>(AppendLog);

        try
        {
            await RunModeAsync(current, mode, progress);
            lblStatus.Text = TF("Main.Status.ModeDone", "{0} fertig.", GetModeDisplayName(mode));
        }
        catch (Exception ex)
        {
            AppendLog(T("Main.Log.ErrorPrefix", "FEHLER: ") + ex.Message);
            lblStatus.Text = T("Main.Status.Error", "Fehler.");

            MessageBox.Show(
                this,
                ex.Message,
                T("Main.Error.JobTitle", "Auftragsfehler"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            ToggleEditorEnabled(true);
        }
    }

    private Task<BackupPlan> BuildPlanAsync(BackupJob job, BackupMode mode)
    {
        return mode switch
        {
            BackupMode.Mirror => _backupService.BuildMirrorPlanAsync(job),
            BackupMode.Synchronize => _backupService.BuildSynchronizePlanAsync(job),
            BackupMode.Backup => _backupService.BuildBackupPlanAsync(job),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private Task RunModeAsync(BackupJob job, BackupMode mode, IProgress<string> progress)
    {
        return mode switch
        {
            BackupMode.Mirror => _backupService.RunMirrorAsync(job, progress),
            BackupMode.Synchronize => _backupService.RunSynchronizeAsync(job, progress),
            BackupMode.Backup => _backupService.RunBackupAsync(job, progress),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static string GetModeDisplayName(BackupMode mode)
    {
        return mode switch
        {
            BackupMode.Mirror => AppLanguage.T("Main.Mode.Mirror", "Spiegeln"),
            BackupMode.Synchronize => AppLanguage.T("Main.Mode.Synchronize", "Synchronisieren"),
            BackupMode.Backup => AppLanguage.T("Main.Mode.Backup", "Backup"),
            _ => AppLanguage.T("Main.Mode.Job", "Auftrag")
        };
    }

    private void ToggleEditorEnabled(bool enabled)
    {
        treeJobs.Enabled = enabled;
        pnlTiles.Enabled = enabled;
        txtName.Enabled = enabled;
        txtSource.Enabled = enabled;
        txtTarget.Enabled = enabled;
        btnBrowseSource.Enabled = enabled;
        btnBrowseTarget.Enabled = enabled;
        btnRefreshTree.Enabled = enabled;
        btnSwapDirection.Enabled = enabled;
        btnNew.Enabled = enabled;
        btnCopy.Enabled = enabled;
        btnCopyReverse.Enabled = enabled;
        btnRename.Enabled = enabled;
        btnCompare.Enabled = enabled;
        btnNewFolder.Enabled = enabled;
        btnNewRootFolder.Enabled = enabled;
        btnDelete.Enabled = enabled;
        btnRun.Enabled = enabled;
        btnSync.Enabled = enabled;
        btnBackup.Enabled = enabled;
        btnSave.Enabled = enabled;
        treeFolders.Enabled = enabled;

        ToggleRemoteActionsEnabled(enabled);
        UseWaitCursor = !enabled;

        btnNasStartupCountdown.Enabled = true;
    }

    private void SaveAllJobs(bool showStatus = true)
    {
        try
        {
            UpdateCurrentJobFromEditor();

            if (_loadedTreeSourcePath is not null &&
                CurrentJob is not null &&
                PathsEquivalent(_loadedTreeSourcePath, CurrentJob.SourcePath))
            {
                SaveExcludedFromTree();
            }

            CaptureUiState();

            _repository.Save(
                _jobs.ToList(),
                _tiles,
                GetAllFolderEntries(),
                _remoteDeviceSettings,
                _uiSettings);

            if (showStatus)
                lblStatus.Text = TF("Main.Status.SavedAt", "Gespeichert {0:HH:mm:ss}", DateTime.Now);
        }
        catch (Exception ex)
        {
            AppendLog(TF("Main.Log.SaveError", "Speicherfehler: {0}", ex.Message));

            if (showStatus)
                lblStatus.Text = T("Main.Status.SaveFailed", "Speichern fehlgeschlagen.");
        }
    }

    private List<JobRepository.JobFolderEntry> GetAllFolderEntries()
    {
        var entries = new List<JobRepository.JobFolderEntry>();

        foreach (var tile in _tiles)
        {
            foreach (var path in GetFolderSet(tile.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new JobRepository.JobFolderEntry
                {
                    TileId = tile.Id,
                    Path = path
                });
            }
        }

        return entries;
    }

    private void UpdateCurrentJobFromEditor()
    {
        if (_updatingUi || CurrentJob is null)
            return;

        var currentJobId = CurrentJob.Id;
        var oldName = CurrentJob.Name;

        CurrentJob.Name = string.IsNullOrWhiteSpace(txtName.Text)
            ? T("Main.Default.UntitledJob", "Unbenannter Job")
            : txtName.Text.Trim();
        CurrentJob.SourcePath = txtSource.Text.Trim();
        CurrentJob.TargetPath = txtTarget.Text.Trim();

        if (!string.Equals(oldName, CurrentJob.Name, StringComparison.Ordinal))
            RefreshJobTree(selectedJobId: currentJobId);
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => AppendLog(message)));
            return;
        }

        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void TrySetWindowIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly
                .GetManifestResourceStream("SimpleMirrorBackup.Assets.App.ico");

            if (stream is null)
                return;

            Icon = new Icon(stream);
        }
        catch
        {
        }
    }

    private string GetUniqueJobName(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName)
            ? T("Main.Default.JobName", "Job")
            : baseName.Trim();

        if (!_jobs.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return name;

        var i = 2;
        while (_jobs.Any(x => string.Equals(x.Name, $"{name} ({i})", StringComparison.OrdinalIgnoreCase)))
            i++;

        return $"{name} ({i})";
    }

    private string GetUniqueTileTitle(string baseTitle, Guid? ignoreTileId = null)
    {
        var title = string.IsNullOrWhiteSpace(baseTitle)
            ? T("Main.Default.TileName", "Kachel")
            : baseTitle.Trim();

        bool Exists(string candidate) =>
            _tiles.Any(x =>
                (!ignoreTileId.HasValue || x.Id != ignoreTileId.Value) &&
                string.Equals(x.Title, candidate, StringComparison.OrdinalIgnoreCase));

        if (!Exists(title))
            return title;

        var i = 2;
        while (Exists($"{title} ({i})"))
            i++;

        return $"{title} ({i})";
    }

    private string GetUniqueFolderName(Guid tileId, string parentPath, string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName)
            ? T("Main.Default.FolderName", "Ordner")
            : baseName.Trim();
        var set = GetFolderSet(tileId);

        if (!set.Contains(CombineFolderPath(parentPath, name)))
            return name;

        var i = 2;
        while (set.Contains(CombineFolderPath(parentPath, $"{name} ({i})")))
            i++;

        return $"{name} ({i})";
    }

    private string GetTileTitle(Guid tileId)
    {
        return _tiles.FirstOrDefault(x => x.Id == tileId)?.Title ?? T("Main.Default.TileName", "Kachel");
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

    private static string GetFolderLeafName(string folderPath)
    {
        var normalized = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    private static string GetParentFolderPath(string folderPath)
    {
        var normalized = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var idx = normalized.LastIndexOf('/');
        return idx <= 0 ? string.Empty : normalized[..idx];
    }

    private static string CombineFolderPath(string basePath, string name)
    {
        var normalizedName = NormalizeFolderPath(name);
        if (string.IsNullOrWhiteSpace(basePath))
            return normalizedName;

        if (string.IsNullOrWhiteSpace(normalizedName))
            return NormalizeFolderPath(basePath);

        return NormalizeFolderPath(basePath + "/" + normalizedName);
    }

    private static string GetFolderPathForSelection(TreeNode? node)
    {
        return node?.Tag switch
        {
            BackupJob job => NormalizeFolderPath(job.FolderPath),
            string folderPath => NormalizeFolderPath(folderPath),
            _ => string.Empty
        };
    }

    private static bool IsSameOrChildFolder(string candidatePath, string folderPath)
    {
        candidatePath = NormalizeFolderPath(candidatePath);
        folderPath = NormalizeFolderPath(folderPath);

        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(folderPath))
            return false;

        return candidatePath.Equals(folderPath, StringComparison.OrdinalIgnoreCase) ||
               candidatePath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeRelative(string relativePath)
    {
        return (relativePath ?? string.Empty)
            .Replace('\\', '/')
            .Trim('/');
    }

    private static string NormalizeFolderPath(string? folderPath)
    {
        return (folderPath ?? string.Empty)
            .Replace('\\', '/')
            .Trim('/');
    }

    private static string CombineRelative(string baseRelative, string name)
    {
        return string.IsNullOrWhiteSpace(baseRelative)
            ? NormalizeRelative(name)
            : NormalizeRelative(baseRelative + "/" + name);
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

    private static bool PathsEquivalent(string? pathA, string? pathB)
    {
        if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
            return false;

        return string.Equals(
            NormalizeAbsolutePath(pathA),
            NormalizeAbsolutePath(pathB),
            StringComparison.OrdinalIgnoreCase);
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