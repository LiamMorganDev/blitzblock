using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

internal static class BlitzBlockUpdate
{
    private const string ReleaseApi = "https://api.github.com/repos/LiamMorganDev/blitzblock/releases/latest";
    private const string ReleaseRoot = "https://github.com/LiamMorganDev/blitzblock/releases/download/";
    internal const string AssetName = "BlitzBlock.Setup.exe";
    internal static readonly Version CurrentVersion = typeof(BlitzBlockUpdate).Assembly.GetName().Version;

    internal static bool TryStart()
    {
        // A running launcher can still hold the installed EXE open.
        int currentId;
        using (Process current = Process.GetCurrentProcess()) currentId = current.Id;
        foreach (Process candidate in Process.GetProcessesByName("BlitzBlock"))
        using (candidate)
        {
            if (candidate.Id != currentId)
                return false;
        }

        string candidatePath = null;
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var client = new ReleaseClient())
            {
                string url, digest;
                Version version;
                if (!FindUpdate(client.DownloadString(ReleaseApi), CurrentVersion, out version, out url, out digest))
                    return false;

                string temporary = Path.Combine(Path.GetTempPath(), "BlitzBlock-update.exe");
                candidatePath = temporary;
                client.TimeoutMilliseconds = 15000;
                client.DownloadFile(url, temporary);
                if (!MatchesDigest(temporary, digest))
                    return false;

                AssemblyName downloaded = AssemblyName.GetAssemblyName(temporary);
                if (downloaded.Name != typeof(BlitzBlockUpdate).Assembly.GetName().Name || downloaded.Version != version)
                    return false;

                using (Process current = Process.GetCurrentProcess())
                    Process.Start(new ProcessStartInfo(temporary, "--apply-update " + current.Id) { UseShellExecute = false });
                candidatePath = null;
                return true;
            }
        }
        catch (Exception error)
        {
            Trace.WriteLine("BlitzBlock update check failed: " + error.Message);
            return false;
        }
        finally
        {
            if (candidatePath != null)
            {
                try { File.Delete(candidatePath); }
                catch (IOException) {}
                catch (UnauthorizedAccessException) {}
            }
        }
    }

    internal static bool FindUpdate(string json, Version current, out Version version, out string url, out string digest)
    {
        version = null;
        url = null;
        digest = null;
        var release = new JavaScriptSerializer().Deserialize<Release>(json);
        if (release == null || release.draft || release.prerelease || release.assets == null ||
            String.IsNullOrEmpty(release.tag_name) || !release.tag_name.StartsWith("v", StringComparison.Ordinal))
            return false;

        string tagVersion = release.tag_name.Substring(1);
        if (!System.Text.RegularExpressions.Regex.IsMatch(tagVersion, @"^\d+(\.\d+){0,2}$"))
            return false;
        while (tagVersion.Split('.').Length < 3) tagVersion += ".0";
        if (!Version.TryParse(tagVersion + ".0", out version) || version <= current)
            return false;

        foreach (Asset asset in release.assets)
        {
            if (asset == null || asset.name != AssetName ||
                asset.browser_download_url != ReleaseRoot + release.tag_name + "/" + AssetName ||
                asset.size <= 0 || asset.size > 150 * 1024 * 1024 ||
                String.IsNullOrEmpty(asset.digest) ||
                !System.Text.RegularExpressions.Regex.IsMatch(asset.digest, "^sha256:[0-9a-f]{64}$"))
                continue;
            url = asset.browser_download_url;
            digest = asset.digest.Substring(7);
            return true;
        }
        return false;
    }

    private static bool MatchesDigest(string path, string expected)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == expected;
    }

    private sealed class ReleaseClient : WebClient
    {
        internal int TimeoutMilliseconds = 3500;

        internal ReleaseClient()
        {
            Headers[HttpRequestHeader.UserAgent] = "BlitzBlock";
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = (HttpWebRequest)base.GetWebRequest(address);
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            return request;
        }
    }

    private sealed class Release
    {
        public string tag_name { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public List<Asset> assets { get; set; }
    }

    private sealed class Asset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
        public int size { get; set; }
    }
}
