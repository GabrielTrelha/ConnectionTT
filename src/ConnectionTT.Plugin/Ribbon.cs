using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Windows;
using AcRibbonServices = Autodesk.AutoCAD.Ribbon.RibbonServices;

namespace ConnectionTT.Plugin;

// Mantem o painel do ConnectionTT na ribbon SEM ficar pendurado no evento Idle.
//
// Historico: a versao anterior assinava Application.Idle para sempre e varria todas as abas da
// ribbon a cada disparo (muitas vezes por segundo, na thread principal). Isso deixava o Advance
// Steel mais pesado. Agora o Idle so fica ligado durante uma "janela de reconciliacao" curta,
// aberta apenas quando acontece algo que pode reconstruir a ribbon:
//   - carga do plugin (Initialize);
//   - criacao da ribbon (RibbonServices.RibbonPaletteSetCreated);
//   - carga de workspace (RibbonPaletteSet.WorkspaceLoaded);
//   - abertura/troca de desenho (DocumentManager.DocumentActivated);
//   - comando manual CTTRIBBON.
// Dentro da janela, a checagem roda no maximo 1x por segundo. Fora dela, o custo e zero.
// APIs de AcWindows.dll conferidas com o REA no AutoCAD 2026 (25.1).
public static class Ribbon
{
    private const long JanelaMs = 120_000;      // o log mostrou reconstrucoes da ribbon ate ~75 s apos a carga
    private const long IntervaloChecagemMs = 1_000;

    private static bool _initialized;
    private static bool _idleHooked;
    private static bool _workspaceHooked;
    private static long _windowUntil;
    private static long _nextCheck;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            var paletteSet = AcRibbonServices.RibbonPaletteSet;
            if (paletteSet != null)
            {
                HookWorkspace(paletteSet);
            }
            else
            {
                AcRibbonServices.RibbonPaletteSetCreated += OnRibbonPaletteSetCreated;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Ribbon.Initialize: falha ao assinar eventos da ribbon: {ex}");
        }

        try
        {
            Application.DocumentManager.DocumentActivated += OnDocumentActivated;
        }
        catch (Exception ex)
        {
            Log.Write($"Ribbon.Initialize: falha ao assinar DocumentActivated: {ex}");
        }

        OpenWindow("carga do plugin");
    }

    public static void Shutdown()
    {
        try
        {
            AcRibbonServices.RibbonPaletteSetCreated -= OnRibbonPaletteSetCreated;
            var paletteSet = AcRibbonServices.RibbonPaletteSet;
            if (paletteSet != null && _workspaceHooked)
            {
                paletteSet.WorkspaceLoaded -= OnWorkspaceLoaded;
            }

            Application.DocumentManager.DocumentActivated -= OnDocumentActivated;
        }
        catch (Exception ex)
        {
            Log.Write($"Ribbon.Shutdown: {ex}");
        }

        UnhookIdle();
        _workspaceHooked = false;
        _initialized = false;
    }

    // Chamado pelo comando CTTRIBBON: recoloca o painel na hora e abre uma janela curta.
    public static void Reconcile()
    {
        OpenWindow("comando CTTRIBBON");
        SafeEnsure("CTTRIBBON");
    }

    private static void OnRibbonPaletteSetCreated(object? sender, EventArgs e)
    {
        try
        {
            AcRibbonServices.RibbonPaletteSetCreated -= OnRibbonPaletteSetCreated;
            var paletteSet = AcRibbonServices.RibbonPaletteSet;
            if (paletteSet != null)
            {
                HookWorkspace(paletteSet);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Ribbon.OnRibbonPaletteSetCreated: {ex}");
        }

        OpenWindow("ribbon criada");
    }

    private static void HookWorkspace(Autodesk.AutoCAD.Ribbon.RibbonPaletteSet paletteSet)
    {
        if (_workspaceHooked)
        {
            return;
        }

        paletteSet.WorkspaceLoaded += OnWorkspaceLoaded;
        _workspaceHooked = true;
    }

    private static void OnWorkspaceLoaded(object? sender, EventArgs e) => OpenWindow("workspace carregado");

    private static void OnDocumentActivated(object? sender, DocumentCollectionEventArgs e) => OpenWindow("desenho ativado");

    private static void OpenWindow(string reason)
    {
        _windowUntil = Environment.TickCount64 + JanelaMs;
        _nextCheck = 0;

        if (_idleHooked)
        {
            return;
        }

        Application.Idle += OnIdle;
        _idleHooked = true;
        Log.Write($"Ribbon: janela de reconciliacao aberta ({reason})");
    }

    private static void UnhookIdle()
    {
        if (!_idleHooked)
        {
            return;
        }

        Application.Idle -= OnIdle;
        _idleHooked = false;
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        if (now >= _windowUntil)
        {
            UnhookIdle();
            Log.Write("Ribbon: janela de reconciliacao encerrada (Idle desligado)");
            return;
        }

        if (now < _nextCheck)
        {
            return;
        }

        _nextCheck = now + IntervaloChecagemMs;
        SafeEnsure("Idle");
    }

    private static void SafeEnsure(string origin)
    {
        try
        {
            EnsureRibbon();
        }
        catch (Exception ex)
        {
            Log.Write($"Ribbon.EnsureRibbon ERROR ({origin}): {ex}");
        }
    }

    private static void EnsureRibbon()
    {
        var ribbon = ComponentManager.Ribbon;
        if (ribbon == null)
        {
            return;
        }

        // Preferencia: aba "Plug-ins". A aba propria so existe enquanto a "Plug-ins" nao aparece;
        // quando ela aparece, a aba propria e removida para o painel nao ficar duplicado.
        var pluginsTab = FindPluginsTab(ribbon);
        var ownTab = FindOwnTab(ribbon);

        if (pluginsTab != null)
        {
            if (ownTab != null)
            {
                ribbon.Tabs.Remove(ownTab);
                Log.Write("Ribbon: aba propria 'Connection TT' removida (aba 'Plug-ins' disponivel)");
            }

            EnsurePanel(pluginsTab);
            return;
        }

        if (ownTab == null)
        {
            ownTab = new RibbonTab { Title = "Connection TT", Id = "ConnectionTT.Tab", IsActive = false };
            ribbon.Tabs.Add(ownTab);
            Log.Write("Ribbon: criada tab propria 'Connection TT'");
        }

        EnsurePanel(ownTab);
    }

    private static void EnsurePanel(RibbonTab tab)
    {
        if (PanelExists(tab))
        {
            return;
        }

        AddPanel(tab);
        Log.Write($"Ribbon: painel adicionado na tab '{tab.Title}' (Id={tab.Id})");
    }

    private static void AddPanel(RibbonTab tab)
    {
        var source = new RibbonPanelSource { Title = "Connection TT", Id = "ConnectionTT.Panel" };
        source.Items.Add(new RibbonButton
        {
            Text = "Conexões",
            ShowText = true,
            ShowImage = true,
            Image = ConnectionPalette.LoadImage("ribbon_16.png"),
            LargeImage = ConnectionPalette.LoadImage("ribbon_32.png"),
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            ToolTip = "Abre a paleta de conexões do Connection TT (CTTCONEXOES)",
            CommandHandler = new RelayCommand(ConnectionPalette.Toggle),
        });
        source.Items.Add(new RibbonButton
        {
            Text = "MCP Connector",
            ShowText = true,
            Size = RibbonItemSize.Large,
            CommandHandler = new RelayCommand(McpDialog.Show),
        });

        tab.Panels.Add(new RibbonPanel { Source = source });
    }

    private static bool PanelExists(RibbonTab tab)
    {
        foreach (var panel in tab.Panels)
        {
            if (panel.Source != null && panel.Source.Id == "ConnectionTT.Panel")
            {
                return true;
            }
        }

        return false;
    }

    private static RibbonTab? FindPluginsTab(RibbonControl ribbon)
    {
        foreach (var tab in ribbon.Tabs)
        {
            var id = (tab.Id ?? string.Empty).ToUpperInvariant();
            var uid = (tab.UID ?? string.Empty).ToUpperInvariant();
            var title = tab.Title ?? string.Empty;

            if (id == "ACAD.PLUGINS"
                || uid == "ACAD.PLUGINS"
                || id == "ID_ADDINS"
                || title.Equals("Plug-ins", StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }

        return null;
    }

    private static RibbonTab? FindOwnTab(RibbonControl ribbon)
    {
        foreach (var tab in ribbon.Tabs)
        {
            if (tab.Id == "ConnectionTT.Tab")
            {
                return tab;
            }
        }

        return null;
    }
}
