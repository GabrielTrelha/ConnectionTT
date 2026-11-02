using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace ConnectionTT.Core;

public static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> All { get; } = Build();

    private static List<ToolDefinition> Build() => new()
    {
        Tool("get_session_info",
            "Report the Connection TT plugin status: plugin version, AutoCAD version and connection state."),

        Tool("get_detail_drawing_info",
            "Describe the open Advance Steel detail drawing: sheet size, views, dimensions, labels and title texts."),

        Tool("list_detail_views",
            "List the views on the open detail drawing with position, size and scale."),

        Tool("list_detail_dimensions",
            "List the Advance Steel dimensions on the open detail drawing, view by view."),

        Tool("list_detail_labels",
            "List the Advance Steel labels on the open detail drawing and what each says."),

        Tool("get_detail_view_geometry",
            "Read the drawn outline of one view as attachable points and hole centres.",
            Schema(("viewIndex", "integer", "Zero-based view index to read."))),

        Tool("add_detail_dimension",
            "Add an Advance Steel dimension to a view on the open detail drawing.",
            Schema(
                ("viewIndex", "integer", "Zero-based view index."),
                ("points", "array", "Attachment points for the dimension."),
                ("horizontal", "boolean", "True for a horizontal chain, false for vertical."))),

        Tool("add_detail_label",
            "Add an Advance Steel label to an object in a view on the open detail drawing.",
            Schema(
                ("viewIndex", "integer", "Zero-based view index."),
                ("point", "array", "Attachment point of the object to label."))),

        Tool("save_annotation_template",
            "Save the dimensions and labels of the open detail drawing as a named template.",
            Schema(("name", "string", "Template name."))),

        Tool("apply_annotation_template",
            "Apply a saved annotation template to the open detail drawing.",
            Schema(
                ("name", "string", "Template name."),
                ("dryRun", "boolean", "Preview only, apply nothing. Default true."))),

        Tool("export_material_list",
            "Build a material list grouped by part mark and save it as a spreadsheet.",
            Schema(
                ("outputPath", "string", "Destination file path (.xlsx or .csv)."),
                ("format", "string", "'xlsx' or 'csv'. Default 'xlsx'."))),

        Tool("get_numbering_status",
            "Report numbering state: whether up to date, marked/unmarked counts and settings."),

        Tool("run_steel_numbering",
            "Run Advance Steel numbering for single parts and assemblies."),

        Tool("get_steel_project_data",
            "Read the Advance Steel project data: project name, number, client, dates, etc."),

        Tool("set_steel_project_data",
            "Set Advance Steel project data fields.",
            Schema(("fields", "object", "Key/value project data fields to set."))),

        Tool("list_drawing_processes",
            "List the Advance Steel drawing processes installed on this machine."),

        Tool("list_drawing_styles",
            "List the Advance Steel drawing styles installed on this machine."),

        Tool("list_steel_objects",
            "List Advance Steel objects as steel objects (beams, plates, bolts, welds).",
            Schema(
                ("typeFilter", "string", "Optional filter: 'beam', 'plate', 'bolt', 'weld'."),
                ("roleFilter", "string", "Optional model role filter, e.g. 'Column'."))),

        Tool("get_steel_object",
            "Read one Advance Steel object in full by its handle.",
            Schema(("handle", "string", "Handle of the steel object or its entity."))),

        Tool("get_selected_steel_objects",
            "Describe the objects currently selected as steel objects, with marks."),

        Tool("set_steel_object_properties",
            "Set properties (material, coating, role, etc.) on steel objects.",
            Schema(
                ("handles", "array", "Handles of the objects to edit."),
                ("properties", "object", "Key/value properties to set."),
                ("dryRun", "boolean", "Preview only. Default true."))),

        Tool("list_connection_templates",
            "List the connection templates available on this machine."),

        Tool("create_connection",
            "Place an existing connection template on the given target objects.",
            Schema(
                ("template", "string", "Connection template name."),
                ("targets", "array", "Handles of the target objects to connect."))),

        Tool("create_connection_template",
            "Create a named connection template from 2 or 3 selected beams, stored in the drawing database so it travels with the DWG.",
            Schema(
                ("name", "string", "Template name."),
                ("inputs", "array", "Handles of the 2 or 3 input beams."))),

        Tool("export_connection_templates",
            "Embed the used connection template definitions into the DWG so it is portable."),

        Tool("repair_connection_templates",
            "Re-register connection templates referenced by the DWG that are missing or outdated."),
    };

    private static ToolDefinition Tool(string name, string description, JsonObject? schema = null)
        => new()
        {
            Name = name,
            Description = description,
            InputSchema = schema ?? Schema(),
        };

    private static JsonObject Schema(params (string name, string type, string? description)[] props)
    {
        var properties = new JsonObject();
        foreach (var (name, type, description) in props)
        {
            var prop = new JsonObject { ["type"] = type };
            if (description != null)
            {
                prop["description"] = description;
            }
            properties[name] = prop;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
    }
}
