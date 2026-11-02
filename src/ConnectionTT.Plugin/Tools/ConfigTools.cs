using System;

namespace ConnectionTT.Plugin;

public static class ConfigTools
{
    public static void Register(ToolDispatcher d)
    {
        var tools = new[]
        {
            "get_steel_project_data",
            "set_steel_project_data",
            "list_drawing_processes",
            "list_drawing_styles",
        };

        foreach (var tool in tools)
        {
            d.Register(tool, _ => throw new NotImplementedException($"{tool}: implementar com a API do Advance Steel."));
        }
    }
}
