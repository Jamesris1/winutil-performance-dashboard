using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("WinUtil Performance Dashboard")]
[assembly: AssemblyDescription("Independent companion dashboard and launcher for the unmodified Chris Titus Tech WinUtil 26.10.07 release.")]
[assembly: AssemblyCompany("WinUtil Performance contributors")]
[assembly: AssemblyProduct("WinUtil Performance Dashboard")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal static class Launcher
{
    private const string Version = "26.10.07";
    private const string ResourceName = "WinUtil.Script";
    private const string ExpectedHash = "EEAE69922FBE6354EA4E2A37F705EAA17BDAF45CC7889618A313178A513B13EE";
    private const int ExpectedBytes = 927798;

    [STAThread]
    private static int Main(string[] args)
    {
        bool inspectOnly = HasInspectionFlag(args);
        try
        {
            LaunchOptions options = ParseOptions(args);
            if (options.Help)
            {
                Console.WriteLine(HelpText);
                return 0;
            }
            if (options.License)
            {
                Console.WriteLine("WinUtil Performance Dashboard — companion license\r\n");
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinUtil.DashboardLicense"))
                {
                    if (source == null) throw new InvalidDataException("Embedded dashboard license is missing.");
                    using (StreamReader license = new StreamReader(source)) Console.WriteLine(license.ReadToEnd());
                }
                Console.WriteLine("\r\nChris Titus Tech WinUtil — upstream license\r\n");
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinUtil.License"))
                {
                    if (source == null) throw new InvalidDataException("Embedded upstream WinUtil license is missing.");
                    using (StreamReader license = new StreamReader(source)) Console.WriteLine(license.ReadToEnd());
                }
                return 0;
            }

            byte[] script = ReadVerifiedScript();
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\WindowsPowerShell\v1.0\powershell.exe");

            if (options.SelfTest)
            {
                Dictionary<string, string> helperHashes = VerifyCompanionResources();
                Console.WriteLine("{\"success\":true,\"companionVersion\":\"1.1.0\",\"version\":" + JsonString(Version) +
                    ",\"embeddedResource\":" + JsonString(ResourceName) +
                    ",\"embeddedBytes\":" + script.Length +
                    ",\"sha256\":" + JsonString(Hash(script)) +
                    ",\"sha256MatchesOfficialRelease\":true" +
                    ",\"windowsPowerShellPath\":" + JsonString(powershell) +
                    ",\"windowsPowerShellPresent\":" + (File.Exists(powershell) ? "true" : "false") +
                    ",\"dotNetRuntime\":" + JsonString(Environment.Version.ToString()) +
                    ",\"capabilities\":{\"defaultPerformanceDashboard\":true,\"completeVendorGuiButton\":true,\"configFileOrHttpUrl\":true," +
                    "\"presets\":[\"Gaming\",\"Standard\",\"Minimal\",\"Advanced\"],\"offlineFlag\":true," +
                    "\"configAndPresetCanCombine\":true,\"usesUnmodifiedOfficialScript\":true," +
                    "\"readOnlyLiveTelemetry\":true,\"tenSecondBeforeAfterCapture\":true,\"jsonAndCsvExport\":true," +
                    "\"fullGuiErrorPanel\":true,\"persistentSessionLogs\":true,\"dashboardOperationStatus\":true,\"gamingSelection\":\"WPFToggleGameMode\"}" +
                    ",\"companionResourcesVerified\":" + helperHashes.Count +
                    ",\"requestedMode\":" + JsonString(options.Automation ? "vendorHeadlessAutomation" : (options.Offline ? "fullVendorGui" : "performanceDashboard")) +
                    ",\"vendorArguments\":" + JsonArray(VendorArguments(options)) +
                    ",\"commandLinePreview\":" + JsonString(BuildPowerShellArguments("<embedded-winutil.ps1>", options)) +
                    ",\"vendorScriptExecuted\":false" +
                    ",\"guiLaunched\":false,\"elevationRequested\":false,\"cacheWritten\":false}");
                return 0;
            }

            if (options.UiSelfTest)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Console.WriteLine(new JavaScriptSerializer().Serialize(Dashboard.RunUiSelfTests()));
                return 0;
            }

            if (options.BridgeSelfTest)
            {
                Console.WriteLine(new JavaScriptSerializer().Serialize(RunBridgeSelfTests()));
                return 0;
            }

            if (options.TelemetryTest)
            {
                List<TelemetrySample> samples = new List<TelemetrySample>();
                using (PerformanceTelemetry telemetry = new PerformanceTelemetry())
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    do { samples.Add(telemetry.Sample()); Thread.Sleep(1000); } while (timer.Elapsed.TotalSeconds < 10);
                    Console.WriteLine(new JavaScriptSerializer().Serialize(new {
                        success = true, readOnly = true, durationSeconds = timer.Elapsed.TotalSeconds, samples = samples,
                        vendorScriptExecuted = false, elevationRequested = false, cacheWritten = false
                    }));
                }
                return 0;
            }

            if (options.RenderPath != null)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (Dashboard dashboard = new Dashboard(true))
                using (Bitmap preview = dashboard.RenderPreview(options.PreviewPage)) preview.Save(options.RenderPath, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine("{\"success\":true,\"preview\":" + JsonString(options.RenderPath) + ",\"vendorScriptExecuted\":false}");
                return 0;
            }

            if (!options.Automation && !options.Offline)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Dashboard(false));
                return 0;
            }

            if (!File.Exists(powershell))
                throw new FileNotFoundException("Windows PowerShell is required to run this version of WinUtil.", powershell);

            using (VendorSession session = StartVendorSession(options))
            {
                session.Process.WaitForExit();
                VendorSessionSnapshot report = session.RefreshSnapshot();
                if (report.Stage == "Failed" || (report.ExitCode.HasValue && report.ExitCode.Value != 0))
                    throw new InvalidOperationException(report.Message + "\r\n\r\n" + report.LastError + "\r\nSession log: " + session.LogPath);
            }
            return 0;
        }
        catch (Win32Exception error)
        {
            if (error.NativeErrorCode == 1223) return 1223; // The user canceled the UAC request.
            return ReportFailure(error, inspectOnly);
        }
        catch (Exception error)
        {
            return ReportFailure(error, inspectOnly);
        }
    }

    internal static VendorSession StartVendorSession(LaunchOptions options)
    {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell 5.1 is required.", powershell);
            string scriptPath = CacheVerifiedScript(ReadVerifiedScript());
            Dictionary<string, string> hashes = VerifyCompanionResources();
            string cache = Path.GetDirectoryName(scriptPath);
            string wrapper = CacheCompanionResource(cache, "WinUtil.SessionWrapper", "session-wrapper", hashes);
            string extension = CacheCompanionResource(cache, "WinUtil.Extension", "extension", hashes);
            string sessionsRoot = Path.Combine(cache, "sessions");
            EnsureNormalDirectory(sessionsRoot);
            string sessionPath = Path.Combine(sessionsRoot, DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N"));
            EnsureNormalDirectory(sessionPath);
            VendorSession session = new VendorSession(sessionPath);
            File.WriteAllText(session.LogPath, String.Empty, new UTF8Encoding(false));
            File.WriteAllText(session.ErrorsPath, String.Empty, new UTF8Encoding(false));
            session.WriteReport("Starting", "Requesting Windows administrator approval.", 0, String.Empty, null);
            LaunchOptions resolved = ResolveGaming(options, cache);
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = powershell;
            List<string> launchArguments = new List<string>(new string[] {
                "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", wrapper,
                "-ScriptPath", scriptPath, "-ScriptHash", ExpectedHash,
                "-ExtensionPath", extension, "-ExtensionHash", hashes["WinUtil.Extension"], "-SessionPath", sessionPath
            });
            launchArguments.AddRange(VendorArguments(resolved));
            start.Arguments = JoinArguments(launchArguments);
            start.WorkingDirectory = Path.GetDirectoryName(scriptPath);
            start.UseShellExecute = true;
            start.Verb = "runas";
            start.WindowStyle = ProcessWindowStyle.Hidden;
            try
            {
                session.Process = Process.Start(start);
                if (session.Process == null) throw new InvalidOperationException("Windows did not start WinUtil.");
                return session;
            }
            catch (Exception error)
            {
                Win32Exception native = error as Win32Exception;
                session.WriteStartupFailure(error, native != null && native.NativeErrorCode == 1223);
                error.Data["WinUtilSession"] = session;
                throw;
            }
    }

    private const string HelpText =
        "WinUtil Performance (WinUtil 26.10.07)\r\n\r\n" +
        "WinUtil-Performance.exe\r\n" +
        "  Opens the performance dashboard. Open full WinUtil launches the complete original interface. No tweaks are applied automatically.\r\n\r\n" +
        "Optional original WinUtil parameters:\r\n" +
        "  -Config, --config <local JSON file or http(s) URL>\r\n" +
        "  -Preset, --preset <Gaming|Standard|Minimal|Advanced>\r\n" +
        "  -Offline, --offline\r\n\r\n" +
        "Config or Preset runs the original vendor's headless automation and can apply changes without a GUI.\r\n" +
        "Config and Preset can be combined: the preset supplies a baseline and the config adds selections.\r\n" +
        "Gaming is a companion preset containing only Windows Game Mode (WPFToggleGameMode), and cannot combine with Config.\r\n" +
        "Offline is the vendor's mode flag; it is not a network sandbox or a guarantee that every operation works offline.\r\n\r\n" +
        "Inspection without launching WinUtil, requesting UAC, or writing the script cache:\r\n" +
        "  --self-test [optional WinUtil parameters]   Validate resource, arguments, and runtime; print JSON.\r\n" +
        "  --version                                Print the same release and capability JSON.\r\n" +
        "  --help                                   Print this help.\r\n\r\n" +
        "  --telemetry-test                          Collect read-only metrics for 10 seconds; print JSON.\r\n" +
        "  --render-preview <PNG path>                Render only this dashboard to an image.\r\n\r\n" +
        "  --preview-page <overview|live|compare>     Optional page for --render-preview.\r\n" +
        "  --license                                 Print the companion and upstream MIT license notices.\r\n\r\n" +
        "  --ui-self-test                            Validate dashboard state without showing windows or applying actions.\r\n" +
        "  --bridge-self-test                        Validate session status/error persistence using temporary synthetic data.\r\n\r\n" +
        "This unsigned community companion embeds the unmodified official script and requires Windows PowerShell 5.1 and .NET Framework 4.x.\r\n" +
        "Actual GUI or automation launch requests Windows UAC. Only explicitly supplied WinUtil parameters are forwarded.";

    internal sealed class LaunchOptions
    {
        public string Config;
        public string Preset;
        public bool Offline;
        public bool SelfTest;
        public bool Help;
        public bool TelemetryTest;
        public string RenderPath;
        public int PreviewPage;
        public bool License;
        public bool UiSelfTest;
        public bool BridgeSelfTest;
        public bool Automation { get { return Config != null || Preset != null; } }
    }

    private static bool HasInspectionFlag(string[] args)
    {
        foreach (string argument in args)
            if (String.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(argument, "--version", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(argument, "--telemetry-test", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(argument, "--license", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(argument, "--ui-self-test", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(argument, "--bridge-self-test", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(argument, "--render-preview", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static LaunchOptions ParseOptions(string[] args)
    {
        LaunchOptions options = new LaunchOptions();
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "-config":
                case "--config":
                    if (options.Config != null) throw new ArgumentException("Config may only be supplied once.");
                    options.Config = NormalizeConfig(NextValue(args, ref index, "Config"));
                    break;
                case "-preset":
                case "--preset":
                    if (options.Preset != null) throw new ArgumentException("Preset may only be supplied once.");
                    string preset = NextValue(args, ref index, "Preset");
                    foreach (string allowed in new string[] { "Gaming", "Standard", "Minimal", "Advanced" })
                        if (String.Equals(preset, allowed, StringComparison.OrdinalIgnoreCase)) options.Preset = allowed;
                    if (options.Preset == null) throw new ArgumentException("Preset must be Gaming, Standard, Minimal, or Advanced.");
                    break;
                case "-offline":
                case "--offline":
                    options.Offline = true;
                    break;
                case "--self-test":
                case "--version":
                    options.SelfTest = true;
                    break;
                case "--help":
                    options.Help = true;
                    break;
                case "--telemetry-test":
                    options.TelemetryTest = true;
                    break;
                case "--render-preview":
                    options.RenderPath = Path.GetFullPath(NextValue(args, ref index, "Preview path"));
                    break;
                case "--preview-page":
                    string page = NextValue(args, ref index, "Preview page").ToLowerInvariant();
                    if (page == "overview") options.PreviewPage = 0;
                    else if (page == "live") options.PreviewPage = 1;
                    else if (page == "compare") options.PreviewPage = 2;
                    else throw new ArgumentException("Preview page must be overview, live, or compare.");
                    break;
                case "--license":
                    options.License = true;
                    break;
                case "--ui-self-test":
                    options.UiSelfTest = true;
                    break;
                case "--bridge-self-test":
                    options.BridgeSelfTest = true;
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + args[index] + ". Use --help for supported options.");
            }
        }
        if (options.Preset == "Gaming" && options.Config != null)
            throw new ArgumentException("Gaming applies only Windows Game Mode. Use Gaming without Config, or choose a vendor preset to combine with Config.");
        return options;
    }

    private static string NextValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length || String.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException(name + " requires a value.");
        return args[++index];
    }

    private static string NormalizeConfig(string config)
    {
        if (config.IndexOf('\0') >= 0 || config.IndexOf('\r') >= 0 || config.IndexOf('\n') >= 0)
            throw new ArgumentException("Config must be a local file path or an http(s) URL without control characters.");
        if (config.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || config.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Uri address;
            if (!Uri.TryCreate(config, UriKind.Absolute, out address) || String.IsNullOrEmpty(address.Host))
                throw new ArgumentException("Config is not a valid http(s) URL.");
            return config; // Preserve the URL as literal data for the original script.
        }
        string fullPath = Path.GetFullPath(config);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The local Config file does not exist.", fullPath);
        return fullPath; // Relative file names must retain the caller's directory, not the script cache directory.
    }

    private static List<string> VendorArguments(LaunchOptions options)
    {
        List<string> arguments = new List<string>();
        if (options.Config != null) { arguments.Add("-Config"); arguments.Add(options.Config); }
        if (options.Preset == "Gaming") { arguments.Add("-Config"); arguments.Add("<companion-game-mode-only.json>"); }
        else if (options.Preset != null) { arguments.Add("-Preset"); arguments.Add(options.Preset); }
        if (options.Offline) arguments.Add("-Offline");
        return arguments;
    }

    private static string BuildPowerShellArguments(string scriptPath, LaunchOptions options)
    {
        List<string> arguments = new List<string>(new string[] {
            "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath
        });
        arguments.AddRange(VendorArguments(options));
        return JoinArguments(arguments);
    }

    private static string JoinArguments(List<string> arguments)
    {
        StringBuilder commandLine = new StringBuilder();
        foreach (string argument in arguments)
        {
            if (commandLine.Length != 0) commandLine.Append(' ');
            commandLine.Append(QuoteWindowsArgument(argument));
        }
        return commandLine.ToString();
    }

    // Windows argv quoting: double backslashes before an embedded quote or the closing quote.
    // No argument becomes a PowerShell -Command expression or a shell command.
    private static string QuoteWindowsArgument(string argument)
    {
        StringBuilder quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') quoted.Append('\\', backslashes * 2 + 1);
            else quoted.Append('\\', backslashes);
            quoted.Append(character);
            backslashes = 0;
        }
        quoted.Append('\\', backslashes * 2);
        return quoted.Append('"').ToString();
    }

    private static string JsonArray(List<string> values)
    {
        StringBuilder json = new StringBuilder("[");
        foreach (string value in values)
        {
            if (json.Length != 1) json.Append(',');
            json.Append(JsonString(value));
        }
        return json.Append(']').ToString();
    }

    private static int ReportFailure(Exception error, bool selfTest)
    {
        if (selfTest)
            Console.WriteLine("{\"success\":false,\"error\":" + JsonString(error.Message) + "}");
        else
            MessageBox.Show(error.Message, "WinUtil could not start or complete", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }

    private static byte[] ReadVerifiedScript()
    {
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
        {
            if (source == null) throw new InvalidDataException("The embedded WinUtil release is missing.");
            using (MemoryStream buffer = new MemoryStream())
            {
                source.CopyTo(buffer);
                byte[] script = buffer.ToArray();
                if (script.Length != ExpectedBytes || Hash(script) != ExpectedHash)
                    throw new InvalidDataException("The embedded WinUtil script does not match the verified official release.");
                return script;
            }
        }
    }

    private static string CacheVerifiedScript(byte[] script)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (String.IsNullOrWhiteSpace(localAppData) || !Path.IsPathRooted(localAppData))
            throw new IOException("Windows did not provide a valid local application-data directory.");
        string root = Path.GetFullPath(localAppData).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string cache = Path.GetFullPath(Path.Combine(root, "WinUtil" + Version));
        if (!cache.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The WinUtil cache path is outside local application data.");
        if (Directory.Exists(cache) && (File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The WinUtil cache directory is a redirected directory. Choose a normal local application-data directory.");
        EnsureNormalDirectory(cache);
        string destination = Path.Combine(cache, "winutil.ps1");
        if (File.Exists(destination))
        {
            if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The WinUtil cache script is a redirected file.");
            if (Hash(File.ReadAllBytes(destination)) == ExpectedHash) return destination;
        }

        string temporary = Path.Combine(cache, ".winutil-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(script, 0, script.Length);
                output.Flush(true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
            if (Hash(File.ReadAllBytes(destination)) != ExpectedHash)
                throw new InvalidDataException("The cached WinUtil script failed its integrity check.");
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", String.Empty);
    }

    private static byte[] ReadResource(string name)
    {
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (source == null) throw new InvalidDataException("Embedded companion resource is missing: " + name);
            using (MemoryStream buffer = new MemoryStream()) { source.CopyTo(buffer); return buffer.ToArray(); }
        }
    }

    private static Dictionary<string, string> VerifyCompanionResources()
    {
        string json = Encoding.UTF8.GetString(ReadResource("WinUtil.HelperHashes")).TrimStart('\uFEFF');
        Dictionary<string, string> hashes = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(json);
        foreach (string name in new string[] { "WinUtil.SessionWrapper", "WinUtil.Extension" })
        {
            string expected;
            if (!hashes.TryGetValue(name, out expected) || Hash(ReadResource(name)) != expected)
                throw new InvalidDataException("Embedded companion resource failed its integrity check: " + name);
        }
        return hashes;
    }

    private static void EnsureNormalDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        DirectoryInfo current = new DirectoryInfo(full);
        while (current != null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected companion directories are not supported: " + current.FullName);
            current = current.Parent;
        }
        Directory.CreateDirectory(full);
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The companion directory changed while it was being prepared.");
    }

    private static string CacheCompanionResource(string cache, string resource, string baseName, Dictionary<string, string> hashes)
    {
        byte[] bytes = ReadResource(resource);
        string expected = hashes[resource];
        return CacheVerifiedBytes(cache, baseName + "-" + expected.Substring(0, 16).ToLowerInvariant() + ".ps1", bytes, expected);
    }

    private static string CacheVerifiedBytes(string cache, string name, byte[] bytes, string expected)
    {
        string destination = Path.Combine(cache, name);
        if (File.Exists(destination))
        {
            if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The companion cache file is redirected: " + destination);
            if (Hash(File.ReadAllBytes(destination)) == expected) return destination;
        }
        string temporary = Path.Combine(cache, ".companion-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
            if (Hash(File.ReadAllBytes(destination)) != expected) throw new InvalidDataException("Companion cache integrity check failed.");
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static LaunchOptions ResolveGaming(LaunchOptions options, string cache)
    {
        if (options.Preset != "Gaming") return options;
        if (options.Config != null) throw new ArgumentException("Gaming cannot combine with Config.");
        byte[] config = Encoding.UTF8.GetBytes("[\"WPFToggleGameMode\"]\r\n");
        string expected = Hash(config);
        string path = CacheVerifiedBytes(cache, "game-mode-only-" + expected.Substring(0, 16).ToLowerInvariant() + ".json", config, expected);
        return new LaunchOptions { Config = path, Offline = options.Offline };
    }

    private static object RunBridgeSelfTests()
    {
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        string folder = Path.Combine(temporaryRoot, "WinUtil-bridge-test-" + Guid.NewGuid().ToString("N"));
        EnsureNormalDirectory(folder);
        try
        {
            using (VendorSession session = new VendorSession(folder))
            {
                File.WriteAllText(session.LogPath, "Synthetic transcript\r\n[ERROR] Package installation failed\r\nDetailed error: access denied\r\n", new UTF8Encoding(false));
                File.WriteAllText(session.ErrorsPath, "Synthetic exception details: access denied\r\nline 42\r\n", new UTF8Encoding(false));
                session.WriteReport("Running", "Synthetic operation in progress", 1, "access denied", null);
                VendorSessionSnapshot running = session.RefreshSnapshot();
                if (running.Stage != "Running" || running.ErrorCount != 1 || running.Completed) throw new InvalidDataException("Running bridge status was lost.");
                string tail = session.ReadLogTail(8192);
                if (!tail.Contains("Package installation failed") || !tail.Contains("line 42")) throw new InvalidDataException("Persistent error details were lost.");
                File.WriteAllText(session.StatusPath, "{incomplete", new UTF8Encoding(false));
                if (session.RefreshSnapshot().Stage != "Running") throw new InvalidDataException("An incomplete status report replaced the last valid status.");
                session.WriteReport("Failed", "Synthetic bootstrap failure", 2, "Missing extension", 1);
                VendorSessionSnapshot failed = session.RefreshSnapshot();
                if (!failed.Completed || failed.ExitCode != 1 || failed.ErrorCount != 2) throw new InvalidDataException("Bootstrap failure did not reach the dashboard.");
                session.WriteStartupFailure(new InvalidOperationException("Synthetic process start failure"), false);
                if (!session.RefreshSnapshot().Completed || !session.ReadLogTail(8192).Contains("Synthetic process start failure")) throw new InvalidDataException("Process startup failure was not persisted.");
            }
            return new { success = true, syntheticErrors = true, runningStatusRetained = true, logDetailsReadable = true,
                incompleteReportRetainsPriorState = true, startupFailurePersisted = true, vendorScriptExecuted = false,
                elevationRequested = false, cacheWritten = false };
        }
        finally
        {
            // Only the fresh test directory under the system temp root is removed.
            if (String.Equals(Path.GetDirectoryName(folder), temporaryRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(folder, true);
        }
    }

    private static string JsonString(string value)
    {
        StringBuilder result = new StringBuilder("\"");
        foreach (char character in value)
        {
            if (character == '"') result.Append("\\\"");
            else if (character == '\\') result.Append("\\\\");
            else if (character < 32) result.Append("\\u" + ((int)character).ToString("x4"));
            else result.Append(character);
        }
        return result.Append('"').ToString();
    }
}
