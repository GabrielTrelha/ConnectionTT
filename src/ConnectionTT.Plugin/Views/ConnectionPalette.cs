using System;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ConnectionTT.Plugin;

// Paleta acoplavel do ConnectionTT (estilo Tool Palette / Connection Vault).
// API conferida no acmgd.dll do AutoCAD 2026 (REA): PaletteSet(string, Guid), AddVisual(string, Visual),
// Visible, Size, MinimumSize, Style, DockEnabled, Dock, DockSides.None.
//
// A paleta NAO e criada na carga do plugin: so quando o usuario clica no botao "Conexões" ou usa CTTCONEXOES.
// Usa-se o construtor SEM nome de comando. Com o construtor (nome, comando, Guid) o AutoCAD grava o comando
// e o executa na proxima inicializacao para "restaurar" a paleta que estava aberta - era isso que fazia a
// paleta abrir sozinha (e carregar imagens/WPF) toda vez que o programa abria.
public static class ConnectionPalette
{
    // GUID fixo: o AutoCAD guarda posicao/tamanho/acoplamento da paleta por este ID entre sessoes.
    private static readonly Guid PaletteId = new("8E0B5D3A-6C1F-4B7E-9A24-3F5C7D1E9B60");

    private static PaletteSet? _paletteSet;
    private static ConnectionPaletteView? _view;

    public static void Show()
    {
        try
        {
            if (_paletteSet == null)
            {
                _view = new ConnectionPaletteView(ConnectionCatalog.Load());
                _paletteSet = new PaletteSet("Connection TT", PaletteId)
                {
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                    MinimumSize = new System.Drawing.Size(240, 260),
                    DockEnabled = DockSides.Left | DockSides.Right,
                };
                _paletteSet.AddVisual("Conexões", _view);
                // Tamanho inicial conforme o painel de especificacoes (recolhido = so categorias + lista).
                _paletteSet.Size = new System.Drawing.Size(_view.DetailCollapsed ? 320 : 720, 480);
                _view.DetailWidthChanged += OnDetailWidthChanged;
                Log.Write("Paleta: criada");
            }

            _paletteSet.Visible = true;
        }
        catch (Exception ex)
        {
            Log.Write($"Paleta.Show ERRO: {ex}");
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\nConnection TT: erro ao abrir a paleta: {ex.Message}");
        }
    }

    // Recolher/expandir as especificacoes: com a paleta flutuando, ajusta a largura da janela junto.
    // Acoplada, quem manda na largura e o AutoCAD (o painel so some e a lista ocupa o espaco).
    private static void OnDetailWidthChanged(double delta)
    {
        try
        {
            if (_paletteSet == null || _paletteSet.Dock != DockSides.None || Math.Abs(delta) < 1)
            {
                return;
            }

            // delta vem em unidades WPF (DIP); Size e em pixels -> converte pela escala do monitor.
            double scale = _view != null ? System.Windows.Media.VisualTreeHelper.GetDpi(_view).DpiScaleX : 1.0;
            var size = _paletteSet.Size;
            int width = Math.Max(_paletteSet.MinimumSize.Width, size.Width + (int)Math.Round(delta * scale));
            _paletteSet.Size = new System.Drawing.Size(width, size.Height);
            Log.Write($"Paleta: largura {size.Width} -> {width}");
        }
        catch (Exception ex)
        {
            Log.Write($"Paleta: erro ajustando a largura: {ex.Message}");
        }
    }

    public static void Toggle()
    {
        if (_paletteSet != null && _paletteSet.Visible)
        {
            _paletteSet.Visible = false;
            return;
        }

        Show();
    }

    // Executa o comando da conexao no desenho ativo (cancela antes qualquer comando em andamento).
    public static void Run(CatalogConnection connection)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null)
        {
            Log.Write($"Paleta.Run: nenhum desenho ativo para '{connection.Id}'");
            return;
        }

        Log.Write($"Paleta.Run: {connection.Id} -> '{connection.Command}'");
        doc.SendStringToExecute("\u0003\u0003" + connection.Command.Trim() + "\n", true, false, true);
    }

    public static BitmapImage? LoadImage(string? fileName, int decodeWidth = 0)
    {
        var path = ConnectionCatalog.ImagePath(fileName);
        if (path == null || !System.IO.File.Exists(path))
        {
            if (path != null)
            {
                Log.Write($"Paleta: imagem nao encontrada '{path}'");
            }
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // nao deixa o arquivo travado
            image.UriSource = new Uri(path, UriKind.Absolute);
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Log.Write($"Paleta: erro carregando imagem '{path}': {ex.Message}");
            return null;
        }
    }
}
