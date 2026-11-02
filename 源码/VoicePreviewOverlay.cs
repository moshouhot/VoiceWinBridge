using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

internal sealed class VoicePreviewOverlay : Form
{
    private readonly PreviewLabel textLabel;
    private bool hasAnchor;
    private Point anchor;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int CsDropShadow = 0x00020000;
    private const int CornerRadius = 16;
    private const int PreviewContentWidth = 520;
    private const int PreviewMaximumHeight = 126;
    private const int SwpNoActivate = 0x0010;
    private const int SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new IntPtr(-1);

    public VoicePreviewOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.White;
        Padding = new Padding(16, 11, 16, 11);
        StartPosition = FormStartPosition.Manual;

        Font previewFont;
        try { previewFont = new Font("Microsoft YaHei UI", 18f, FontStyle.Underline, GraphicsUnit.Point); }
        catch { previewFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 18f, FontStyle.Underline); }
        textLabel = new PreviewLabel {
            AutoSize = false,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(30, 41, 59),
            Font = previewFont,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
        Controls.Add(textLabel);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams value = base.CreateParams;
            value.ExStyle |= WsExToolWindow | WsExNoActivate;
            value.ClassStyle |= CsDropShadow;
            return value;
        }
    }

    public void SetPreview(string text, IntPtr target)
    {
        if (String.IsNullOrWhiteSpace(text))
        {
            Hide();
            hasAnchor = false;
            return;
        }

        text = text.Replace("\r", " ").Replace("\n", " ");
        Size proposed = new Size(PreviewContentWidth, PreviewMaximumHeight);
        Size measured = TextRenderer.MeasureText(text, textLabel.Font, proposed,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        int contentWidth = PreviewContentWidth;
        int contentHeight = Math.Max(38, Math.Min(PreviewMaximumHeight, measured.Height + 4));
        Size desiredSize = new Size(contentWidth + Padding.Horizontal,
            contentHeight + Padding.Vertical);
        bool sizeChanged = ClientSize != desiredSize;
        if (sizeChanged)
        {
            ClientSize = desiredSize;
            textLabel.Location = new Point(Padding.Left, Padding.Top);
            textLabel.Size = new Size(contentWidth, contentHeight);
            UpdateRoundedRegion();
        }
        textLabel.Text = text;

        if (!hasAnchor)
        {
            Point located;
            if (!TryFindTextCaret(target, out located))
                located = FindMouseScreenPoint();
            anchor = located;
            hasAnchor = true;
        }
        Rectangle work = Screen.FromPoint(anchor).WorkingArea;
        int x = Math.Max(work.Left, Math.Min(anchor.X, work.Right - Width));
        int y = anchor.Y - Height - 8;
        if (y < work.Top)
            y = Math.Min(work.Bottom - Height, anchor.Y + 8);

        if (!Visible)
            Show();
        if (Left != x || Top != y || sizeChanged)
            SetWindowPos(Handle, HwndTopmost, x, y, Width, Height,
                SwpNoActivate | SwpShowWindow);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = CreateRoundedPath(
            new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CornerRadius))
        using (var pen = new Pen(Color.FromArgb(224, 224, 224), 1f))
            e.Graphics.DrawPath(pen, path);
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;
        using (GraphicsPath path = CreateRoundedPath(
            new Rectangle(0, 0, Width, Height), CornerRadius))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static bool TryFindTextCaret(IntPtr target, out Point result)
    {
        if (TryFindAutomationCaret(out result))
            return true;
        return TryFindWin32Caret(target, out result);
    }

    private static bool TryFindAutomationCaret(out Point result)
    {
        result = Point.Empty;
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (focused == null)
                return false;

            object value;
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out value))
                return false;

            TextPatternRange[] selections = ((TextPattern)value).GetSelection();
            if (selections == null || selections.Length == 0 || selections[0] == null)
                return false;
            TextPatternRange caret = selections[0];

            System.Windows.Rect rectangle;
            if (!TryGetCaretRectangle(caret, out rectangle))
                return false;

            result = new Point((int)Math.Round(rectangle.Left),
                (int)Math.Round(rectangle.Bottom));
            return true;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (COMException) { return false; }
    }

    private static bool TryGetCaretRectangle(TextPatternRange caret,
        out System.Windows.Rect rectangle)
    {
        System.Windows.Rect[] bounds = caret.GetBoundingRectangles();
        if (bounds != null && bounds.Length > 0 && !bounds[0].IsEmpty)
        {
            rectangle = bounds[0];
            return true;
        }

        TextPatternRange forward = caret.Clone();
        if (forward.MoveEndpointByUnit(TextPatternRangeEndpoint.End,
            TextUnit.Character, 1) != 0)
        {
            bounds = forward.GetBoundingRectangles();
            if (bounds != null && bounds.Length > 0 && !bounds[0].IsEmpty)
            {
                rectangle = new System.Windows.Rect(bounds[0].Left,
                    bounds[0].Top, 1, bounds[0].Height);
                return true;
            }
        }

        TextPatternRange backward = caret.Clone();
        if (backward.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,
            TextUnit.Character, -1) != 0)
        {
            bounds = backward.GetBoundingRectangles();
            if (bounds != null && bounds.Length > 0)
            {
                System.Windows.Rect last = bounds[bounds.Length - 1];
                if (!last.IsEmpty)
                {
                    rectangle = new System.Windows.Rect(last.Right,
                        last.Top, 1, last.Height);
                    return true;
                }
            }
        }

        rectangle = System.Windows.Rect.Empty;
        return false;
    }

    private static bool TryFindWin32Caret(IntPtr target, out Point result)
    {
        result = Point.Empty;
        if (target == IntPtr.Zero)
            return false;

        uint processId;
        uint threadId = GetWindowThreadProcessId(target, out processId);
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf(typeof(GuiThreadInfo)) };
        if (threadId == 0 || !GetGUIThreadInfo(threadId, ref info)
            || info.hwndCaret == IntPtr.Zero)
            return false;
        if (info.rcCaret.left == 0 && info.rcCaret.top == 0
            && info.rcCaret.right == 0 && info.rcCaret.bottom == 0)
            return false;

        var point = new NativePoint { x = info.rcCaret.left, y = info.rcCaret.bottom };
        if (!ClientToScreen(info.hwndCaret, ref point))
            return false;
        result = new Point(point.x, point.y);
        return true;
    }

    private static Point FindMouseScreenPoint()
    {
        NativePoint cursor;
        if (GetCursorPos(out cursor))
            return new Point(cursor.x, cursor.y);
        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        return new Point(work.Left + work.Width / 2, work.Top + work.Height / 2);
    }

    private sealed class PreviewLabel : Label
    {
        public PreviewLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo {
        public int cbSize; public uint flags; public IntPtr hwndActive, hwndFocus, hwndCapture;
        public IntPtr hwndMenuOwner, hwndMoveSize, hwndCaret; public NativeRect rcCaret;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, int flags);
}
