using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("BlitzBlock")]
[assembly: AssemblyDescription("Launches Blitz with local promotional content filters")]
[assembly: AssemblyVersion("1.0.0.0")]

internal static class BlitzLauncher
{
    private const string Title = "BlitzBlock";
    private const string PayloadId = "__PAYLOAD_ID__";

    private const string Settings = "blitz-location.txt";
    internal static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            string location = Assembly.GetExecutingAssembly().Location;
            string root = Path.GetDirectoryName(location);
            if (Array.IndexOf(args, "--uninstall") >= 0)
            {
                StartUninstaller(location);
                return 0;
            }
            if (Array.IndexOf(args, "--confirm-uninstall") >= 0)
            {
                string installed = InstalledFolder();
                if (installed == null) throw new InvalidOperationException("BlitzBlock is already uninstalled.");
                using (var wizard = new SetupWizard(installed))
                {
                    wizard.Shown += delegate { wizard.ConfirmUninstall(); };
                    Application.Run(wizard);
                }
                return 0;
            }
            string settingsFile = Path.Combine(root, Settings);
            if (Path.GetFileName(location).Equals("BlitzBlock.exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(settingsFile))
            {
                string blitz = FindBlitz(File.ReadAllText(settingsFile));
                if (blitz == null)
                    throw new InvalidOperationException("Blitz has moved or is missing. Run BlitzBlock Setup again and select its current folder.");
                Launch(root, blitz);
            }
            else
            {
                using (var wizard = new SetupWizard())
                    Application.Run(wizard);
            }
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string ResolveFolder(string path)
    {
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!Path.IsPathRooted(expanded)) throw new ArgumentException("Enter a full folder path.");
        return Path.GetFullPath(expanded);
    }

    internal static string FindBlitz(string folder)
    {
        try
        {
            string root = ResolveFolder(folder);
            string exe = Path.Combine(root, "Blitz.exe");
            if (File.Exists(exe) && File.Exists(Path.Combine(root, @"resources\app.asar"))
                && String.Equals(FileVersionInfo.GetVersionInfo(exe).ProductName, "Blitz", StringComparison.OrdinalIgnoreCase))
                return exe;
        }
        catch (ArgumentException) {}
        catch (NotSupportedException) {}
        catch (IOException) {}
        catch (UnauthorizedAccessException) {}
        return null;
    }

    internal static void Install(string root, string blitz, bool makeShortcut)
    {
        string blitzRoot = Path.GetDirectoryName(blitz).TrimEnd(Path.DirectorySeparatorChar);
        string target = root.TrimEnd(Path.DirectorySeparatorChar);
        if (target.Equals(blitzRoot, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(blitzRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a folder outside the Blitz install.");


        string source = Assembly.GetExecutingAssembly().Location;
        string installed = Path.Combine(root, "BlitzBlock.exe");
        if (File.Exists(installed) && FileVersionInfo.GetVersionInfo(installed).FileDescription != Title)
            throw new InvalidOperationException("The destination already contains another BlitzBlock.exe. Choose a different folder.");
        Unpack(root);
        if (!String.Equals(source, installed, StringComparison.OrdinalIgnoreCase)
            && (!File.Exists(installed) || FileHash(source) != FileHash(installed)))
        {
            string stage = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, stage);
                if (File.Exists(installed))
                    File.Replace(stage, installed, null);
                else
                    File.Move(stage, installed);
            }
            finally { File.Delete(stage); }
        }
        File.WriteAllText(Path.Combine(root, Settings), blitzRoot);
        string icon = InstallIcon(root, blitz);
        WriteShortcut(ShortcutPath(Environment.SpecialFolder.Programs), root, installed, icon);
        if (makeShortcut) WriteShortcut(ShortcutPath(Environment.SpecialFolder.DesktopDirectory), root, installed, icon);
        else RemoveShortcut(root, Environment.SpecialFolder.DesktopDirectory);
        RegisterInstalledApp(root, installed);
    }

    private static void Launch(string root, string blitz)
    {
        string folder = Unpack(root);
        string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var launch = new ProcessStartInfo(powershell,
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(folder, "open.ps1") + "\"");
        launch.WorkingDirectory = folder;
        launch.EnvironmentVariables["BLITZBLOCK_BLITZ_EXE"] = blitz;
        launch.UseShellExecute = false;
        launch.CreateNoWindow = true;
        using (var process = Process.Start(launch))
        {
            ApplyWindowIcon(process, blitz, Path.Combine(root, "BlitzBlock.ico"));
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Blitz could not start. Close Blitz from the system tray and try again.");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SendMessageTimeout(IntPtr window, uint message, IntPtr size, IntPtr icon,
        uint flags, uint timeout, out IntPtr result);

    private static void ApplyWindowIcon(Process launcher, string blitz, string path)
    {
        if (!File.Exists(path)) { launcher.WaitForExit(); return; }
        using (var large = new Icon(path, SystemInformation.IconSize))
        using (var small = new Icon(path, SystemInformation.SmallIconSize))
        {
            IntPtr previous = IntPtr.Zero;
            while (!launcher.WaitForExit(1000))
            {
                foreach (Process candidate in Process.GetProcessesByName("Blitz"))
                using (candidate)
                {
                    try
                    {
                        IntPtr window = candidate.MainWindowHandle;
                        if (window == IntPtr.Zero || window == previous ||
                            !String.Equals(candidate.MainModule.FileName, blitz, StringComparison.OrdinalIgnoreCase))
                            continue;
                        IntPtr result;
                        SendMessageTimeout(window, 0x0080, IntPtr.Zero, small.Handle, 2, 250, out result);
                        if (SendMessageTimeout(window, 0x0080, new IntPtr(1), large.Handle, 2, 250, out result))
                            previous = window;
                    }
                    catch (InvalidOperationException) {}
                    catch (System.ComponentModel.Win32Exception) {}
                }
            }
        }
    }

    private static string InstallIcon(string root, string blitz)
    {
        string iconPath = Path.Combine(root, "BlitzBlock.ico");
        using (var original = Icon.ExtractAssociatedIcon(blitz))
        using (var bitmap = original.ToBitmap())
        using (var image = new MemoryStream())
        {
            bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.R > pixel.G + 20 && pixel.R > pixel.B + 20)
                        bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, (pixel.R + pixel.G) / 2, pixel.G, pixel.R));
                }
            int maskStride = ((bitmap.Width + 31) / 32) * 4;
            using (var ico = new BinaryWriter(File.Create(iconPath)))
            {
                ico.Write((ushort)0); ico.Write((ushort)1); ico.Write((ushort)1);
                ico.Write((byte)bitmap.Width); ico.Write((byte)bitmap.Height); ico.Write((ushort)0);
                ico.Write((ushort)1); ico.Write((ushort)32);
                ico.Write(40 + (bitmap.Width * 4 + maskStride) * bitmap.Height);
                ico.Write(22);
                ico.Write(40); ico.Write(bitmap.Width); ico.Write(bitmap.Height * 2);
                ico.Write((ushort)1); ico.Write((ushort)32);
                for (int i = 0; i < 6; i++) ico.Write(0);
                var mask = new byte[maskStride * bitmap.Height];
                for (int row = 0; row < bitmap.Height; row++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, bitmap.Height - row - 1);
                        ico.Write(pixel.ToArgb());
                        if (pixel.A == 0)
                            mask[row * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                ico.Write(mask);
            }
        }

        return iconPath;
    }

    private static void WriteShortcut(string path, string root, string installed, string iconPath)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = installed;
        shortcut.WorkingDirectory = root;
        shortcut.Description = "Open Blitz with promo filtering";
        shortcut.IconLocation = iconPath + ",0";
        shortcut.Save();
    }

    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BlitzBlock";

    private static void StartUninstaller(string executable)
    {
        // Run the confirmation outside the install folder so Windows can remove the launcher.
        string directory = Path.Combine(Path.GetTempPath(), "BlitzBlock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string copy = Path.Combine(directory, "BlitzBlock Maintenance.exe");
        File.Copy(executable, copy);
        Process.Start(new ProcessStartInfo(copy, "--confirm-uninstall") { UseShellExecute = false });
    }

    private static void RegisterInstalledApp(string root, string executable)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            key.SetValue("InstallLocation", root);
        using (RegistryKey app = Registry.CurrentUser.CreateSubKey(UninstallPath))
        {
            app.SetValue("DisplayName", Title);
            app.SetValue("DisplayVersion", "1.0.0");
            app.SetValue("Publisher", "LiamMorganDev");
            app.SetValue("InstallLocation", root);
            app.SetValue("DisplayIcon", Path.Combine(root, "BlitzBlock.ico") + ",0");
            app.SetValue("UninstallString", "\"" + executable + "\" --uninstall");
            app.SetValue("URLInfoAbout", "https://github.com/LiamMorganDev/blitzblock");
            app.SetValue("NoModify", 1);
            app.SetValue("NoRepair", 1);
        }
    }

    private const string RegistryPath = @"Software\BlitzBlock";
    private static readonly string[] PayloadFiles = { "node.exe", "Node.js LICENSE.txt", "blitz.cjs", "open.ps1" };

    internal static string InstalledFolder()
    {
        string root;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
            root = key == null ? null : key.GetValue("InstallLocation") as string;
        if (root != null && File.Exists(Path.Combine(root, "BlitzBlock.exe"))) return root;
        string fallback = Path.Combine(Local, Title);
        string legacy = Path.Combine(fallback, "BlitzBlock.exe");
        return File.Exists(legacy)
            && FileVersionInfo.GetVersionInfo(legacy).FileDescription == Title ? fallback : null;
    }

    internal static string SavedBlitzFolder(string root)
    {
        string file = Path.Combine(root, Settings);
        return File.Exists(file) ? File.ReadAllText(file) : Path.Combine(Local, @"Programs\Blitz");
    }

    private static string ShortcutPath(Environment.SpecialFolder folder)
    {
        return Path.Combine(Environment.GetFolderPath(folder), "BlitzBlock.lnk");
    }

    internal static bool HasShortcut(string root) { return OwnsShortcut(root, Environment.SpecialFolder.DesktopDirectory); }

    private static bool OwnsShortcut(string root, Environment.SpecialFolder folder)
    {
        string path = ShortcutPath(folder);
        if (!File.Exists(path)) return false;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic link = shell.CreateShortcut(path);
        return String.Equals((string)link.TargetPath, Path.Combine(root, "BlitzBlock.exe"), StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveShortcut(string root, Environment.SpecialFolder folder)
    {
        if (OwnsShortcut(root, folder)) File.Delete(ShortcutPath(folder));
    }

    internal static void Uninstall(string root)
    {
        string installed = Path.Combine(root, "BlitzBlock.exe");
        if (!File.Exists(installed) || FileVersionInfo.GetVersionInfo(installed).FileDescription != Title)
            throw new InvalidOperationException("BlitzBlock could not be verified in the installed folder.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("This install folder is a link. Remove BlitzBlock from its actual folder.");
        File.Delete(installed);
        RemoveShortcut(root, Environment.SpecialFolder.DesktopDirectory);
        RemoveShortcut(root, Environment.SpecialFolder.Programs);
        File.Delete(Path.Combine(root, Settings));
        File.Delete(Path.Combine(root, "BlitzBlock.ico"));
        foreach (string folder in Directory.GetDirectories(root))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder), "^[a-f0-9]{64}$")
                || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0
                || !File.Exists(Path.Combine(folder, "blitz.cjs")))
                continue;
            foreach (string name in PayloadFiles) File.Delete(Path.Combine(folder, name));
            if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
        }
        if (Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root);
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
            if (key != null) key.DeleteValue("InstallLocation", false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallPath, false);
    }

    private static string FileHash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return Convert.ToBase64String(sha.ComputeHash(stream));
    }

    private static string Unpack(string root)
    {
        string folder = Path.Combine(root, PayloadId);
        if (Directory.Exists(folder))
            return folder;

        Directory.CreateDirectory(root);
        string stage = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                foreach (string name in PayloadFiles)
                    archive.GetEntry(name).ExtractToFile(Path.Combine(stage, name));
            }
            try { Directory.Move(stage, folder); }
            catch (IOException) { if (!Directory.Exists(folder)) throw; }
            return folder;
        }
        finally
        {
            foreach (string file in Directory.Exists(stage) ? Directory.GetFiles(stage) : new string[0])
                File.Delete(file);
            if (Directory.Exists(stage))
                Directory.Delete(stage);
        }
    }
}
