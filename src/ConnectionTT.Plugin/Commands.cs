using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

namespace ConnectionTT.Plugin;

public static class Commands
{
    [CommandMethod("CTTSTATUS")]
    public static void Status()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        doc?.Editor.WriteMessage("\nConnection TT: plugin carregado. Bridge (MCP) ativo no pipe 'ConnectionTT.AdvanceSteel.v1'.");
    }

    [CommandMethod("CTTRIBBON")]
    public static void RibbonReconcile()
    {
        Ribbon.Reconcile();
        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nConnection TT: painel da ribbon verificado.");
    }

    // Abre/fecha a paleta de conexoes (mesmo efeito do botao "Conexões" na ribbon).
    // Obs.: o antigo CTTPALETA foi removido de proposito - o AutoCAD tinha gravado esse comando para reabrir
    // a paleta a cada inicializacao. Sem o comando, a restauracao automatica antiga deixa de funcionar.
    [CommandMethod("CTTCONEXOES")]
    public static void Palette()
    {
        ConnectionPalette.Toggle();
    }

    [CommandMethod("CTTMCP")]
    public static void OpenMcp()
    {
        McpDialog.Show();
    }
}
