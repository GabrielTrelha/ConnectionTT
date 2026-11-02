using System;

namespace ConnectionTT.Plugin;

public static class ModelTools
{
    public static void Register(ToolDispatcher d)
    {
        var tools = new[]
        {
            "list_steel_objects",
            "get_steel_object",
            "get_selected_steel_objects",
            "set_steel_object_properties",
        };

        foreach (var tool in tools)
        {
            d.Register(tool, _ => throw new NotImplementedException($"{tool}: implementar com a API do Advance Steel."));
        }
    }
}
