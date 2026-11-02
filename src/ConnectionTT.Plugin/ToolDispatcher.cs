using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using ConnectionTT.IpcContracts;

namespace ConnectionTT.Plugin;

public sealed class ToolDispatcher
{
    public static ToolDispatcher Current { get; } = new();

    private readonly Dictionary<string, Func<JsonObject, JsonNode?>> _handlers = new();

    public void Register(string tool, Func<JsonObject, JsonNode?> handler) => _handlers[tool] = handler;

    public void RegisterAll()
    {
        SessionTools.Register(this);
        DetailingTools.Register(this);
        ListTools.Register(this);
        ConfigTools.Register(this);
        ModelTools.Register(this);
        ConnectionTools.Register(this);
    }

    public IpcResponse Dispatch(IpcRequest request)
    {
        if (!_handlers.TryGetValue(request.Tool, out var handler))
        {
            return new IpcResponse { Id = request.Id, Ok = false, Error = $"Ferramenta desconhecida: {request.Tool}" };
        }

        try
        {
            var result = handler(request.Args ?? new JsonObject());
            return new IpcResponse { Id = request.Id, Ok = true, Result = result };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Id = request.Id, Ok = false, Error = ex.Message };
        }
    }
}
