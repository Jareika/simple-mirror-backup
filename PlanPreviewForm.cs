using System.Drawing;

namespace SimpleMirrorBackup;

public sealed class PlanPreviewForm : Form
{
    private readonly BackupPlan _plan;
    private readonly DataGridView grid = new();
    private readonly Label lblSummary = new();
    private readonly TextBox txtSearch = new();
    private readonly Label lblSearchInfo = new();
    private readonly Button btnClearSearch = new();
    private readonly Button btnReset = new();
    private readonly Button btnRun = new();

    public bool RunRequested { get; private set; }

    public PlanPreviewForm(BackupPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));

        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppLanguage.F("Preview.Title", "Vergleich - {0}", GetModeText(plan.Mode));
        Width = 1450;
        Height = 760;
        MinimumSize = new Size(980, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        KeyPreview = true;

        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                txtSearch.Focus();
                txtSearch.SelectAll();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && txtSearch.Focused && txtSearch.TextLength > 0)
            {
                txtSearch.Clear();
                e.SuppressKeyPress = true;
            }
        };

        lblSummary.AutoSize = true;
        lblSummary.Dock = DockStyle.Fill;
        lblSummary.Margin = new Padding(0, 0, 0, 8);

        var lblHint = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Text = AppLanguage.T(
                "Preview.Hint",
                "Blau = aktuell abgewählt oder aus einem früheren Vergleich gemerkt. " +
                "Abgewählte Zeilen werden beim Starten nicht ausgeführt. Reset setzt wieder alle Häkchen.")
        };

        var txtWarnings = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Visible = plan.Warnings.Count > 0,
            Height = 92,
            MinimumSize = new Size(0, 92),
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.FromArgb(255, 248, 225),
            Text = string.Join(Environment.NewLine, plan.Warnings.Select(x => "• " + x))
        };

        txtSearch.Dock = DockStyle.Fill;
        txtSearch.PlaceholderText = AppLanguage.T(
            "Preview.SearchPlaceholder",
            "Pfad, Aktion oder Details suchen");
        txtSearch.Margin = new Padding(0, 0, 8, 0);
        txtSearch.TextChanged += (_, _) => ApplySearchFilter();

        btnClearSearch.Text = AppLanguage.T("Preview.Button.ClearSearch", "Leeren");
        btnClearSearch.AutoSize = true;
        btnClearSearch.MinimumSize = new Size(90, 30);
        btnClearSearch.Click += (_, _) =>
        {
            txtSearch.Clear();
            txtSearch.Focus();
        };

        lblSearchInfo.AutoSize = true;
        lblSearchInfo.Anchor = AnchorStyles.Left;
        lblSearchInfo.Margin = new Padding(8, 6, 0, 0);

        var searchLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        searchLayout.Controls.Add(new Label
        {
            Text = AppLanguage.T("Preview.SearchLabel", "Suchen"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, 0);
        searchLayout.Controls.Add(txtSearch, 1, 0);
        searchLayout.Controls.Add(btnClearSearch, 2, 0);
        searchLayout.Controls.Add(lblSearchInfo, 3, 0);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AllowUserToResizeColumns = true;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;

        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            HeaderText = AppLanguage.T("Preview.Column.Active", "Aktiv"),
            Width = 55,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = AppLanguage.T("Preview.Column.Action", "Aktion"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = AppLanguage.T("Preview.Column.Path", "Pfad"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 28,
            MinimumWidth = 220,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = AppLanguage.T("Preview.Column.SourceChanged", "Quelle geändert"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            MinimumWidth = 180,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = AppLanguage.T("Preview.Column.TargetChanged", "Ziel geändert"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
            MinimumWidth = 180,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = AppLanguage.T("Preview.Column.Details", "Details"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 38,
            MinimumWidth = 320,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0)
                return;

            var row = grid.Rows[e.RowIndex];
            if (row.Tag is not BackupPlanEntry entry)
                return;

            entry.IsSelected = row.Cells[0].Value is bool value && value;
            ApplyRowStyle(row, entry);
            UpdateSummary();
        };

        PopulateGrid();
        ApplySearchFilter();

        btnReset.Text = AppLanguage.T("Preview.Button.Reset", "Reset");
        btnReset.AutoSize = true;
        btnReset.MinimumSize = new Size(110, 34);
        btnReset.Padding = new Padding(10, 4, 10, 4);
        btnReset.Click += (_, _) => ResetSelection();

        btnRun.Text = AppLanguage.T("Preview.Button.RunSelected", "Ausgewählte Aktionen starten");
        btnRun.AutoSize = true;
        btnRun.MinimumSize = new Size(220, 34);
        btnRun.Padding = new Padding(10, 4, 10, 4);
        btnRun.Click += (_, _) => ConfirmRun();

        var btnClose = new Button
        {
            Text = AppLanguage.T("Common.Close", "Schließen"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(110, 34),
            Padding = new Padding(10, 4, 10, 4)
        };

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttonBar.Controls.Add(btnClose);
        buttonBar.Controls.Add(btnRun);
        buttonBar.Controls.Add(btnReset);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(lblSummary, 0, 0);
        layout.Controls.Add(lblHint, 0, 1);
        layout.Controls.Add(searchLayout, 0, 2);
        layout.Controls.Add(grid, 0, 3);

        if (plan.Warnings.Count > 0)
            layout.Controls.Add(txtWarnings, 0, 4);

        layout.Controls.Add(buttonBar, 0, 5);

        Controls.Add(layout);
        CancelButton = btnClose;

        UpdateSummary();
    }

    private void PopulateGrid()
    {
        grid.SuspendLayout();
        try
        {
            foreach (var entry in _plan.Entries)
            {
                var rowIndex = grid.Rows.Add(
                    entry.IsSelected,
                    entry.ActionText,
                    string.IsNullOrWhiteSpace(entry.RelativePath) ? "." : entry.RelativePath,
                    entry.SourceStateText,
                    entry.TargetStateText,
                    entry.DetailsText);

                var row = grid.Rows[rowIndex];
                row.Tag = entry;
                ApplyRowStyle(row, entry);
            }
        }
        finally
        {
            grid.ResumeLayout();
        }
    }

    private void ApplySearchFilter()
    {
        var term = txtSearch.Text.Trim();
        var visibleCount = 0;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not BackupPlanEntry entry)
                continue;

            var visible = MatchesSearch(entry, term);

            if (!visible && grid.CurrentCell is not null && grid.CurrentCell.OwningRow == row)
                grid.CurrentCell = null;

            row.Visible = visible;

            if (visible)
                visibleCount++;
        }

        btnClearSearch.Enabled = term.Length > 0;

        lblSearchInfo.Text = term.Length == 0
            ? AppLanguage.F("Preview.SearchVisibleCount", "{0} Zeilen sichtbar", visibleCount)
            : visibleCount == 0
                ? AppLanguage.T("Preview.SearchNoMatches", "Keine Treffer.")
                : AppLanguage.F("Preview.SearchMatches", "{0} Treffer sichtbar", visibleCount);
    }

    private static bool MatchesSearch(BackupPlanEntry entry, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return true;

        return entry.ActionText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               entry.RelativePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               entry.SourceStateText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               entry.TargetStateText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               entry.DetailsText.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void ResetSelection()
    {
        foreach (var entry in _plan.Entries)
            entry.IsSelected = true;

        foreach (DataGridViewRow row in grid.Rows)
        {
            row.Cells[0].Value = true;

            if (row.Tag is BackupPlanEntry entry)
                ApplyRowStyle(row, entry);
        }

        UpdateSummary();
    }

    private void ConfirmRun()
    {
        if (_plan.SelectedCount == 0)
        {
            MessageBox.Show(
                this,
                AppLanguage.T("Preview.NoSelectionMessage", "Es ist keine Aktion ausgewählt."),
                AppLanguage.T("Preview.NoSelectionTitle", "Vergleich"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var message = _plan.SelectedDeleteCount == 0
            ? AppLanguage.F("Preview.ConfirmRunNoDelete", "Die {0} ausgewählten Aktionen jetzt starten?", _plan.SelectedCount)
            : AppLanguage.F(
                "Preview.ConfirmRunWithDelete",
                "Die {0} ausgewählten Aktionen jetzt starten?{1}{1}Davon sind {2} Löschaktionen.",
                _plan.SelectedCount,
                Environment.NewLine,
                _plan.SelectedDeleteCount);

        var result = MessageBox.Show(
            this,
            message,
            AppLanguage.T("Preview.ConfirmRunTitle", "Aktionen starten"),
            MessageBoxButtons.YesNo,
            _plan.SelectedDeleteCount == 0 ? MessageBoxIcon.Question : MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        RunRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateSummary()
    {
        var modeText = GetModeText(_plan.Mode);

        var baseText = _plan.TotalCount == 0
            ? AppLanguage.F(
                "Preview.Summary.NoChanges",
                "{0}: keine Änderungen erforderlich.",
                modeText)
            : AppLanguage.F(
                "Preview.Summary.WithChanges",
                "{0}: insgesamt {1} Änderungen ({2} Kopieraktionen, {3} Löschaktionen). Ausgewählt: {4} ({5} Kopieren, {6} Löschen).",
                modeText,
                _plan.TotalCount,
                _plan.CopyCount,
                _plan.DeleteCount,
                _plan.SelectedCount,
                _plan.SelectedCopyCount,
                _plan.SelectedDeleteCount);

        lblSummary.Text = _plan.Warnings.Count == 0
            ? baseText
            : baseText + AppLanguage.F("Preview.Summary.WithWarningsSuffix", " Warnungen: {0}.", _plan.Warnings.Count);

        btnRun.Enabled = _plan.SelectedCount > 0;
    }

    private static void ApplyRowStyle(DataGridViewRow row, BackupPlanEntry entry)
    {
        if (!entry.IsSelected)
        {
            row.DefaultCellStyle.ForeColor = Color.Navy;
            row.DefaultCellStyle.SelectionForeColor = Color.Navy;
            row.DefaultCellStyle.BackColor = Color.FromArgb(221, 235, 255);
            row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(196, 220, 255);
            return;
        }

        var isDelete = entry.IsDelete;
        var foreColor = isDelete ? Color.Firebrick : Color.DarkGreen;
        var backColor = isDelete ? Color.MistyRose : Color.Honeydew;
        var selectionColor = isDelete
            ? Color.FromArgb(255, 225, 225)
            : Color.FromArgb(225, 245, 225);

        row.DefaultCellStyle.ForeColor = foreColor;
        row.DefaultCellStyle.SelectionForeColor = foreColor;
        row.DefaultCellStyle.BackColor = backColor;
        row.DefaultCellStyle.SelectionBackColor = selectionColor;
    }

    private static string GetModeText(BackupMode mode)
    {
        return mode switch
        {
            BackupMode.Mirror => AppLanguage.T("Preview.Mode.Mirror", "Spiegel-Vergleich"),
            BackupMode.Synchronize => AppLanguage.T("Preview.Mode.Synchronize", "Synchronisations-Vergleich"),
            BackupMode.Backup => AppLanguage.T("Preview.Mode.Backup", "Backup-Vergleich"),
            _ => "Vergleich"
        };
    }
}