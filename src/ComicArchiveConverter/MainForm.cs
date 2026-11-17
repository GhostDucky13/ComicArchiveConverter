using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Reflection;
using ComicArchiveConverter.Core;

namespace ComicArchiveConverter;

/// <summary>
/// The main window. The layout is built in code (not with the Visual Studio designer) so that
/// every control and every setting can be read top to bottom in one file.
///
/// Pages: Convert (drop zone + list), Preview, Log and Settings. Only the most important choice,
/// how chapters are packed, is on the Convert page. Everything else lives on the Settings page.
/// </summary>
[DesignerCategory("Code")] // open this file as code, not in the visual designer
public sealed class MainForm : Form
{
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly Icon? _appIcon = LoadAppIcon(null);
    private CancellationTokenSource? _cancellation;
    private bool _loadingSettings;
    private string? _lastOutputFolder;

    // ---- Navigation and pages
    private readonly HeaderPanel _header;
    private readonly FlowLayoutPanel _nav = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(14, 6, 14, 0), BackColor = Theme.Surface };
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8) };
    private readonly Dictionary<RadioButton, Control> _pages = new();
    private RadioButton _convertTab = null!, _previewTab = null!, _logTab = null!, _settingsTab = null!;

    // ---- Convert page
    private readonly DropPanel _dropPanel = new() { Dock = DockStyle.Fill };
    private readonly EmptyHint _emptyHint = new() { Dock = DockStyle.Fill };
    private readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        Dock = DockStyle.Fill,
        AllowDrop = true,
        BorderStyle = BorderStyle.None,
        ShowItemToolTips = true,
        Font = Theme.Body,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly RadioButton _oneCbzPerSeries = Theme.Radio("All chapters in one CBZ");
    private readonly RadioButton _oneCbzPerChapter = Theme.Radio("One CBZ per chapter");
    private readonly Button _removeButton = Theme.SecondaryButton("Remove");
    private readonly Button _clearButton = Theme.SecondaryButton("Clear list");
    private readonly ContextMenuStrip _listMenu = new();

    // ---- Preview page
    private readonly Label _previewSummary = Theme.Label("Press Preview to see exactly what will be created.", Theme.BodyBold);
    private readonly TreeView _previewTree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.Body, HideSelection = false };

    // ---- Log page
    private readonly TextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = Theme.Mono,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Surface,
    };

    // ---- Settings page
    private readonly RadioButton _saveNextTo = Theme.Radio("Next to the original folder or file");
    private readonly RadioButton _saveToFolder = Theme.Radio("In this folder:");
    private readonly TextBox _outputFolderBox = new() { Width = 380, Font = Theme.Body, Margin = new Padding(3, 4, 6, 3) };
    private readonly Button _browseButton = Theme.SecondaryButton("Browse…");
    private readonly CheckBox _keepStructure = Theme.CheckBox("Recreate the subfolders of the folder I dropped");
    private readonly ComboBox _existingCombo = Theme.Combo("Keep both (add a number to the new file)", "Skip it", "Replace it");
    private readonly CheckBox _flattenCbz = Theme.CheckBox("Number the pages in reading order (works in every reader)");
    private readonly CheckBox _comicInfo = Theme.CheckBox("Add ComicInfo.xml (series name, chapter number and chapter bookmarks)");
    private readonly CheckBox _groupChapter = Theme.CheckBox("Chapter / Episode");
    private readonly CheckBox _groupVolume = Theme.CheckBox("Volume");
    private readonly CheckBox _groupPart = Theme.CheckBox("Part");
    private readonly CheckBox _groupPages = Theme.CheckBox("Pages");
    private readonly CheckBox _groupImages = Theme.CheckBox("Images");
    private readonly ComboBox _compressionCombo = Theme.Combo("None (fastest, recommended)", "Fast", "Smallest file");
    private readonly RadioButton _archiveExtract = Theme.Radio("Extract the images into a folder");
    private readonly RadioButton _archiveToCbz = Theme.Radio("Convert them to CBZ");
    private readonly CheckBox _flattenExtract = Theme.CheckBox("Put all extracted images in one folder, numbered in reading order");
    private readonly CheckBox _verify = Theme.CheckBox("Check every image after converting (recommended)");
    private readonly CheckBox _openWhenDone = Theme.CheckBox("Open the output folder");
    private readonly CheckBox _playSound = Theme.CheckBox("Play a sound and flash the taskbar");
    private readonly Button _resetButton = Theme.SecondaryButton("Restore defaults");

    // ---- Footer
    private readonly Label _statusLabel = new() { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, Text = "Ready.", Margin = new Padding(0, 2, 0, 4) };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Continuous, Margin = new Padding(0) };
    private readonly LinkLabel _openOutputLink = new() { Text = "Open output folder", AutoSize = true, Visible = false, Font = Theme.Body, LinkColor = Theme.Accent, Margin = new Padding(12, 2, 0, 0) };
    private readonly Button _previewButton = Theme.SecondaryButton("Preview");
    private readonly Button _convertButton = Theme.PrimaryButton("Convert");

    public MainForm()
    {
        _settings = _settingsStore.Load();
        _header = new HeaderPanel(LoadAppIcon(new Size(64, 64))?.ToBitmap())
        {
            Text = "Comic Archive Converter",
            Subtitle = "Folders → CBZ   •   CBZ / CBR / CB7 → folders",
        };

        Text = "Comic Archive Converter " + typeof(MainForm).Assembly.GetName().Version?.ToString(3);
        if (_appIcon is not null) Icon = _appIcon;
        Font = Theme.Body;
        BackColor = Theme.Background;
        MinimumSize = new Size(820, 600);
        Size = new Size(1060, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        KeyPreview = true;

        BuildLayout();
        WireEvents();
        ApplySettingsToScreen(_settings);
        RestoreWindowBounds();
        UpdateControlStates();
    }

    // =====================================================================================
    // Layout
    // =====================================================================================

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0), Padding = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));   // header
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // tabs
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // current page
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // progress + buttons

        _convertTab = AddPage("Convert", BuildConvertPage());
        _previewTab = AddPage("Preview", BuildPreviewPage());
        _logTab = AddPage("Log", BuildLogPage());
        _settingsTab = AddPage("Settings", BuildSettingsPage());
        _convertTab.Checked = true;

        root.Controls.Add(_header, 0, 0);
        root.Controls.Add(_nav, 0, 1);
        root.Controls.Add(_pageHost, 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);
    }

    /// <summary>Adds a tab button to the bar and its page to the page area. Only the checked tab's page is visible.</summary>
    private RadioButton AddPage(string title, Control page)
    {
        var tab = new RadioButton
        {
            Text = title,
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            MinimumSize = new Size(96, 34),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.BodyBold,
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 4, 0),
        };
        tab.FlatAppearance.BorderSize = 0;
        tab.FlatAppearance.CheckedBackColor = Theme.AccentLight;
        tab.FlatAppearance.MouseOverBackColor = Theme.Background;
        tab.CheckedChanged += (_, _) =>
        {
            tab.ForeColor = tab.Checked ? Theme.Accent : Theme.Muted;
            page.Visible = tab.Checked;
            if (tab.Checked) page.BringToFront();
        };

        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages[tab] = page;
        _pageHost.Controls.Add(page);
        _nav.Controls.Add(tab);
        return tab;
    }

    private Control BuildConvertPage()
    {
        _list.Columns.Add("Name", 520);
        _list.Columns.Add("Action", 150);
        _list.Columns.Add("Status", 220);
        _dropPanel.Controls.Add(_list);
        _dropPanel.Controls.Add(_emptyHint);

        _listMenu.Items.Add("Show result in Explorer", null, (_, _) => ShowResultOfSelected());
        _listMenu.Items.Add("Open original location", null, (_, _) => ShowOriginalOfSelected());
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Remove", null, (_, _) => RemoveSelected());
        _list.ContextMenuStrip = _listMenu;

        // Bottom row: chapter choice on the left, Remove / Clear on the right.
        // Both halves size themselves to their contents, so the buttons can never be cut off.
        var chapterChoice = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        chapterChoice.Controls.Add(Theme.Label("Chapter folders:", Theme.BodyBold));
        chapterChoice.Controls.Add(_oneCbzPerSeries);
        chapterChoice.Controls.Add(_oneCbzPerChapter);

        var listButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0) };
        listButtons.Controls.Add(_clearButton);
        listButtons.Controls.Add(_removeButton);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(chapterChoice, 0, 0);
        bottom.Controls.Add(listButtons, 1, 0);

        var page = new TableLayoutPanel { ColumnCount = 1, RowCount = 2 };
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(_dropPanel, 0, 0);
        page.Controls.Add(bottom, 0, 1);
        return page;
    }

    private Control BuildPreviewPage()
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(8) };
        card.Controls.Add(_previewTree);

        var page = new TableLayoutPanel { ColumnCount = 1, RowCount = 2 };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _previewSummary.Margin = new Padding(0, 0, 0, 8);
        page.Controls.Add(_previewSummary, 0, 0);
        page.Controls.Add(card, 0, 1);
        return page;
    }

    private Control BuildLogPage()
    {
        var copy = Theme.SecondaryButton("Copy log");
        var clear = Theme.SecondaryButton("Clear log");
        copy.Click += (_, _) => { if (_logBox.TextLength > 0) Clipboard.SetText(_logBox.Text); };
        clear.Click += (_, _) => _logBox.Clear();

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(clear);
        buttons.Controls.Add(copy);

        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(8) };
        card.Controls.Add(_logBox);

        var page = new TableLayoutPanel { ColumnCount = 1, RowCount = 2 };
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(card, 0, 0);
        page.Controls.Add(buttons, 0, 1);
        return page;
    }

    private Control BuildSettingsPage()
    {
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 12, 20, 20),
            BackColor = Theme.Surface,
        };

        void Section(string title, string? help = null)
        {
            Label heading = Theme.Label(title, Theme.SectionTitle, Theme.Accent);
            heading.Margin = new Padding(0, stack.Controls.Count == 0 ? 0 : 18, 0, 2);
            stack.Controls.Add(heading);
            if (help is not null)
            {
                Label text = Theme.Label(help, color: Theme.Muted);
                text.Margin = new Padding(0, 0, 0, 4);
                stack.Controls.Add(text);
            }
        }

        void AddRow(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(12, 0, 0, 0) };
            row.Controls.AddRange(controls);
            stack.Controls.Add(row);
        }

        Section("Where to save");
        AddRow(_saveNextTo);
        AddRow(_saveToFolder, _outputFolderBox, _browseButton);
        AddRow(Indent(_keepStructure));
        AddRow(Theme.Label("If the file already exists:"), _existingCombo);

        Section("Creating CBZ files");
        AddRow(_flattenCbz);
        AddRow(_comicInfo);
        AddRow(Theme.Label("Compression:"), _compressionCombo);
        Section("Chapter folders", "Subfolders with these words in their name are chapters of the comic above them:");
        AddRow(_groupChapter, _groupVolume, _groupPart, _groupPages, _groupImages);

        Section("Opening CBR, CB7 and CBT files", "CBZ files are always extracted into a folder.");
        AddRow(_archiveExtract);
        AddRow(_archiveToCbz);
        AddRow(_flattenExtract);

        Section("Safety");
        AddRow(_verify);

        Section("When finished");
        AddRow(_openWhenDone);
        AddRow(_playSound);

        _resetButton.Margin = new Padding(12, 22, 0, 0);
        stack.Controls.Add(_resetButton);
        Label saved = Theme.Label("Settings are saved automatically.", color: Theme.Muted);
        saved.Margin = new Padding(12, 10, 0, 0);
        stack.Controls.Add(saved);

        // The settings scroll if the window is small.
        var page = new Panel { AutoScroll = true, BackColor = Theme.Surface };
        page.Controls.Add(stack);
        return page;
    }

    private static void LinkRadios(RadioButton a, RadioButton b)
    {
        a.CheckedChanged += (_, _) => { if (a.Checked) b.Checked = false; };
        b.CheckedChanged += (_, _) => { if (b.Checked) a.Checked = false; };
    }

    private static Control Indent(Control control)
    {
        control.Margin = new Padding(26, control.Margin.Top, control.Margin.Right, control.Margin.Bottom);
        return control;
    }

    private Control BuildFooter()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        _convertButton.MinimumSize = new Size(130, 40);
        _previewButton.MinimumSize = new Size(100, 40);
        buttons.Controls.Add(_previewButton);
        buttons.Controls.Add(_convertButton);

        var statusRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        statusRow.Controls.Add(_statusLabel);
        statusRow.Controls.Add(_openOutputLink);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Surface,
            Padding = new Padding(16, 0, 16, 10),
            Margin = new Padding(0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_progressBar, 0, 0);
        footer.SetColumnSpan(_progressBar, 2);
        _progressBar.Margin = new Padding(0, 0, 0, 0);
        footer.Controls.Add(statusRow, 0, 1);
        footer.Controls.Add(buttons, 1, 1);
        return footer;
    }

    // =====================================================================================
    // Events
    // =====================================================================================

    private void WireEvents()
    {
        // Drag and drop: every big control in the drop area accepts files.
        foreach (Control target in new Control[] { this, _dropPanel, _list, _emptyHint, _previewTree })
        {
            target.AllowDrop = true;
            target.DragEnter += OnDragEnter;
            target.DragOver += OnDragEnter;
            target.DragLeave += (_, _) => SetDropHighlight(false);
            target.DragDrop += OnDragDrop;
        }

        // Radio buttons only switch each other off when they share a container. These pairs sit
        // in different rows, so they are linked by hand.
        LinkRadios(_saveNextTo, _saveToFolder);
        LinkRadios(_archiveExtract, _archiveToCbz);

        _removeButton.Click += (_, _) => RemoveSelected();
        _clearButton.Click += (_, _) => ClearList();
        _list.SelectedIndexChanged += (_, _) => UpdateControlStates();
        _list.DoubleClick += (_, _) => ShowResultOfSelected();
        _list.Resize += (_, _) => FitColumns();
        _listMenu.Opening += (_, e) =>
        {
            bool any = _list.SelectedItems.Count > 0;
            _listMenu.Items[0].Enabled = any && SelectedRows().Any(r => r.Outputs.Count > 0);
            _listMenu.Items[1].Enabled = any;
            _listMenu.Items[3].Enabled = any && !IsBusy;
        };

        _previewButton.Click += async (_, _) => await PreviewAsync();
        _convertButton.Click += async (_, _) =>
        {
            if (IsBusy) _cancellation?.Cancel();
            else await ConvertAsync();
        };
        _openOutputLink.LinkClicked += (_, _) => { if (_lastOutputFolder is not null) OpenInExplorer(_lastOutputFolder, select: false); };

        _browseButton.Click += (_, _) => BrowseForOutputFolder();
        _resetButton.Click += (_, _) => ResetSettings();

        // Any settings change is saved straight away.
        foreach (Control control in new Control[]
                 {
                     _oneCbzPerSeries, _oneCbzPerChapter, _saveNextTo, _saveToFolder, _keepStructure, _flattenCbz,
                     _comicInfo, _groupChapter, _groupVolume, _groupPart, _groupPages, _groupImages, _archiveExtract,
                     _archiveToCbz, _flattenExtract, _verify, _openWhenDone, _playSound,
                 })
        {
            switch (control)
            {
                case CheckBox box: box.CheckedChanged += (_, _) => OnSettingChanged(); break;
                case RadioButton radio: radio.CheckedChanged += (_, _) => { if (radio.Checked) OnSettingChanged(); }; break;
            }
        }
        _existingCombo.SelectedIndexChanged += (_, _) => OnSettingChanged();
        _compressionCombo.SelectedIndexChanged += (_, _) => OnSettingChanged();
        _outputFolderBox.Leave += (_, _) => OnSettingChanged();

        FormClosing += OnFormClosing;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.V when _list.Focused || _emptyHint.Focused || _convertTab.Checked && !_outputFolderBox.Focused:
                PasteFiles();
                return true;
            case Keys.Control | Keys.A when _list.Focused:
                foreach (ListViewItem item in _list.Items) item.Selected = true;
                return true;
            case Keys.Delete when _list.Focused:
                RemoveSelected();
                return true;
            case Keys.F5:
                _ = PreviewAsync();
                return true;
            case Keys.Control | Keys.Enter:
                if (!IsBusy) _ = ConvertAsync();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        bool ok = !IsBusy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(ok);
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    private void SetDropHighlight(bool on)
    {
        _dropPanel.Highlight = on;
        if (_emptyHint.Highlight != on)
        {
            _emptyHint.Highlight = on;
            _emptyHint.Invalidate();
        }
        _list.BackColor = on ? Theme.AccentLight : Theme.Surface;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (IsBusy)
        {
            DialogResult answer = MessageBox.Show(this, "A conversion is still running. Cancel it and close?",
                "Comic Archive Converter", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cancellation?.Cancel();
        }
        RememberWindowBounds();
        SaveSettings();
    }

    // =====================================================================================
    // The list
    // =====================================================================================

    /// <summary>One row of the list, with what happened to it.</summary>
    private sealed class Row
    {
        public required InputItem Input { get; init; }
        public required ListViewItem Item { get; init; }
        public List<string> Outputs { get; } = new();
        public int Done { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
    }

    private IEnumerable<Row> AllRows() => _list.Items.Cast<ListViewItem>().Select(item => (Row)item.Tag!);

    private IEnumerable<Row> SelectedRows() => _list.SelectedItems.Cast<ListViewItem>().Select(item => (Row)item.Tag!);

    private void AddPaths(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        int added = 0, ignored = 0;

        _list.BeginUpdate();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
                added += AddInput(InputKind.ComicFolder, path) ? 1 : 0;
            else if (File.Exists(path) && ArchiveFormats.IsArchive(path))
                added += AddInput(InputKind.Archive, path) ? 1 : 0;
            else
            {
                ignored++;
                Log(LogLevel.Warning, $"Ignored (not a folder or comic archive): {path}");
            }
        }
        _list.EndUpdate();

        if (ignored > 0) SetStatus($"Added {added}. Ignored {ignored} item(s) that aren't folders or comic archives.");
        else if (added > 0) SetStatus($"{_list.Items.Count} item(s) in the list. Press Convert when ready.");
        UpdateControlStates();
    }

    /// <summary>Adds a row to the list. Returns false if it was already there.</summary>
    private bool AddInput(InputKind kind, string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (AllRows().Any(r => string.Equals(r.Input.Path, fullPath, StringComparison.OrdinalIgnoreCase))) return false;

        var input = new InputItem(kind, fullPath);
        string name = kind == InputKind.ComicFolder ? new DirectoryInfo(fullPath).Name : Path.GetFileName(fullPath);
        var item = new ListViewItem(new[] { name, ActionLabel(input), "Ready" }) { ToolTipText = fullPath, UseItemStyleForSubItems = false };
        item.Tag = new Row { Input = input, Item = item };
        item.SubItems[2].ForeColor = Theme.Muted;
        _list.Items.Add(item);
        return true;
    }

    private string ActionLabel(InputItem input)
    {
        if (input.Kind == InputKind.ComicFolder) return "Folder → CBZ";
        string type = ArchiveFormats.Label(input.Path);
        bool toCbz = _archiveToCbz.Checked && !ArchiveFormats.IsZip(input.Path);
        return toCbz ? $"{type} → CBZ" : $"{type} → Folder";
    }

    private void RemoveSelected()
    {
        if (IsBusy) return;
        foreach (ListViewItem item in _list.SelectedItems.Cast<ListViewItem>().ToList()) _list.Items.Remove(item);
        UpdateControlStates();
    }

    private void ClearList()
    {
        if (IsBusy) return;
        _list.Items.Clear();
        _previewTree.Nodes.Clear();
        _previewSummary.Text = "Press Preview to see exactly what will be created.";
        _openOutputLink.Visible = false;
        SetStatus("Ready.");
        UpdateControlStates();
    }

    private void PasteFiles()
    {
        if (Clipboard.ContainsFileDropList())
            AddPaths(Clipboard.GetFileDropList().Cast<string>());
        else if (Clipboard.ContainsText())
            AddPaths(Clipboard.GetText().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().Trim('"')));
    }

    private void SetRowStatus(Row row, string text, Color color)
    {
        row.Item.SubItems[2].Text = text;
        row.Item.SubItems[2].ForeColor = color;
    }

    private void FitColumns()
    {
        if (_list.Columns.Count < 3) return;
        int rest = _list.Columns[1].Width + _list.Columns[2].Width + SystemInformation.VerticalScrollBarWidth + 4;
        _list.Columns[0].Width = Math.Max(160, _list.ClientSize.Width - rest);
    }

    private void ShowResultOfSelected()
    {
        Row? row = SelectedRows().FirstOrDefault();
        if (row is null) return;
        if (row.Outputs.Count > 0) OpenInExplorer(row.Outputs[0], select: true);
        else ShowOriginalOfSelected();
    }

    private void ShowOriginalOfSelected()
    {
        Row? row = SelectedRows().FirstOrDefault();
        if (row is not null) OpenInExplorer(row.Input.Path, select: true);
    }

    private static void OpenInExplorer(string path, bool select)
    {
        try
        {
            if (select && (File.Exists(path) || Directory.Exists(path)))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Explorer not available: nothing useful to do.
        }
    }

    // =====================================================================================
    // Preview and Convert
    // =====================================================================================

    private async Task PreviewAsync()
    {
        if (IsBusy || !CheckReady()) return;

        await RunBusyAsync("Looking through your folders…", async token =>
        {
            ConversionService service = CreateService();
            List<InputItem> inputs = AllRows().Select(r => r.Input).ToList();
            ConversionPlan plan = await Task.Run(() => service.CreatePlan(inputs, token), token);

            ShowPlan(plan);
            ShowPlanInList(plan);
            LogWarnings(plan);
            _previewTab.Checked = true;
            SetStatus(_previewSummary.Text);
        });
    }

    private async Task ConvertAsync()
    {
        if (IsBusy || !CheckReady()) return;

        var started = Stopwatch.StartNew();
        await RunBusyAsync("Looking through your folders…", async token =>
        {
            ConversionService service = CreateService();
            List<InputItem> inputs = AllRows().Select(r => r.Input).ToList();

            // Plan again so the conversion always matches the current folders and settings.
            ConversionPlan plan = await Task.Run(() => service.CreatePlan(inputs, token), token);
            ShowPlan(plan);
            LogWarnings(plan);

            Dictionary<InputItem, Row> rows = AllRows().ToDictionary(r => r.Input);
            foreach (Row row in rows.Values)
            {
                row.Outputs.Clear();
                row.Done = row.Failed = row.Skipped = 0;
                if (plan.EmptyInputs.Contains(row.Input)) SetRowStatus(row, "No images found", Theme.Warning);
                else SetRowStatus(row, "Waiting…", Theme.Muted);
            }

            if (plan.TotalItems == 0)
            {
                SetStatus("Nothing to convert. See the Log page for details.");
                return;
            }

            Log(LogLevel.Info, $"Starting: {plan.TotalItems} item(s), {plan.TotalPages} images.");

            // Progress<T> automatically sends each report back to the UI thread.
            var progress = new Progress<ConversionProgress>(p =>
            {
                _progressBar.Maximum = Math.Max(1, p.Total);
                _progressBar.Value = Math.Min(p.Completed, _progressBar.Maximum);
                int percent = (int)(100L * p.Completed / Math.Max(1, p.Total));
                SetStatus(p.CurrentItem == "Finished" ? "Finishing…" : $"{percent}%  •  {p.ItemNumber} of {p.ItemCount}: {p.CurrentItem}");
            });
            var log = new Progress<LogMessage>(m => Log(m.Level, m.Text));
            var results = new Progress<ItemResult>(r =>
            {
                if (!rows.TryGetValue(r.Source, out Row? row)) return;
                switch (r.Status)
                {
                    case ItemStatus.Done: row.Done++; break;
                    case ItemStatus.Failed: row.Failed++; break;
                    default: row.Skipped++; break;
                }
                if (r.OutputPath is not null && r.Status != ItemStatus.Failed)
                {
                    row.Outputs.Add(r.OutputPath);
                    _lastOutputFolder = Path.GetDirectoryName(r.OutputPath);
                }
                UpdateRowAfterRun(row);
            });

            ConversionSummary summary = await Task.Run(() => service.Run(plan, progress, log, token, results), token);

            string time = started.Elapsed.TotalMinutes >= 1 ? $"{started.Elapsed:m\\:ss} min" : $"{started.Elapsed.TotalSeconds:0.0} s";
            string text = $"Done in {time}. Created {summary.Created} CBZ, extracted {summary.Extracted}"
                          + (summary.Skipped > 0 ? $", skipped {summary.Skipped}" : string.Empty)
                          + (summary.Failed > 0 ? $", {summary.Failed} FAILED (see Log)" : string.Empty) + ".";
            Log(summary.Failed == 0 ? LogLevel.Success : LogLevel.Warning, text);
            SetStatus(text);
            _progressBar.Value = _progressBar.Maximum;
            _openOutputLink.Visible = _lastOutputFolder is not null;

            OnFinished(summary);
        });
    }

    private void UpdateRowAfterRun(Row row)
    {
        if (row.Failed > 0)
            SetRowStatus(row, $"✗ {row.Failed} failed" + (row.Done > 0 ? $", {row.Done} OK" : string.Empty), Theme.Error);
        else if (row.Done > 0)
            SetRowStatus(row, row.Done == 1 ? "✓ Done" : $"✓ Done ({row.Done} files)", Theme.Success);
        else if (row.Skipped > 0)
            SetRowStatus(row, "Skipped (already exists)", Theme.Warning);
    }

    private void OnFinished(ConversionSummary summary)
    {
        if (_settings.PlaySoundWhenDone)
        {
            (summary.Failed > 0 ? SystemSounds.Exclamation : SystemSounds.Asterisk).Play();
            if (!ContainsFocus) NativeMethods.FlashTaskbar(Handle);
        }

        if (_settings.OpenOutputWhenDone && _lastOutputFolder is not null && summary.Created + summary.Extracted > 0)
            OpenInExplorer(_lastOutputFolder, select: false);
    }

    /// <summary>Shared busy/cancel/error handling for Preview and Convert.</summary>
    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> work)
    {
        SaveSettings();
        _cancellation = new CancellationTokenSource();
        _progressBar.Value = 0;
        _openOutputLink.Visible = false;
        SetStatus(status);
        UpdateControlStates();

        try
        {
            await work(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Log(LogLevel.Warning, "Cancelled. Everything already finished was kept; the unfinished file was removed.");
            SetStatus("Cancelled.");
            foreach (Row row in AllRows().Where(r => r.Item.SubItems[2].Text == "Waiting…")) SetRowStatus(row, "Cancelled", Theme.Muted);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, ex.Message);
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            UpdateControlStates();
        }
    }

    private bool CheckReady()
    {
        if (_list.Items.Count == 0)
        {
            SetStatus("Drag comic folders or CBZ / CBR files into the window first.");
            _convertTab.Checked = true;
            return false;
        }

        if (_saveToFolder.Checked && string.IsNullOrWhiteSpace(_outputFolderBox.Text))
        {
            MessageBox.Show(this, "Choose an output folder on the Settings page, or select \"Next to the original\".",
                "Comic Archive Converter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _settingsTab.Checked = true;
            return false;
        }

        return true;
    }

    private ConversionService CreateService() => new(ReadSettingsFromScreen(), new GdiImageValidator());

    private void ShowPlan(ConversionPlan plan)
    {
        _previewTree.BeginUpdate(); // stop redrawing while thousands of nodes are added
        try
        {
            _previewTree.Nodes.Clear();

            foreach (PlannedComic comic in plan.Comics)
            {
                string merged = comic.Job.MergedFolderCount > 0 ? $", {comic.Job.MergedFolderCount} chapters" : string.Empty;
                TreeNode node = _previewTree.Nodes.Add($"{comic.Job.Name}.cbz   ({comic.Job.Pages.Count} pages{merged})   →   {comic.OutputPath}");
                foreach (PageEntry page in comic.Job.Pages)
                    node.Nodes.Add($"{page.EntryName}   ←   {page.RelativeSource}");
            }

            foreach (PlannedExtraction extraction in plan.Extractions)
            {
                string verb = extraction.ToCbz ? "convert" : "extract";
                TreeNode node = _previewTree.Nodes.Add(
                    $"{Path.GetFileName(extraction.ArchivePath)}   ({verb}, {extraction.Entries.Count} images)   →   {extraction.OutputPath}");
                foreach (ExtractEntry entry in extraction.Entries)
                    node.Nodes.Add(extraction.ToCbz ? entry.EntryName : $"{entry.RelativeTarget}   ←   {entry.EntryName}");
            }

            if (_previewTree.Nodes.Count == 1) _previewTree.Nodes[0].Expand();
        }
        finally
        {
            _previewTree.EndUpdate();
        }

        int cbz = plan.Comics.Count + plan.Extractions.Count(e => e.ToCbz);
        int folders = plan.Extractions.Count(e => !e.ToCbz);
        _previewSummary.Text = plan.TotalItems == 0
            ? "Nothing to do: no images were found. See the Log page."
            : $"Will create {cbz} CBZ file(s) and {folders} folder(s) from {plan.TotalPages} images. Expand an item to see its pages in reading order.";
    }

    /// <summary>After a preview, each row shows what it will produce.</summary>
    private void ShowPlanInList(ConversionPlan plan)
    {
        foreach (Row row in AllRows())
        {
            if (plan.EmptyInputs.Contains(row.Input))
            {
                SetRowStatus(row, "No images found", Theme.Warning);
                continue;
            }

            List<PlannedComic> comics = plan.Comics.Where(c => c.Source == row.Input).ToList();
            PlannedExtraction? extraction = plan.Extractions.FirstOrDefault(e => e.Source == row.Input);
            if (comics.Count > 0)
                SetRowStatus(row, $"{comics.Count} CBZ  •  {comics.Sum(c => c.Job.Pages.Count)} pages", Theme.Text);
            else if (extraction is not null)
                SetRowStatus(row, $"{extraction.Entries.Count} images", Theme.Text);
        }
    }

    private void LogWarnings(ConversionPlan plan)
    {
        foreach (string warning in plan.Warnings) Log(LogLevel.Warning, warning);
        if (plan.Warnings.Count > 0 && plan.TotalItems == 0) _logTab.Checked = true;
    }

    // =====================================================================================
    // Settings  <->  screen
    // =====================================================================================

    private void ApplySettingsToScreen(AppSettings s)
    {
        _loadingSettings = true;
        try
        {
            _oneCbzPerChapter.Checked = s.ChapterOutput == ChapterOutput.OneFilePerChapter;
            _oneCbzPerSeries.Checked = !_oneCbzPerChapter.Checked;

            _saveToFolder.Checked = s.UseCustomOutputFolder;
            _saveNextTo.Checked = !s.UseCustomOutputFolder;
            _outputFolderBox.Text = s.OutputFolder;
            _keepStructure.Checked = s.KeepFolderStructure;
            _existingCombo.SelectedIndex = (int)s.ExistingOutput; // combo items are in the same order as the enum

            _flattenCbz.Checked = s.FlattenPagesInCbz;
            _comicInfo.Checked = s.AddComicInfo;
            _compressionCombo.SelectedIndex = (int)s.Compression;
            _groupChapter.Checked = s.Grouping.Chapter;
            _groupVolume.Checked = s.Grouping.Volume;
            _groupPart.Checked = s.Grouping.Part;
            _groupPages.Checked = s.Grouping.Pages;
            _groupImages.Checked = s.Grouping.Images;

            _archiveToCbz.Checked = s.ArchiveAction == ArchiveAction.ConvertToCbz;
            _archiveExtract.Checked = !_archiveToCbz.Checked;
            _flattenExtract.Checked = s.FlattenWhenExtracting;

            _verify.Checked = s.VerifyArchives;
            _openWhenDone.Checked = s.OpenOutputWhenDone;
            _playSound.Checked = s.PlaySoundWhenDone;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private AppSettings ReadSettingsFromScreen()
    {
        _settings.ChapterOutput = _oneCbzPerChapter.Checked ? ChapterOutput.OneFilePerChapter : ChapterOutput.OneFilePerSeries;

        _settings.UseCustomOutputFolder = _saveToFolder.Checked;
        _settings.OutputFolder = _outputFolderBox.Text.Trim();
        _settings.KeepFolderStructure = _keepStructure.Checked;
        _settings.ExistingOutput = (ExistingOutputPolicy)Math.Max(0, _existingCombo.SelectedIndex);

        _settings.FlattenPagesInCbz = _flattenCbz.Checked;
        _settings.AddComicInfo = _comicInfo.Checked;
        _settings.Compression = (ArchiveCompression)Math.Max(0, _compressionCombo.SelectedIndex);
        _settings.Grouping = new GroupingOptions
        {
            Chapter = _groupChapter.Checked,
            Volume = _groupVolume.Checked,
            Part = _groupPart.Checked,
            Pages = _groupPages.Checked,
            Images = _groupImages.Checked,
        };

        _settings.ArchiveAction = _archiveToCbz.Checked ? ArchiveAction.ConvertToCbz : ArchiveAction.ExtractToFolder;
        _settings.FlattenWhenExtracting = _flattenExtract.Checked;

        _settings.VerifyArchives = _verify.Checked;
        _settings.OpenOutputWhenDone = _openWhenDone.Checked;
        _settings.PlaySoundWhenDone = _playSound.Checked;
        return _settings;
    }

    private void OnSettingChanged()
    {
        if (_loadingSettings) return;
        SaveSettings();
        UpdateControlStates();
        foreach (Row row in AllRows()) row.Item.SubItems[1].Text = ActionLabel(row.Input);
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(ReadSettingsFromScreen());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogLevel.Warning, "Could not save settings: " + ex.Message);
        }
    }

    private void ResetSettings()
    {
        var defaults = new AppSettings
        {
            OutputFolder = _outputFolderBox.Text, // keep the typed folder, just untick it
            WindowBounds = _settings.WindowBounds,
            WindowMaximized = _settings.WindowMaximized,
        };
        ApplySettingsToScreen(defaults);
        OnSettingChanged();
        SetStatus("Settings restored to their defaults.");
    }

    private void BrowseForOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where CBZ files and extracted folders should be saved",
            UseDescriptionForTitle = true,
            InitialDirectory = _outputFolderBox.Text,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _outputFolderBox.Text = dialog.SelectedPath;
        _saveToFolder.Checked = true;
        OnSettingChanged();
    }

    private void RestoreWindowBounds()
    {
        if (_settings.WindowBounds is not { Length: 4 } b) return;
        var bounds = new Rectangle(b[0], b[1], Math.Max(b[2], MinimumSize.Width), Math.Max(b[3], MinimumSize.Height));

        // Only restore if the window would be visible (a second monitor may have been unplugged).
        if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
    }

    private void RememberWindowBounds()
    {
        Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowBounds = new[] { b.X, b.Y, b.Width, b.Height };
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
    }

    // =====================================================================================
    // Small helpers
    // =====================================================================================

    private bool IsBusy => _cancellation is not null;

    private void UpdateControlStates()
    {
        bool busy = IsBusy;
        bool empty = _list.Items.Count == 0;

        _emptyHint.Visible = empty;
        _list.Visible = !empty;
        if (!empty) FitColumns();

        _removeButton.Enabled = !busy && _list.SelectedItems.Count > 0;
        _clearButton.Enabled = !busy && !empty;
        _previewButton.Enabled = !busy && !empty;
        _convertButton.Enabled = busy || !empty;
        _convertButton.Text = busy ? "Cancel" : "Convert";
        Theme.MakePrimary(_convertButton, !busy);
        _oneCbzPerSeries.Enabled = _oneCbzPerChapter.Enabled = !busy;
        _pages[_settingsTab].Enabled = !busy;

        _outputFolderBox.Enabled = _saveToFolder.Checked;
        _keepStructure.Enabled = _saveToFolder.Checked;
        _flattenExtract.Enabled = _archiveExtract.Checked;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void Log(LogLevel level, string text)
    {
        string tag = level switch
        {
            LogLevel.Success => "[ OK ]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[FAIL]",
            _ => "[INFO]",
        };
        _logBox.AppendText($"{DateTime.Now:HH:mm:ss} {tag} {text}{Environment.NewLine}");
    }

    /// <summary>The icon is embedded in the .exe, so it works wherever the program is copied.</summary>
    private static Icon? LoadAppIcon(Size? size)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ComicArchiveConverter.app.ico");
        if (stream is null) return null;
        return size is Size wanted ? new Icon(stream, wanted) : new Icon(stream);
    }
}
