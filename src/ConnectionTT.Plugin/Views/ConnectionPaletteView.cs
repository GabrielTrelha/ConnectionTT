using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ConnectionTT.Plugin;

// Conteudo da paleta, no mesmo arranjo do Connection Vault:
//   [categorias (icones)] | [lista de conexoes] | [figura + descricao (recolhivel)]
// Clique = executa o comando da conexao. Passar o mouse = mostra a figura/descricao.
// O painel de especificacoes (figura + descricao) pode ser recolhido pelo botao ◀/▶ do cabecalho;
// o estado fica salvo em %APPDATA%\ConnectionTT\paleta-detalhes.txt.
// Botao direito na conexao = adicionar/remover dos favoritos.
public sealed class ConnectionPaletteView : UserControl
{
    private const string FavoritesId = "__favoritos";

    private static readonly Brush BackBrush = MakeBrush("#2B303A");
    private static readonly Brush SideBrush = MakeBrush("#232731");
    private static readonly Brush ItemBrush = MakeBrush("#F4F5F7");
    private static readonly Brush ItemHoverBrush = MakeBrush("#FFFFFF");
    private static readonly Brush AccentBrush = MakeBrush("#E8792B");
    private static readonly Brush BorderGray = MakeBrush("#C9CDD4");
    private static readonly Brush TextLight = MakeBrush("#E6E8EC");
    private static readonly Brush TextDark = MakeBrush("#1E2229");

    private ConnectionCatalog _catalog;
    private string _categoryId = "";
    private CatalogConnection? _selected;

    private readonly StackPanel _categoryPanel = new() { Margin = new Thickness(6) };
    private readonly StackPanel _listPanel = new() { Margin = new Thickness(6, 0, 6, 6) };
    private readonly TextBlock _listTitle = new() { FontSize = 15, Foreground = TextLight, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    private readonly TextBlock _previewEmpty = new() { Text = "Passe o mouse sobre uma conexão", Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Foreground = TextDark, Margin = new Thickness(10), FontSize = 12.5 };
    private readonly Button _detailToggle = new() { Width = 26, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0) };
    private Grid _root = new();
    private Grid _detail = new();
    private bool _detailCollapsed;

    public bool DetailCollapsed => _detailCollapsed;

    // Avisa a paleta que a largura util mudou (recolher/expandir), para ela ajustar o tamanho quando flutuante.
    public event Action<double>? DetailWidthChanged;

    private static readonly string DetailStateFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConnectionTT", "paleta-detalhes.txt");

    public ConnectionPaletteView(ConnectionCatalog catalog)
    {
        _catalog = catalog;
        Background = BackBrush;
        FontFamily = new FontFamily("Segoe UI");

        var root = _root;
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240), MinWidth = 160 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });

        // Coluna 1: categorias
        var side = new Border { Background = SideBrush, Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _categoryPanel } };
        Grid.SetColumn(side, 0);
        root.Children.Add(side);

        // Coluna 2: lista
        var header = new DockPanel { Margin = new Thickness(6, 8, 6, 8), LastChildFill = true };
        var reload = new Button { Content = "⟳", ToolTip = "Recarregar catálogo", Width = 26, Height = 24, Padding = new Thickness(0) };
        reload.Click += (_, _) => Reload();
        DockPanel.SetDock(reload, Dock.Right);
        header.Children.Add(reload);
        _detailToggle.Click += (_, _) => SetDetailCollapsed(!_detailCollapsed, notify: true);
        DockPanel.SetDock(_detailToggle, Dock.Right);
        header.Children.Add(_detailToggle);
        header.Children.Add(_listTitle);

        var listColumn = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        listColumn.Children.Add(header);
        listColumn.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _listPanel });
        Grid.SetColumn(listColumn, 1);
        root.Children.Add(listColumn);

        // Coluna 3: figura + descricao (recolhivel)
        var detail = _detail;
        detail.Margin = new Thickness(6, 8, 8, 8);
        detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });

        var previewGrid = new Grid();
        previewGrid.Children.Add(_previewEmpty);
        previewGrid.Children.Add(_preview);
        var previewBox = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(4), Child = previewGrid };
        Grid.SetRow(previewBox, 0);
        detail.Children.Add(previewBox);

        var descBox = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(4),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _description },
        };
        Grid.SetRow(descBox, 2);
        detail.Children.Add(descBox);

        Grid.SetColumn(detail, 2);
        root.Children.Add(detail);

        Content = root;
        SetDetailCollapsed(ReadDetailState(), notify: false);

        _categoryId = _catalog.Connections.Any(c => Favorites.Contains(c.Id))
            ? FavoritesId
            : _catalog.Categories.FirstOrDefault()?.Id ?? FavoritesId;
        Refresh();
    }

    private void Reload()
    {
        _catalog = ConnectionCatalog.Load();
        if (_categoryId != FavoritesId && _catalog.Categories.All(c => c.Id != _categoryId))
        {
            _categoryId = _catalog.Categories.FirstOrDefault()?.Id ?? FavoritesId;
        }

        if (_selected != null)
        {
            _selected = _catalog.Connections.FirstOrDefault(c => c.Id == _selected.Id);
        }

        Refresh();
    }

    private void Refresh()
    {
        BuildCategories();
        BuildList();
        ShowDetail();
    }

    // ------------------------------------------------------------ categorias
    private void BuildCategories()
    {
        _categoryPanel.Children.Clear();
        _categoryPanel.Children.Add(CategoryButton(FavoritesId, "Favoritos", null, "★"));
        foreach (var category in _catalog.Categories)
        {
            var glyph = string.IsNullOrEmpty(category.Name) ? "?" : category.Name.Substring(0, 1).ToUpperInvariant();
            _categoryPanel.Children.Add(CategoryButton(category.Id, category.Name, category.Icon, glyph));
        }
    }

    private UIElement CategoryButton(string id, string name, string? icon, string glyph)
    {
        bool active = id == _categoryId;
        UIElement content;
        var image = ConnectionPalette.LoadImage(icon, 64);
        if (image != null)
        {
            content = new Image { Source = image, Width = 30, Height = 30 };
        }
        else
        {
            content = new TextBlock
            {
                Text = glyph,
                FontSize = 20,
                Foreground = id == FavoritesId ? Brushes.Goldenrod : TextDark,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var border = new Border
        {
            Width = 42,
            Height = 42,
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(4),
            Background = active ? ItemHoverBrush : ItemBrush,
            BorderBrush = active ? AccentBrush : BorderGray,
            BorderThickness = new Thickness(active ? 2 : 1),
            Child = content,
            ToolTip = name,
            Cursor = Cursors.Hand,
        };
        border.MouseLeftButtonUp += (_, _) =>
        {
            _categoryId = id;
            BuildCategories();
            BuildList();
        };
        return border;
    }

    // ------------------------------------------------------------ lista
    private void BuildList()
    {
        _listPanel.Children.Clear();
        IEnumerable<CatalogConnection> items;
        if (_categoryId == FavoritesId)
        {
            _listTitle.Text = "Favoritos";
            items = _catalog.Connections.Where(c => Favorites.Contains(c.Id));
        }
        else
        {
            _listTitle.Text = _catalog.Categories.FirstOrDefault(c => c.Id == _categoryId)?.Name ?? "";
            items = _catalog.Connections.Where(c => string.Equals(c.Category, _categoryId, StringComparison.OrdinalIgnoreCase));
        }

        var list = items.ToList();
        if (list.Count == 0)
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text = _categoryId == FavoritesId
                    ? "Nenhum favorito ainda.\nClique com o botão direito numa conexão para adicionar."
                    : "Nenhuma conexão nesta categoria.",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 0),
            });
            return;
        }

        foreach (var connection in list)
        {
            _listPanel.Children.Add(ConnectionItem(connection));
        }
    }

    private UIElement ConnectionItem(CatalogConnection connection)
    {
        bool selected = _selected?.Id == connection.Id;

        var grid = new Grid { Margin = new Thickness(6, 4, 8, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = ConnectionPalette.LoadImage(connection.Icon, 64);
        if (icon != null)
        {
            var image = new Image { Source = icon, Width = 32, Height = 32 };
            grid.Children.Add(image);
        }

        var label = new TextBlock
        {
            Text = (Favorites.Contains(connection.Id) ? "★ " : "") + connection.Name,
            Foreground = TextDark,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(4),
            Background = selected ? ItemHoverBrush : ItemBrush,
            BorderBrush = selected ? AccentBrush : BorderGray,
            BorderThickness = new Thickness(selected ? 2 : 1),
            Child = grid,
            ToolTip = connection.Command,
            Cursor = Cursors.Hand,
        };
        border.MouseEnter += (_, _) =>
        {
            if (_selected?.Id != connection.Id) border.Background = ItemHoverBrush;
            ShowDetail(connection);
        };
        border.MouseLeave += (_, _) =>
        {
            if (_selected?.Id != connection.Id) border.Background = ItemBrush;
            ShowDetail();
        };
        border.MouseLeftButtonDown += (_, e) =>
        {
            // Um clique ja executa. O segundo clique de um duplo clique e ignorado (nao roda duas vezes).
            if (e.ClickCount == 1)
            {
                Select(connection);
                ConnectionPalette.Run(connection);
            }
            e.Handled = true;
        };

        var menu = new ContextMenu();
        var fav = new MenuItem { Header = Favorites.Contains(connection.Id) ? "Remover dos favoritos" : "Adicionar aos favoritos" };
        fav.Click += (_, _) =>
        {
            Favorites.Toggle(connection.Id);
            BuildList();
        };
        menu.Items.Add(fav);
        border.ContextMenu = menu;

        return border;
    }

    private void Select(CatalogConnection connection)
    {
        _selected = connection;
        BuildList();
        ShowDetail();
    }

    // ------------------------------------------------------------ detalhe
    private void ShowDetail(CatalogConnection? shown = null)
    {
        shown ??= _selected;
        _description.Inlines.Clear();
        if (shown == null)
        {
            _preview.Source = null;
            _previewEmpty.Visibility = Visibility.Visible;
            _description.Inlines.Add(new Run("Passe o mouse sobre uma conexão para ver a figura e a descrição. Clique para criar a conexão."));
            return;
        }

        _preview.Source = ConnectionPalette.LoadImage(shown.Preview);
        _previewEmpty.Visibility = _preview.Source == null ? Visibility.Visible : Visibility.Collapsed;

        AddField("Ordem de seleção", shown.Selection);
        AddField("Perfis", shown.Profiles);
        AddField("Descrição", shown.Description);
        AddField("Opções", shown.Options);
        AddField("Comando", shown.Command);
    }

    // ------------------------------------------------------------ recolher especificacoes
    private void SetDetailCollapsed(bool collapsed, bool notify)
    {
        var column = _root.ColumnDefinitions[2];
        // Recolhido, a lista ocupa toda a largura que sobrar; expandido, volta a ter largura fixa.
        _root.ColumnDefinitions[1].Width = collapsed ? new GridLength(1, GridUnitType.Star) : new GridLength(240);
        double before = _detail.ActualWidth + _detail.Margin.Left + _detail.Margin.Right;

        _detailCollapsed = collapsed;
        _detail.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        column.MinWidth = collapsed ? 0 : 180;
        column.Width = collapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _detailToggle.Content = collapsed ? "▶" : "◀";
        _detailToggle.ToolTip = collapsed ? "Mostrar especificações" : "Recolher especificações";

        if (!notify)
        {
            return;
        }

        WriteDetailState(collapsed);
        // Recolher: diminui pela largura que o painel ocupava. Expandir: aumenta numa largura padrao.
        DetailWidthChanged?.Invoke(collapsed ? -Math.Max(before, 0) : 400);
    }

    private static bool ReadDetailState()
    {
        try
        {
            return System.IO.File.Exists(DetailStateFile) && System.IO.File.ReadAllText(DetailStateFile).Trim() == "1";
        }
        catch (Exception ex)
        {
            Log.Write($"Paleta: erro lendo '{DetailStateFile}': {ex.Message}");
            return false;
        }
    }

    private static void WriteDetailState(bool collapsed)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DetailStateFile)!);
            System.IO.File.WriteAllText(DetailStateFile, collapsed ? "1" : "0");
        }
        catch (Exception ex)
        {
            Log.Write($"Paleta: erro gravando '{DetailStateFile}': {ex.Message}");
        }
    }

    private void AddField(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (_description.Inlines.Count > 0)
        {
            _description.Inlines.Add(new LineBreak());
            _description.Inlines.Add(new LineBreak());
        }

        _description.Inlines.Add(new Bold(new Run(label + ": ")));
        _description.Inlines.Add(new Run(value));
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
