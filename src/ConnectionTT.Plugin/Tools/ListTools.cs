using System;

namespace ConnectionTT.Plugin;

public static class ListTools
{
    public static void Register(ToolDispatcher d)
    {
        var tools = new[]
        {
            "export_material_list",
            "get_numbering_status",
            "run_steel_numbering",
        };

        foreach (var tool in tools)
        {
            d.Register(tool, _ => throw new NotImplementedException($"{tool}: implementar com a API do Advance Steel."));
        }
    }
}
