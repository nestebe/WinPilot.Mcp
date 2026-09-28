namespace WinPilot.TestApps.WinForms;

internal sealed class MainForm : Form
{
    private readonly int _hangMs;

    private readonly MenuStrip _menuStrip = new() { Name = "menuMain", Dock = DockStyle.Top };
    private readonly ToolStripMenuItem _mnuFile = new() { Name = "mnuFile", Text = "File" };
    private readonly ToolStripMenuItem _mnuExit = new() { Name = "mnuExit", Text = "Exit" };

    private readonly TextBox _txtName = new()
    {
        Name = "txtName",
        Location = new Point(12, 30),
        Size = new Size(200, 23),
    };

    private readonly Button _btnHello = new()
    {
        Name = "btnHello",
        Text = "Hello",
        Location = new Point(220, 28),
        Size = new Size(90, 27),
    };

    private readonly Label _lblResult = new()
    {
        Name = "lblResult",
        Text = "Result",
        AutoSize = true,
        Location = new Point(12, 60),
    };

    private readonly Button _btnHang = new()
    {
        Name = "btnHang",
        Text = "Hang UI",
        Location = new Point(12, 90),
        Size = new Size(100, 27),
    };

    private readonly Button _btnShowLater = new()
    {
        Name = "btnShowLater",
        Text = "Show Later",
        Location = new Point(120, 90),
        Size = new Size(100, 27),
    };

    private readonly Button _btnLate = new()
    {
        Name = "btnLate",
        Text = "Late",
        Location = new Point(228, 90),
        Size = new Size(90, 27),
    };

    private readonly CheckBox _chkOption = new()
    {
        Name = "chkOption",
        Text = "Option",
        AutoSize = true,
        Location = new Point(12, 130),
    };

    private readonly RadioButton _radA = new()
    {
        Name = "radA",
        Text = "Radio A",
        AutoSize = true,
        Location = new Point(12, 160),
    };

    private readonly RadioButton _radB = new()
    {
        Name = "radB",
        Text = "Radio B",
        AutoSize = true,
        Location = new Point(100, 160),
    };

    private readonly ComboBox _cboChoice = new()
    {
        Name = "cboChoice",
        Location = new Point(12, 190),
        Size = new Size(120, 23),
    };

    private readonly ListBox _lstItems = new()
    {
        Name = "lstItems",
        Location = new Point(12, 220),
        Size = new Size(120, 90),
    };

    private readonly TreeView _treeItems = new()
    {
        Name = "treeItems",
        Location = new Point(150, 190),
        Size = new Size(150, 120),
    };

    private readonly TabControl _tabMain = new()
    {
        Name = "tabMain",
        Location = new Point(320, 28),
        Size = new Size(200, 280),
    };

    private readonly TabPage _tabFirst = new() { Name = "tabFirst", Text = "Tab 1" };
    private readonly TabPage _tabSecond = new() { Name = "tabSecond", Text = "Tab 2" };

    private bool _lateShown;

    public MainForm(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _hangMs = options.HangMs;

        Name = "frmMain";
        Text = "WinPilot WinForms Test App";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 330);

        _mnuFile.DropDownItems.Add(_mnuExit);
        _menuStrip.Items.Add(_mnuFile);
        MainMenuStrip = _menuStrip;

        _cboChoice.Items.Add("One");
        _cboChoice.Items.Add("Two");
        _lstItems.Items.Add("Alpha");
        _lstItems.Items.Add("Beta");

        var root = new TreeNode { Name = "nodeRoot", Text = "Root" };
        root.Nodes.Add(new TreeNode { Name = "nodeChild", Text = "Child" });
        _treeItems.Nodes.Add(root);

        _tabMain.TabPages.Add(_tabFirst);
        _tabMain.TabPages.Add(_tabSecond);

        _btnHello.Click += OnHelloClick;
        _btnHang.Click += OnHangClick;
        _btnShowLater.Click += OnShowLaterClick;
        _mnuExit.Click += OnExitClick;

        Controls.Add(_menuStrip);
        Controls.Add(_txtName);
        Controls.Add(_btnHello);
        Controls.Add(_lblResult);
        Controls.Add(_btnHang);
        Controls.Add(_btnShowLater);
        Controls.Add(_chkOption);
        Controls.Add(_radA);
        Controls.Add(_radB);
        Controls.Add(_cboChoice);
        Controls.Add(_lstItems);
        Controls.Add(_treeItems);
        Controls.Add(_tabMain);
    }

    private void OnHelloClick(object? sender, EventArgs e)
    {
        _lblResult.Text = $"Hello {_txtName.Text}";
    }

    private void OnHangClick(object? sender, EventArgs e)
    {
        if (_hangMs > 0)
        {
            Thread.Sleep(_hangMs);
        }
    }

    private async void OnShowLaterClick(object? sender, EventArgs e)
    {
        await Task.Delay(800);
        if (_lateShown || IsDisposed)
        {
            return;
        }

        _lateShown = true;
        Controls.Add(_btnLate);
        _btnLate.BringToFront();
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        Close();
    }
}
