using System;

namespace ConnectionTT.Plugin;

public static class DetailingTools
{
    public static void Register(ToolDispatcher d)
    {
        var tools = new[]
        {
            "get_detail_drawing_info",
            "list_detail_views",
            "list_detail_dimensions",
            "list_detail_labels",
            "get_detail_view_geometry",
            "add_detail_dimension",
            "add_detail_label",
            "save_annotation_template",
            "apply_annotation_template",
        };

        foreach (var tool in tools)
        {
            d.Register(tool, _ => throw new NotImplementedException($"{tool}: implementar com a API do Advance Steel."));
        }
    }
}
