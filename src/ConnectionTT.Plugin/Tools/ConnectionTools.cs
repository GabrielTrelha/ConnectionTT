using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.AdvanceSteel.CADAccess;
using Autodesk.AdvanceSteel.CADLink.Database;
using Autodesk.AdvanceSteel.ConstructionTypes;
using Autodesk.AdvanceSteel.DocumentManagement;
using Autodesk.AdvanceSteel.Modelling;

namespace ConnectionTT.Plugin;

public static class ConnectionTools
{
    public static void Register(ToolDispatcher d)
    {
        d.Register("list_connection_templates", ListConnectionTemplates);
        d.Register("create_connection_template", CreateConnectionTemplate);
        d.Register("create_connection", CreateConnection);
        d.Register("export_connection_templates", ExportConnectionTemplates);
        d.Register("repair_connection_templates", RepairConnectionTemplates);
    }

    private static JsonNode ListConnectionTemplates(JsonObject _)
    {
        var db = CADDocumentManager.GetCurrentDatabase();

        return new JsonObject
        {
            ["database"] = db.getFileName(),
            ["inDrawing"] = Strings(CollectTemplateNames()),
            ["definitionFiles"] = Strings(CollectDefinitionFiles()),
        };
    }

    private static JsonNode CreateConnectionTemplate(JsonObject args)
    {
        var name = Required(args, "name");
        var db = CADDocumentManager.GetCurrentDatabase();
        var ids = ResolveHandles(db, args["inputs"] as JsonArray);
        var beams = ids.Select(SafeOpen).OfType<Beam>().ToList();

        if (beams.Count >= 3)
        {
            UserDefinedConnection.CreateTemplate(db, name, beams[0], beams[1], beams[2], ids);
        }
        else if (beams.Count == 2)
        {
            UserDefinedConnection.CreateTemplate(db, name, beams[0], beams[1], ids);
        }
        else
        {
            throw new InvalidOperationException("Informe 2 ou 3 vigas (beams) em 'inputs'.");
        }

        return new JsonObject
        {
            ["created"] = true,
            ["name"] = name,
            ["database"] = db.getFileName(),
            ["inputs"] = ids.Count,
        };
    }

    private static JsonNode CreateConnection(JsonObject args)
    {
        var name = Required(args, "template");
        var db = CADDocumentManager.GetCurrentDatabase();
        var ids = ResolveHandles(db, args["targets"] as JsonArray);
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("Informe os handles dos objetos em 'targets'.");
        }

        var instance = UserDefinedConnectionInstance.CreateFrom(db, name, ids);
        return new JsonObject
        {
            ["created"] = true,
            ["template"] = name,
            ["objects"] = ids.Count,
            ["instanceType"] = instance.Type().ToString(),
        };
    }

    private static JsonNode ExportConnectionTemplates(JsonObject _)
    {
        var db = CADDocumentManager.GetCurrentDatabase();
        var doc = DocumentManager.GetCurrentDocument();
        var currentFile = db.getFileName();

        var databases = new JsonArray();
        if (doc != null)
        {
            foreach (var d in doc.Databases)
            {
                databases.Add(new JsonObject
                {
                    ["file"] = d.getFileName(),
                    ["isCurrent"] = d.getFileName() == currentFile,
                });
            }
        }

        return new JsonObject
        {
            ["currentDatabase"] = currentFile,
            ["databases"] = databases,
            ["templatesInDrawing"] = Strings(CollectTemplateNames()),
        };
    }

    private static JsonNode RepairConnectionTemplates(JsonObject _)
    {
        var templates = new HashSet<string>(CollectTemplateNames(), StringComparer.OrdinalIgnoreCase);
        var instances = CollectInstanceNames();

        var missing = new JsonArray();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in instances)
        {
            if (templates.Contains(name) || !reported.Add(name))
            {
                continue;
            }
            missing.Add(name);
        }

        return new JsonObject
        {
            ["ok"] = missing.Count == 0,
            ["templatesInDrawing"] = Strings(templates),
            ["instanceCount"] = instances.Count,
            ["missingTemplates"] = missing,
        };
    }

    private static string Required(JsonObject args, string key)
    {
        var value = args[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Informe '{key}'.");
        }
        return value;
    }

    private static List<ObjectId> ResolveHandles(OpenDatabase db, JsonArray? handles)
    {
        var ids = new List<ObjectId>();
        if (handles == null)
        {
            return ids;
        }

        foreach (var handle in handles)
        {
            var text = handle?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }
            ids.Add(db.getObjectIdFromHandle(text));
        }

        return ids;
    }

    private static FilerObject? SafeOpen(ObjectId id)
    {
        try
        {
            return DatabaseManager.Open(id);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> CollectTemplateNames()
    {
        var names = new List<string>();
        foreach (var id in ModelIds())
        {
            if (SafeOpen(id) is UserDefinedConnection template)
            {
                var name = template.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    private static List<string> CollectInstanceNames()
    {
        var names = new List<string>();
        foreach (var id in ModelIds())
        {
            if (SafeOpen(id) is UserDefinedConnectionInstance instance)
            {
                var name = instance.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    private static IEnumerable<ObjectId> ModelIds()
    {
        ObjectId[] ids = Array.Empty<ObjectId>();
        DatabaseManager.GetModelObjectIds(out ids);
        return ids ?? Array.Empty<ObjectId>();
    }

    private static List<string> CollectDefinitionFiles()
    {
        var files = new List<string>();
        var folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Connections");
        if (System.IO.Directory.Exists(folder))
        {
            foreach (var file in System.IO.Directory.GetFiles(folder, "*.json"))
            {
                files.Add(System.IO.Path.GetFileName(file));
            }
        }
        return files;
    }

    private static JsonArray Strings(IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(item);
        }
        return array;
    }
}

