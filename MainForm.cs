using System.ComponentModel;
using System.Drawing;

namespace SimpleMirrorBackup;

public sealed class MainForm : Form
{
    private readonly JobRepository _repository = new();
    private readonly BackupService _backupService = new();
    private readonly BindingList<BackupJob> _jobs;
    private readonly HashSet<string> _jobFolders = new(StringComparer.OrdinalIgnoreCase);

    private readonly TreeView treeJobs = new();
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
    private readonly SplitContainer splitMain = new();

    private bool _updatingUi;
    private bool _handlingTreeChecks;
    private string? _loadedTreeSourcePath;
    private CancellationTokenSource? _folderLoadCts;

    private const int MinLeftPanelWidth = 320;
    private const int MinRightPanelWidth = 460;
    private const int DesiredRightPanelWidth = 520;

    private BackupJob? CurrentJob => treeJobs.SelectedNode?.Tag as BackupJob;

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
        Text = "Simple Mirror Backup";
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(980, 650);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var store = _repository.Load();
        _jobs = new BindingList<BackupJob>(store.Jobs);

        foreach (var folderPath in store.Folders)
            EnsureFolderAndAncestors(folderPath);

        foreach (var job in _jobs)
            EnsureFolderAndAncestors(job.FolderPath);

        InitializeUi();
        WireEvents();
        Shown += (_, _) => BeginInvoke((Action)AdjustSplitLayout);

        if (_jobs.Count == 0)
        {
            var firstJob = new BackupJob
            {
                Name = GetUniqueJobName("Neuer Job")
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

    private void InitializeUi()
    {
        treeJobs.Dock = DockStyle.Fill;
        treeJobs.HideSelection = false;
        treeJobs.FullRowSelect = true;
        treeJobs.AllowDrop = true;

        txtName.Dock = DockStyle.Fill;
        txtName.PlaceholderText = "Job name";

        txtSource.Dock = DockStyle.Fill;
        txtSource.PlaceholderText = @"C:\Source or \\Server\Share\Source";

        txtTarget.Dock = DockStyle.Fill;
        txtTarget.PlaceholderText = @"D:\Target or \\Server\Share\Target";

        btnBrowseSource.Text = "...";
        btnBrowseSource.Width = 40;

        btnBrowseTarget.Text = "...";
        btnBrowseTarget.Width = 40;

        btnRefreshTree.Text = "Load folders";
        btnSwapDirection.Text = "Swap direction";

        btnNew.Text = "New";
        btnCopy.Text = "Copy";
        btnCopyReverse.Text = "Copy ↔";
        btnRename.Text = "Rename";
        btnDelete.Text = "Delete";
        btnCompare.Text = "Compare ▼";
        btnRun.Text = "Mirror";
        btnSync.Text = "Synchronisze";
        btnBackup.Text = "Backup";
        btnNewFolder.Text = "New folder";
		btnNewRootFolder.Text = "New root folder";
        btnSave.Text = "Save";

        StylePrimaryButton(btnNew);
        StylePrimaryButton(btnCopy);
        StylePrimaryButton(btnCopyReverse);
        StylePrimaryButton(btnRename);
        StylePrimaryButton(btnDelete);
		StylePrimaryButton(btnCompare);
        StylePrimaryButton(btnRun);
        StylePrimaryButton(btnSync);
        StylePrimaryButton(btnBackup);
        StylePrimaryButton(btnNewFolder);
		StylePrimaryButton(btnNewRootFolder);
        StylePrimaryButton(btnSave);
		
		compareMenu.ShowImageMargin = false;

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
        lblStatus.Text = "Bereit.";

        splitMain.Dock = DockStyle.Fill;
        splitMain.SplitterWidth = 6;

        var leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 1,
            RowCount = 2
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 248));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
        leftButtons.Controls.Add(btnSave);

        leftLayout.Controls.Add(leftButtons, 0, 0);
        leftLayout.Controls.Add(treeJobs, 0, 1);

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

        var lblName = new Label { Text = "Name", AutoSize = true, Anchor = AnchorStyles.Left };
        var lblSource = new Label { Text = "Source", AutoSize = true, Anchor = AnchorStyles.Left };
        var lblTarget = new Label { Text = "Target", AutoSize = true, Anchor = AnchorStyles.Left };
        var lblTree = new Label
        {
            Text = "Subfolders: unchecked folders will not be scanned during execution",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        var lblLog = new Label { Text = "Log", AutoSize = true, Anchor = AnchorStyles.Left };

        rightLayout.Controls.Add(lblName, 0, 0);
        rightLayout.Controls.Add(txtName, 1, 0);
        rightLayout.SetColumnSpan(txtName, 3);

        rightLayout.Controls.Add(lblSource, 0, 1);
        rightLayout.Controls.Add(txtSource, 1, 1);
        rightLayout.Controls.Add(btnBrowseSource, 2, 1);
        rightLayout.Controls.Add(btnRefreshTree, 3, 1);

        rightLayout.Controls.Add(lblTarget, 0, 2);
        rightLayout.Controls.Add(txtTarget, 1, 2);
        rightLayout.Controls.Add(btnBrowseTarget, 2, 2);
        rightLayout.Controls.Add(btnSwapDirection, 3, 2);

        rightLayout.Controls.Add(lblStatus, 0, 3);
        rightLayout.SetColumnSpan(lblStatus, 4);

        rightLayout.Controls.Add(lblTree, 0, 4);
        rightLayout.SetColumnSpan(lblTree, 4);

        rightLayout.Controls.Add(treeFolders, 0, 5);
        rightLayout.SetColumnSpan(treeFolders, 4);

        rightLayout.Controls.Add(lblLog, 0, 6);
        rightLayout.SetColumnSpan(lblLog, 4);

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

        splitMain.Panel1.Controls.Add(leftLayout);
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
                        ? "Please select a source folder."
                        : "Source folder changed. Please click 'Load folders'.");
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
		btnNewRootFolder.Click += (_, _) => CreateNewRootFolder();
        btnRename.Click += (_, _) => RenameCurrentJob();
        btnDelete.Click += (_, _) => DeleteCurrentJob();
        btnSave.Click += (_, _) => SaveAllJobs();
        btnCompare.Click += (_, _) => compareMenu.Show(btnCompare, new Point(0, btnCompare.Height));
        compareMenu.Items.Add("Compare mirror", null, async (_, _) => await CompareCurrentJobAsync(BackupMode.Mirror));
        compareMenu.Items.Add("Compare synchronization", null, async (_, _) => await CompareCurrentJobAsync(BackupMode.Synchronize));
        compareMenu.Items.Add("Compare backup", null, async (_, _) => await CompareCurrentJobAsync(BackupMode.Backup));
        btnRun.Click += async (_, _) => await RunCurrentJobAsync(BackupMode.Mirror);
        btnSync.Click += async (_, _) => await RunCurrentJobAsync(BackupMode.Synchronize);
        btnBackup.Click += async (_, _) => await RunCurrentJobAsync(BackupMode.Backup);

        treeJobs.ItemDrag += TreeJobs_ItemDrag;
		treeJobs.MouseDown += TreeJobs_MouseDown;
        treeJobs.DragEnter += TreeJobs_DragEnter;
        treeJobs.DragOver += TreeJobs_DragOver;
        treeJobs.DragDrop += TreeJobs_DragDrop;

        treeFolders.AfterCheck += TreeFolders_AfterCheck;
    }

    private void RefreshJobTree(Guid? selectedJobId = null, string? selectedFolderPath = null)
    {
        selectedJobId ??= CurrentJob?.Id;
        selectedFolderPath ??= treeJobs.SelectedNode?.Tag as string;

        foreach (var job in _jobs)
            EnsureFolderAndAncestors(job.FolderPath);

        treeJobs.BeginUpdate();
        try
        {
            treeJobs.Nodes.Clear();

            var folderMap = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var folderPath in _jobFolders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                GetOrCreateFolderNode(folderMap, folderPath);

            foreach (var job in _jobs
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

        if (selectedJobId.HasValue && SelectJobNode(selectedJobId.Value))
            return;

        if (!string.IsNullOrWhiteSpace(selectedFolderPath) && SelectFolderNode(selectedFolderPath))
            return;

        var firstJob = _jobs
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

        PopulateEditorFromCurrentJob();
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

    private void EnsureFolderAndAncestors(string? folderPath)
    {
        foreach (var path in ExpandFolderPath(folderPath))
            _jobFolders.Add(path);
    }

    private void CreateNewFolder()
    {
        CreateFolderUnder(GetFolderPathForSelection(treeJobs.SelectedNode));
    }

    private void CreateNewRootFolder()
    {
        CreateFolderUnder(string.Empty);
    }

    private void CreateFolderUnder(string parentPath)
    {
        parentPath = NormalizeFolderPath(parentPath);
        var initialName = GetUniqueFolderName(parentPath, "New folder");

        var newName = PromptDialog.Show(
            this,
            string.IsNullOrWhiteSpace(parentPath) ? "Create root folder" : "Create folder",
            "Folder name:",
            initialName);

        if (string.IsNullOrWhiteSpace(newName))
            return;

		var folderPath = CombineFolderPath(parentPath, newName.Trim());
        if (_jobFolders.Contains(folderPath))
            folderPath = CombineFolderPath(parentPath, GetUniqueFolderName(parentPath, newName.Trim()));

        EnsureFolderAndAncestors(folderPath);
        RefreshJobTree(selectedFolderPath: folderPath);
        SaveAllJobs();
    }

    private void RenameSelectedFolder(string folderPath)
    {
        folderPath = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        var parentPath = GetParentFolderPath(folderPath);
        var currentName = GetFolderLeafName(folderPath);

        var newName = PromptDialog.Show(this, "Rename folder", "Name:", currentName);
        if (newName is null)
            return;

        newName = newName.Trim();
        if (newName.Length == 0)
            return;

        var newPath = CombineFolderPath(parentPath, newName);
        if (!string.Equals(folderPath, newPath, StringComparison.OrdinalIgnoreCase) && _jobFolders.Contains(newPath))
            newPath = CombineFolderPath(parentPath, GetUniqueFolderName(parentPath, newName));

        if (string.Equals(folderPath, newPath, StringComparison.OrdinalIgnoreCase))
            return;

        var affectedFolders = _jobFolders
            .Where(x => IsSameOrChildFolder(x, folderPath))
            .OrderBy(x => x.Length)
            .ToList();

        foreach (var oldPath in affectedFolders)
            _jobFolders.Remove(oldPath);

        foreach (var oldPath in affectedFolders)
        {
            var suffix = oldPath.Length == folderPath.Length
                ? string.Empty
                : oldPath.Substring(folderPath.Length);

            EnsureFolderAndAncestors(newPath + suffix);
        }

        foreach (var job in _jobs)
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
        folderPath = NormalizeFolderPath(folderPath);
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        var jobsToDelete = _jobs
            .Where(x => IsSameOrChildFolder(NormalizeFolderPath(x.FolderPath), folderPath))
            .ToList();

        var foldersToDelete = _jobFolders
            .Where(x => IsSameOrChildFolder(x, folderPath))
            .ToList();

        var result = MessageBox.Show(
            this,
            $"Delete folder '{folderPath}'?{Environment.NewLine}{Environment.NewLine}" +
            $"Contained jobs: {jobsToDelete.Count}{Environment.NewLine}" +
            $"Contained subfolders: {Math.Max(0, foldersToDelete.Count - 1)}",
            "Delete folder",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        foreach (var job in jobsToDelete)
            _jobs.Remove(job);

        foreach (var path in foldersToDelete)
            _jobFolders.Remove(path);

        RefreshJobTree();
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
        if (job is null)
            return;

        var targetFolderPath = GetDropTargetFolderPath(new Point(e.X, e.Y));
        if (targetFolderPath is null)
            return;

        targetFolderPath = NormalizeFolderPath(targetFolderPath);
        if (string.Equals(NormalizeFolderPath(job.FolderPath), targetFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            SelectJobNode(job.Id);
            return;
        }

        job.FolderPath = targetFolderPath;
        EnsureFolderAndAncestors(targetFolderPath);

        RefreshJobTree(selectedJobId: job.Id);
        SaveAllJobs(false);

        lblStatus.Text = string.IsNullOrWhiteSpace(targetFolderPath)
            ? "Job moved to the root level."
            : $"Job moved to '{targetFolderPath}'.";
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
            ResetFolderTreeState("No job selected.");
            return;
        }

        var source = job.SourcePath.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            ResetFolderTreeState("Please select a source folder.");
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
        lblStatus.Text = "Loading folders...";

        try
        {
            var model = await Task.Run(
                () => BuildFolderTreeModel(normalizedSource, cts.Token),
                cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            if (CurrentJob is null || !PathsEquivalent(CurrentJob.SourcePath, normalizedSource))
                return;

            PopulateTreeFromModel(model, excluded, normalizedSource);
            lblStatus.Text = "Folders loaded.";
        }
        catch (DirectoryNotFoundException)
        {
            ResetFolderTreeState("Source folder not found.");
        }
        catch (OperationCanceledException)
        {
            if (CurrentJob is not null)
                lblStatus.Text = "Folder loading cancelled.";
        }
        catch (Exception ex)
        {
            ResetFolderTreeState("Could not load folder tree.");
            AppendLog("Folder tree error: " + ex.Message);
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

    private void AdjustSplitLayout()
    {
        if (!splitMain.IsHandleCreated)
            return;

        var availableWidth = splitMain.ClientSize.Width - splitMain.SplitterWidth;
        if (availableWidth <= 0)
            return;

        splitMain.FixedPanel = FixedPanel.None;
        splitMain.Panel1MinSize = 0;
        splitMain.Panel2MinSize = 0;

        if (availableWidth < MinLeftPanelWidth + MinRightPanelWidth)
        {
            splitMain.SplitterDistance = Math.Max(0, availableWidth / 2);
            return;
        }

        var maxLeftWidth = availableWidth - MinRightPanelWidth;
        var desiredLeftWidth = availableWidth - DesiredRightPanelWidth;
        var splitterDistance = Math.Clamp(
            desiredLeftWidth,
            MinLeftPanelWidth,
            maxLeftWidth);

        splitMain.SplitterDistance = splitterDistance;

        splitMain.Panel1MinSize = MinLeftPanelWidth;
        splitMain.Panel2MinSize = MinRightPanelWidth;
        splitMain.FixedPanel = FixedPanel.Panel2;
    }

    private void CommitNameEdit()
    {
        if (_updatingUi || CurrentJob is null)
            return;

        var newName = string.IsNullOrWhiteSpace(txtName.Text)
            ? "Unnamed job"
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
            btnNewFolder.Enabled = true;

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
                    lblStatus.Text = $"Folder selected: {folderPath}";
                else
                    lblStatus.Text = "No job selected.";

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
                ? "Please select a source folder."
                : "Folder tree for this job is not loaded. Please click 'Load folders'.");
    }

    private void CreateNewJob()
    {
        var job = new BackupJob
        {
            Name = GetUniqueJobName("New Job"),
            FolderPath = GetFolderPathForSelection(treeJobs.SelectedNode)
        };

        _jobs.Add(job);
        EnsureFolderAndAncestors(job.FolderPath);
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
            ? GetUniqueJobName(current.Name + " (reverse copy)")
            : GetUniqueJobName(current.Name + " (copy)");

        EnsureFolderAndAncestors(clone.FolderPath);
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

        var newName = PromptDialog.Show(this, "Rename job", "Name:", current.Name);
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

        var result = MessageBox.Show(
            this,
            $"Delete job '{current.Name}'?",
            "Delete job",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        _jobs.Remove(current);
        RefreshJobTree(selectedFolderPath: folderPath);

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
        ResetFolderTreeState("Direction swapped. Please click 'Load folders'.");
        SaveAllJobs();
    }

    private void ChooseFolder(TextBox targetBox)
    {
        using var dialog = new FolderBrowserDialog
        {
            ShowNewFolderButton = true,
            Description = "Select folder"
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
            throw new DirectoryNotFoundException("Source folder not found.");

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
            AppendLog($"Folder tree: could not read '{fullPath}': {ex.Message}");
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
            {
                CollectUncheckedNodes(child, parentChecked: true, excluded);
            }
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
        {
            CollectUncheckedNodes(child, node.Checked, excluded);
        }
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
        lblStatus.Text = "Comparison running...";

        try
        {
            var plan = await BuildPlanAsync(current, mode);
            lblStatus.Text = $"{GetModeDisplayName(mode)}: {plan.CopyCount} copy actions, {plan.DeleteCount} delete actions.";

            ToggleEditorEnabled(true);

            using var preview = new PlanPreviewForm(plan);
            preview.ShowDialog(this);
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER: " + ex.Message);
            lblStatus.Text = "Comparison failed.";

            MessageBox.Show(
                this,
                ex.Message,
                "Comparison error",
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
        AppendLog($"Job: {current.Name}");
        AppendLog($"Mode: {GetModeDisplayName(mode)}");

        ToggleEditorEnabled(false);
        lblStatus.Text = $"{GetModeDisplayName(mode)} running...";

        var progress = new Progress<string>(AppendLog);

        try
        {
            await RunModeAsync(current, mode, progress);
            lblStatus.Text = $"{GetModeDisplayName(mode)} finished.";
        }
        catch (Exception ex)
        {
            AppendLog("FEHLER: " + ex.Message);
            lblStatus.Text = "Fehler.";

            MessageBox.Show(
                this,
                ex.Message,
                "Job error",
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
            BackupMode.Mirror => "Mirror",
            BackupMode.Synchronize => "Synchronize",
            BackupMode.Backup => "Backup",
            _ => "Job"
        };
    }

    private void ToggleEditorEnabled(bool enabled)
    {
        treeJobs.Enabled = enabled;
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

        UseWaitCursor = !enabled;
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

            _repository.Save(_jobs.ToList(), _jobFolders);

            if (showStatus)
                lblStatus.Text = $"Saved {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            AppendLog("Save error: " + ex.Message);

            if (showStatus)
                lblStatus.Text = "Saving failed.";
        }
    }

    private void UpdateCurrentJobFromEditor()
    {
        if (_updatingUi || CurrentJob is null)
            return;

        var currentJobId = CurrentJob.Id;
        var oldName = CurrentJob.Name;

        CurrentJob.Name = string.IsNullOrWhiteSpace(txtName.Text) ? "Unnamed job" : txtName.Text.Trim();
        CurrentJob.SourcePath = txtSource.Text.Trim();
        CurrentJob.TargetPath = txtTarget.Text.Trim();

        if (!string.Equals(oldName, CurrentJob.Name, StringComparison.Ordinal))
            RefreshJobTree(selectedJobId: currentJobId);
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
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
        var name = string.IsNullOrWhiteSpace(baseName) ? "Job" : baseName.Trim();

        if (!_jobs.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return name;

        var i = 2;
        while (_jobs.Any(x => string.Equals(x.Name, $"{name} ({i})", StringComparison.OrdinalIgnoreCase)))
            i++;

        return $"{name} ({i})";
    }

    private string GetUniqueFolderName(string parentPath, string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "Folder" : baseName.Trim();;

        if (!_jobFolders.Contains(CombineFolderPath(parentPath, name)))
            return name;

        var i = 2;
        while (_jobFolders.Contains(CombineFolderPath(parentPath, $"{name} ({i})")))
            i++;

        return $"{name} ({i})";
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