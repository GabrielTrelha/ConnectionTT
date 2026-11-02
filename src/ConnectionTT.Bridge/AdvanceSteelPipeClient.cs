using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConnectionTT.Core;
using ConnectionTT.IpcContracts;

namespace ConnectionTT.Bridge;

public sealed class AdvanceSteelPipeClient
{
    public async Task<IpcResponse> CallAsync(string tool, JsonObject? args)
    {
        var request = new IpcRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Tool = tool,
            Args = args,
        };

        using var pipe = ConnectWithRetry();

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonDefaults.Options));

        var line = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(line))
        {
            return new IpcResponse { Id = request.Id, Ok = false, Error = "Resposta vazia do Advance Steel." };
        }

        var response = JsonSerializer.Deserialize<IpcResponse>(line, JsonDefaults.Options);
        return response ?? new IpcResponse { Id = request.Id, Ok = false, Error = "Resposta invalida do Advance Steel." };
    }

    private static NamedPipeClientStream ConnectWithRetry()
    {
        for (int attempt = 1; ; attempt++)
        {
            var pipe = new NamedPipeClientStream(".", PipeNames.ServerName, PipeDirection.InOut);
            try
            {
                pipe.Connect(2000);
                return pipe;
            }
            catch (Exception)
            {
                pipe.Dispose();
                if (attempt >= 3)
                {
                    throw;
                }
                Thread.Sleep(200);
            }
        }
    }
}
