using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Collections.Generic;

// A per-launch folder lets the unelevated dashboard read the elevated worker's reports.
// It contains text/JSON only; the worker never reads code from a status or log file.
internal sealed class VendorSession : IDisposable
{
    internal Process Process;
    internal readonly string SessionId;
    internal readonly string SessionPath;
    internal string LogPath { get { return Path.Combine(SessionPath, "session.log"); } }
    internal string ErrorsPath { get { return Path.Combine(SessionPath, "errors.log"); } }
    internal string StatusPath { get { return Path.Combine(SessionPath, "status.json"); } }
    private VendorSessionSnapshot latest;

    internal VendorSession(string path)
    {
        SessionPath = path;
        SessionId = Path.GetFileName(path);
        latest = new VendorSessionSnapshot { Stage = "Starting", Message = "Requesting Windows administrator approval.", UpdatedUtc = DateTime.UtcNow };
    }

    internal VendorSessionSnapshot RefreshSnapshot()
    {
        try
        {
            string text = ReadShared(StatusPath, 1024 * 1024);
            if (!String.IsNullOrWhiteSpace(text))
            {
                Dictionary<string, object> report = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
                DateTime timestamp;
                string stage = Field(report, "state");
                if (String.IsNullOrWhiteSpace(stage)) stage = "Starting";
                int count;
                Int32.TryParse(Field(report, "errorCount"), out count);
                int code;
                int? exit = Int32.TryParse(Field(report, "exitCode"), out code) ? (int?)code : null;
                DateTime.TryParse(Field(report, "updatedUtc"), null, System.Globalization.DateTimeStyles.RoundtripKind, out timestamp);
                latest = new VendorSessionSnapshot {
                    Stage = stage, Message = Field(report, "message"), ErrorCount = Math.Max(0, count),
                    LastError = Field(report, "lastError"), ExitCode = exit, UpdatedUtc = timestamp,
                    Completed = stage == "Completed" || stage == "Failed" || stage == "Canceled"
                };
            }
        }
        catch (IOException) { /* Atomic replacement/antivirus may briefly hold the report. Keep the last complete report. */ }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }

        if (Process != null && !latest.Completed)
        {
            try
            {
                if (Process.HasExited)
                {
                    // The wrapper must report completion. A missing report must not appear successful.
                    latest = new VendorSessionSnapshot {
                        Stage = "Failed", Message = "WinUtil exited before reporting completion. Open the session log for details.",
                        ErrorCount = Math.Max(1, latest.ErrorCount), LastError = latest.LastError,
                        ExitCode = Process.ExitCode, Completed = true, UpdatedUtc = DateTime.UtcNow
                    };
                }
            }
            catch (InvalidOperationException) { }
        }
        return latest.Copy();
    }

    internal string ReadLogTail(int maxChars)
    {
        maxChars = Math.Max(1024, Math.Min(1024 * 1024, maxChars));
        string log = ReadShared(LogPath, maxChars);
        string errors = ReadShared(ErrorsPath, maxChars / 2);
        if (!String.IsNullOrWhiteSpace(errors)) log += "\r\n\r\n--- Error details ---\r\n" + errors;
        return log.Length > maxChars ? log.Substring(log.Length - maxChars) : log;
    }

    internal void WriteStartupFailure(Exception error, bool canceled)
    {
        string detail = error.ToString();
        File.AppendAllText(ErrorsPath, DateTime.UtcNow.ToString("o") + " " + detail + Environment.NewLine, new UTF8Encoding(false));
        WriteReport(canceled ? "Canceled" : "Failed", canceled ? "UAC canceled. No WinUtil action was started." : error.Message, canceled ? 0 : 1, detail, canceled ? 1223 : 1);
    }

    internal void WriteReport(string state, string message, int errorCount, string lastError, int? exitCode)
    {
        string json = new JavaScriptSerializer().Serialize(new {
            state = state, message = message, errorCount = errorCount, lastError = lastError,
            exitCode = exitCode, updatedUtc = DateTime.UtcNow.ToString("o")
        });
        string temporary = StatusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(StatusPath)) File.Replace(temporary, StatusPath, null);
            else File.Move(temporary, StatusPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Field(Dictionary<string, object> report, string name)
    {
        object value;
        return report.TryGetValue(name, out value) && value != null ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : String.Empty;
    }

    internal static string ReadShared(string path, int maxChars)
    {
        if (!File.Exists(path)) return String.Empty;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected session files cannot be read.");
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            // Seek near the tail without allocating memory proportional to a long-running transcript.
            long maxBytes = Math.Max(4096L, (long)maxChars * 4);
            bool skipped = stream.Length > maxBytes;
            if (skipped) stream.Seek(-maxBytes, SeekOrigin.End);
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string text = reader.ReadToEnd();
                if (skipped) { int firstLine = text.IndexOf('\n'); if (firstLine >= 0) text = text.Substring(firstLine + 1); }
                return text.Length > maxChars ? text.Substring(text.Length - maxChars) : text;
            }
        }
    }

    public void Dispose()
    {
        // Closing a dashboard never terminates an administrator's system operation.
        if (Process != null) { Process.Dispose(); Process = null; }
    }
}

internal sealed class VendorSessionSnapshot
{
    internal string Stage;
    internal string Message;
    internal string LastError;
    internal int ErrorCount;
    internal bool Completed;
    internal int? ExitCode;
    internal DateTime UpdatedUtc;
    internal VendorSessionSnapshot Copy() { return (VendorSessionSnapshot)MemberwiseClone(); }
}
