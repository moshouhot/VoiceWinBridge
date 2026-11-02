using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox winCheck;
    private readonly CheckBox rightAltCheck;
    private readonly CheckBox startupCheck;
    private readonly NumericUpDown holdInput;
    private readonly TextBox directoryInput;
    private readonly Label directoryStatus;

    public bool EnableWin { get { return winCheck.Checked; } }
    public bool EnableRightAlt { get { return rightAltCheck.Checked; } }
    public bool StartWithWindows { get { return startupCheck.Checked; } }
    public int HoldMilliseconds { get { return Decimal.ToInt32(holdInput.Value); } }
    public string InstallDirectory { get { return directoryInput.Text.Trim().TrimEnd('\\'); } }

    public SettingsForm(BridgeSettings settings)
    {
        Text = "豆包语音桥接设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 320);
        Font = new Font("Microsoft YaHei UI", 9F);

        winCheck = new CheckBox {
            AutoSize = true, Left = 24, Top = 22, Text = "启用长按 Win",
            Checked = settings.EnableWin
        };
        rightAltCheck = new CheckBox {
            AutoSize = true, Left = 170, Top = 22, Text = "启用长按右 Alt",
            Checked = settings.EnableRightAlt
        };
        startupCheck = new CheckBox {
            AutoSize = true, Left = 24, Top = 57, Text = "开机自动启动（当前用户）",
            Checked = settings.StartWithWindows
        };
        var holdLabel = new Label {
            AutoSize = true, Left = 24, Top = 96, Text = "长按触发时间"
        };
        holdInput = new NumericUpDown {
            Left = 133, Top = 92, Width = 82, Minimum = 300, Maximum = 1500,
            Increment = 20, Value = settings.HoldMilliseconds
        };
        var unitLabel = new Label { AutoSize = true, Left = 222, Top = 96, Text = "毫秒" };
        var directoryLabel = new Label {
            AutoSize = true, Left = 24, Top = 143, Text = "豆包输入法安装目录"
        };
        directoryInput = new TextBox {
            Left = 24, Top = 167, Width = 472, Text = settings.InstallDirectory ?? String.Empty
        };
        directoryInput.TextChanged += delegate { UpdateDirectoryStatus(); };

        var detectButton = new Button {
            Text = "自动探测", Left = 24, Top = 203, Width = 88, Height = 29
        };
        detectButton.Click += delegate
        {
            string detected = BridgeSettings.DetectInstallDirectory();
            if (String.IsNullOrEmpty(detected))
                MessageBox.Show("没有探测到豆包输入法，请使用“浏览”选择安装目录。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                directoryInput.Text = detected;
        };
        var browseButton = new Button {
            Text = "浏览...", Left = 120, Top = 203, Width = 80, Height = 29
        };
        browseButton.Click += delegate
        {
            using (var dialog = new FolderBrowserDialog {
                Description = "选择包含 ImeService.exe 和 rpc.dll 的豆包输入法目录",
                SelectedPath = directoryInput.Text
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    directoryInput.Text = dialog.SelectedPath;
            }
        };
        directoryStatus = new Label {
            AutoSize = true, Left = 216, Top = 210, ForeColor = Color.DimGray
        };
        var okButton = new Button {
            Text = "保存", DialogResult = DialogResult.OK,
            Left = 344, Top = 271, Width = 72, Height = 29
        };
        var cancelButton = new Button {
            Text = "取消", DialogResult = DialogResult.Cancel,
            Left = 424, Top = 271, Width = 72, Height = 29
        };

        Controls.AddRange(new Control[] {
            winCheck, rightAltCheck, startupCheck, holdLabel, holdInput, unitLabel, directoryLabel,
            directoryInput, detectButton, browseButton, directoryStatus, okButton, cancelButton
        });
        AcceptButton = okButton;
        CancelButton = cancelButton;
        UpdateDirectoryStatus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && !BridgeSettings.IsInstallDirectory(InstallDirectory))
        {
            MessageBox.Show("所选目录中缺少 ImeService.exe 或 rpc.dll。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private void UpdateDirectoryStatus()
    {
        if (directoryStatus == null) return;
        bool valid = BridgeSettings.IsInstallDirectory(InstallDirectory);
        directoryStatus.Text = valid ? "目录有效" : "未找到所需文件";
        directoryStatus.ForeColor = valid ? Color.SeaGreen : Color.Firebrick;
    }
}
