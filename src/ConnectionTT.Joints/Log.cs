using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ConnectionTT.Joints;

internal static class Log
{
    private static readonly object Sync = new();

    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConnectionTT", "connectiontt-joint.log");

    private static readonly string FallbackPath = Path.Combine(Path.GetTempPath(), "connectiontt-joint.log");

    private static bool _headerWritten;

    public static void Write(string message)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine;
        lock (Sync)
        {
            if (!_headerWritten)
            {
                _headerWritten = true;
                var asm = Assembly.GetExecutingAssembly();
                var header = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ==== ConnectionTT.Joints carregado: {asm.Location} (build {File.GetLastWriteTime(asm.Location):yyyy-MM-dd HH:mm:ss}) ===={Environment.NewLine}";
                line = header + line;
            }

            if (TryAppend(FilePath, line, out var primaryError))
            {
                return;
            }

            // Nao silencia: tenta o TEMP e, se tambem falhar, manda para o Trace do processo.
            if (!TryAppend(FallbackPath, "[fallback: " + primaryError + "] " + line, out var fallbackError))
            {
                Trace.WriteLine("ConnectionTT.Joints LOG FALHOU: " + primaryError + " | " + fallbackError + " | " + line);
            }
        }
    }

    private static bool TryAppend(string path, string text, out string? error)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.AppendAllText(path, text);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }
}
