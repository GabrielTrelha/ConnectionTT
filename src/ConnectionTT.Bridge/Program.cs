using System.Text;

namespace ConnectionTT.Bridge;

public static class Program
{
    public static async Task Main()
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);

        var server = new McpServer(new AdvanceSteelPipeClient());
        await server.RunAsync(Console.In, Console.Out);
    }
}
