using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConnectionTT.Plugin;

public sealed class McpDialog : Window
{
    private readonly TextBlock _status;
    private readonly CheckBox _toggle;

    private McpDialog()
    {
        Title = "Connection TT - MCP Connector";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(20) };

        stack.Children.Add(new TextBlock
        {
            Text = "MCP Connector",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _toggle = new CheckBox { Content = "Enable MCP", FontSize = 14 };
        _toggle.IsChecked = PluginApp.Server.IsRunning;
        _toggle.Checked += (_, _) => { PluginApp.Server.Start(); RefreshStatus(); };
        _toggle.Unchecked += (_, _) => { PluginApp.Server.Stop(); RefreshStatus(); };
        stack.Children.Add(_toggle);

        _status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold };
        stack.Children.Add(_status);

        stack.Children.Add(new TextBlock
        {
            Text = "Pipe: ConnectionTT.AdvanceSteel.v1\nQuando habilitado, o Bridge (opencode/Claude) consegue se conectar a esta instancia do Advance Steel.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 12, 0, 0),
        });

        var close = new Button
        {
            Content = "Close",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        close.Click += (_, _) => Close();
        stack.Children.Add(close);

        Content = stack;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var running = PluginApp.Server.IsRunning;
        _status.Text = running ? "Status: Active" : "Status: Disabled";
        _status.Foreground = running ? Brushes.Green : Brushes.Gray;
    }

    public static void Show()
    {
        var dialog = new McpDialog();
        dialog.ShowDialog();
    }
}
