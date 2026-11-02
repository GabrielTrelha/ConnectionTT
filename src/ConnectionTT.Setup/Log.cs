using System.Text;

namespace ConnectionTT.Setup;

/// <summary>Escreve no console e em %ProgramData%\ConnectionTT\setup.log (o instalador aponta para ele em caso de erro).</summary>
public static class Log
{
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ConnectionTT");

    public static string FilePath { get; } = Path.Combine(DataFolder, "setup.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("AVISO", message);
    public static void Error(string message) => Write("ERRO", message);
    public static void Error(string message, Exception ex) => Write("ERRO", message + Environment.NewLine + ex);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        Console.WriteLine(line);
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // Sem log em arquivo o console ainda mostra a mensagem; avisa uma vez no proprio console.
            Console.Error.WriteLine($"(nao foi possivel gravar em {FilePath}: {ex.Message})");
        }
    }
}
