using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;

namespace ConnectionTT.Plugin;

public static class SessionTools
{
    public static void Register(ToolDispatcher d)
    {
        d.Register("get_session_info", GetSessionInfo);
    }

    private static JsonNode GetSessionInfo(JsonObject _)
    {
        return new JsonObject
        {
            ["plugin"] = "ConnectionTT",
            ["pluginVersion"] = "1.0.0",
            ["autocadVersion"] = Application.Version.ToString(),
            ["connected"] = true,
        };
    }
}
