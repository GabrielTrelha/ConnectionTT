using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(ConnectionTT.Plugin.PluginApp))]

namespace ConnectionTT.Plugin;

public sealed class PluginApp : IExtensionApplication
{
    public static PipeServer Server { get; } = new();

    public void Initialize()
    {
        Log.Write("PluginApp.Initialize");
        ToolDispatcher.Current.RegisterAll();
        Ribbon.Initialize();
        Server.Start();
    }

    public void Terminate()
    {
        Log.Write("PluginApp.Terminate");
        Ribbon.Shutdown();
        Server.Stop();
    }
}
