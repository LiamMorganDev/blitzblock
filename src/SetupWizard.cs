using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class SetupWizard : Form
{
    private static readonly Color Surface = Color.FromArgb(21, 17, 34);
    private static readonly Color PanelColor = Color.FromArgb(30, 24, 45);
    private static readonly Color TextColor = Color.FromArgb(246, 239, 255);
    private static readonly Color Muted = Color.FromArgb(187, 176, 209);
    private static readonly Color Border = Color.FromArgb(70, 59, 88);
    private static readonly Color Accent = Color.FromArgb(194, 150, 255);
    private static readonly Color Danger = Color.FromArgb(255, 155, 173);
    private readonly Label step, status, finishedTitle, finished;
    private readonly TextBox blitz, destination;
    private readonly Panel managePage, blitzPage, installPage, finishedPage;
    private readonly Button next, back, cancel, destinationBrowse;
    private readonly CheckBox shortcut;
    private readonly string installed;
    private bool changing;
    private int page;

    internal SetupWizard() : this(BlitzLauncher.InstalledFolder()) {}

    internal SetupWizard(string managedFolder)
    {
        Text = "BlitzBlock Setup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(545, 344);
        MinimumSize = new Size(390, 383);
        FormBorderStyle = FormBorderStyle.None;
        Icon = Icon.ExtractAssociatedIcon(typeof(SetupWizard).Assembly.Location);
        Font = new Font("Segoe UI", 10);
        BackColor = Surface;
        ForeColor = TextColor;

        var hero = new Panel { Bounds = new Rectangle(0, 0, ClientSize.Width, 80),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        hero.Paint += PaintHero;
        var heading = LabelAt("BlitzBlock Setup", 72, 15, 305, 31);
        heading.Font = new Font(Font.FontFamily, 16, FontStyle.Bold);
        hero.Controls.Add(heading);
        var subtitle = LabelAt("Windows installer", 74, 47, 210, 24);
        subtitle.ForeColor = Muted;
        hero.Controls.Add(subtitle);
        var closeWindow = ButtonAt("×", "closeWindow", hero.Width - 43, 10, 35, false);
        closeWindow.AccessibleName = "Close setup";
        closeWindow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeWindow.FlatAppearance.BorderSize = 0;
        closeWindow.Click += delegate { Close(); };
        hero.Controls.Add(closeWindow);
        hero.MouseDown += DragWindow;
        heading.MouseDown += DragWindow;
        subtitle.MouseDown += DragWindow;
        Controls.Add(hero);

        installed = managedFolder;
        page = installed == null ? 1 : 0;
        step = LabelAt("", 18, 96, 330, 28);
        step.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
        Controls.Add(step);

        managePage = PagePanel();
        var path = LabelAt(installed, 0, 0, managePage.Width, 60);
        path.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        managePage.Controls.Add(path);
        var changeButton = ButtonAt("Change settings", "change", 0, 67, 132, true);
        var uninstallButton = ButtonAt("Uninstall", "uninstall", 141, 67, 88, false);
        uninstallButton.ForeColor = Danger;
        managePage.Controls.Add(changeButton);
        managePage.Controls.Add(uninstallButton);
        changeButton.Click += delegate { changing = true; page = 1; RefreshPage(); };
        uninstallButton.Click += RemoveInstallation;

        blitzPage = PagePanel();
        Button unused;
        blitz = FolderField(blitzPage, "Blitz install folder", "blitz",
            installed == null ? Path.Combine(BlitzLauncher.Local, @"Programs\Blitz") : BlitzLauncher.SavedBlitzFolder(installed), out unused);
        status = LabelAt("", 0, 64, blitzPage.Width, 45);
        status.Name = "status";
        status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        blitzPage.Controls.Add(status);

        installPage = PagePanel();
        destination = FolderField(installPage, "Install BlitzBlock to", "destination",
            installed ?? Path.Combine(BlitzLauncher.Local, "BlitzBlock"), out destinationBrowse);
        shortcut = new CheckBox { Name = "shortcut", Text = "Create a desktop shortcut",
            Checked = installed == null || BlitzLauncher.HasShortcut(installed), AutoSize = true, Location = new Point(0, 68) };
        installPage.Controls.Add(shortcut);

        finishedPage = PagePanel();
        finishedTitle = LabelAt("", 0, 0, 330, 30);
        finishedTitle.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
        finished = LabelAt("", 0, 37, finishedPage.Width, 77);
        finished.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        finishedPage.Controls.Add(finishedTitle);
        finishedPage.Controls.Add(finished);

        var divider = new Panel { Bounds = new Rectangle(0, ClientSize.Height - 60, ClientSize.Width, 1),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, BackColor = Border };
        Controls.Add(divider);
        next = FooterButton("Next", "next", 18, 120, true);
        cancel = FooterButton("Cancel", "cancel", 146, 78, false);
        back = FooterButton("Back", "back", 232, 73, false);
        AcceptButton = next;
        CancelButton = cancel;
        blitz.TextChanged += delegate { RefreshPage(); };
        destination.TextChanged += delegate { RefreshPage(); };
        back.Click += delegate {
            page = page == 2 ? 1 : 0;
            if (page == 0) {
                changing = false;
                blitz.Text = BlitzLauncher.SavedBlitzFolder(installed);
                shortcut.Checked = BlitzLauncher.HasShortcut(installed);
            }
            RefreshPage();
        };
        cancel.Click += delegate { Close(); };
        next.Click += Advance;
        RefreshPage();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private void DragWindow(object sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }

    private static void PaintHero(object sender, PaintEventArgs args)
    {
        var hero = (Panel)sender;
        using (var gradient = new LinearGradientBrush(hero.ClientRectangle, Color.FromArgb(53, 31, 80), Surface, 13f))
            args.Graphics.FillRectangle(gradient, hero.ClientRectangle);
        using (var pen = new Pen(Border))
        {
            args.Graphics.DrawLine(pen, 0, hero.Height - 1, hero.Width, hero.Height - 1);
            args.Graphics.DrawRectangle(pen, 18, 21, 39, 39);
        }
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(Accent))
            args.Graphics.FillPolygon(brush, new[] { new Point(36, 28), new Point(47, 39), new Point(39, 39),
                new Point(43, 51), new Point(28, 37), new Point(36, 37) });
    }

    private Panel PagePanel()
    {
        var panel = new Panel { Bounds = new Rectangle(18, 134, ClientSize.Width - 36, 128),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        Controls.Add(panel);
        return panel;
    }

    private TextBox FolderField(Panel panel, string label, string name, string initial, out Button browse)
    {
        panel.Controls.Add(LabelAt(label, 0, 0, 320, 22));
        var input = new TextBox { Name = name, Text = initial, Bounds = new Rectangle(0, 28, panel.Width - 98, 28),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, BackColor = PanelColor, ForeColor = TextColor };
        browse = ButtonAt("Browse…", name + "Browse", panel.Width - 90, 24, 90, false);
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += delegate {
            using (var picker = new FolderBrowserDialog { Description = label })
            {
                if (Directory.Exists(input.Text)) picker.SelectedPath = input.Text;
                if (picker.ShowDialog(this) == DialogResult.OK) input.Text = picker.SelectedPath;
            }
        };
        panel.Controls.Add(input);
        panel.Controls.Add(browse);
        return input;
    }

    private static Label LabelAt(string text, int x, int y, int width, int height)
    {
        return new Label { Text = text ?? "", BackColor = Color.Transparent, Bounds = new Rectangle(x, y, width, height) };
    }

    private static Button ButtonAt(string text, string name, int x, int y, int width, bool primary)
    {
        var button = new Button { Text = text, Name = name, Bounds = new Rectangle(x, y, width, 34),
            BackColor = primary ? Accent : PanelColor, ForeColor = primary ? Surface : TextColor, FlatStyle = FlatStyle.Flat };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(210, 175, 255) : Border;
        return button;
    }

    private Button FooterButton(string label, string name, int right, int width, bool primary)
    {
        var button = ButtonAt(label, name, ClientSize.Width - right - width, ClientSize.Height - 47, width, primary);
        button.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        Controls.Add(button);
        return button;
    }

    private void RefreshPage()
    {
        bool valid = BlitzLauncher.FindBlitz(blitz.Text) != null;
        step.Text = page == 0 ? "Manage BlitzBlock" : page == 1 ? "1 of 2 · Locate Blitz" : "2 of 2 · Installation options";
        step.Visible = page != 3;
        status.Text = valid ? "Blitz.exe and app files found" : "Blitz.exe or app files are missing from this folder.";
        status.ForeColor = valid ? Color.FromArgb(123, 217, 173) : Danger;
        managePage.Visible = page == 0;
        blitzPage.Visible = page == 1;
        installPage.Visible = page == 2;
        finishedPage.Visible = page == 3;
        finishedPage.Top = 96;
        destination.ReadOnly = changing;
        destinationBrowse.Visible = !changing;
        back.Visible = page == 2 || (page == 1 && changing);
        cancel.Visible = page != 3;
        cancel.Text = page == 0 ? "Close" : "Cancel";
        cancel.Left = page == 0 ? ClientSize.Width - 96 : ClientSize.Width - 224;
        next.Visible = page != 0;
        next.Text = page == 1 ? "Next" : page == 2 ? (changing ? "Save changes" : "Install") : "Close";
        next.Enabled = page == 3 || (valid && (page == 1 || !String.IsNullOrWhiteSpace(destination.Text)));
        next.BackColor = next.Enabled ? Accent : Border;
    }

    private void Complete(string title, string detail)
    {
        finishedTitle.Text = title;
        finished.Text = detail;
        page = 3;
        RefreshPage();
    }

    private void Advance(object sender, EventArgs args)
    {
        if (page == 3) { Close(); return; }
        string executable = BlitzLauncher.FindBlitz(blitz.Text);
        if (executable == null) { page = 1; RefreshPage(); return; }
        if (page == 1) { page = 2; RefreshPage(); return; }
        try
        {
            string folder = BlitzLauncher.ResolveFolder(destination.Text);
            BlitzLauncher.Install(folder, executable, shortcut.Checked);
            Complete(changing ? "Changes saved" : "BlitzBlock is installed",
                folder + (shortcut.Checked ? " · Desktop shortcut enabled" : ""));
        }
        catch (Exception error) { ShowError(error); }
    }

    private void RemoveInstallation(object sender, EventArgs args) { ConfirmUninstall(); }

    internal void ConfirmUninstall()
    {
        using (var dialog = new Form { Text = "Uninstall BlitzBlock?", Width = 348, Height = 200,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
            MaximizeBox = false, MinimizeBox = false, BackColor = Surface, ForeColor = TextColor, Font = Font })
        {
            var title = LabelAt("Uninstall BlitzBlock?", 16, 13, 280, 26);
            title.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
            dialog.Controls.Add(title);
            dialog.Controls.Add(LabelAt("This removes BlitzBlock and its desktop shortcut. Blitz and its settings are kept.", 16, 48, 297, 47));
            var no = ButtonAt("Cancel", "dismiss", 138, 110, 75, false);
            var yes = ButtonAt("Uninstall", "confirm", 222, 110, 92, false);
            yes.ForeColor = Danger;
            yes.DialogResult = DialogResult.OK;
            no.DialogResult = DialogResult.Cancel;
            dialog.Controls.Add(no);
            dialog.Controls.Add(yes);
            dialog.CancelButton = no;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
        }
        try
        {
            BlitzLauncher.Uninstall(installed);
            Complete("BlitzBlock is uninstalled", "Blitz and its settings were kept.");
        }
        catch (Exception error) { ShowError(error); }
    }

    private void ShowError(Exception error)
    {
        MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
