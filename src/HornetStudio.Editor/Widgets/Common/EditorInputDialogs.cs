using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HornetStudio.Editor.Monitoring;

namespace HornetStudio.Editor.Widgets;

public static class EditorInputDialogs
{
    public static async Task<string?> EditTextAsync(Window owner, string header, string subHeader, string? initialValue = null)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackDialogOpen(owner, nameof(TextInputDialogWindow));
        var dialog = new TextInputDialogWindow();
        dialog.Initialize(owner.DataContext as HornetStudio.Editor.ViewModels.MainWindowViewModel, header, subHeader, initialValue);
        await dialog.ShowDialog(owner);
        return await dialog.WaitForResultAsync();
    }

    public static async Task<string?> EditTextAsync(Window owner, string header, string subHeader, string? initialValue, bool isPassword)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackDialogOpen(owner, nameof(TextInputDialogWindow));
        var dialog = new TextInputDialogWindow();
        dialog.Initialize(owner.DataContext as HornetStudio.Editor.ViewModels.MainWindowViewModel, header, subHeader, initialValue, isPassword);
        await dialog.ShowDialog(owner);
        return await dialog.WaitForResultAsync();
    }

    public static async Task<double?> EditNumericAsync(Window owner, string header, string subHeader, string format = "0.##", double? initialValue = null)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackDialogOpen(owner, nameof(NumericInputDialogWindow));
        var dialog = new NumericInputDialogWindow();
        dialog.DataContext = owner.DataContext;
        dialog.Initialize(header, subHeader, initialValue?.ToString(format, System.Globalization.CultureInfo.InvariantCulture), format);
        await dialog.ShowDialog(owner);
        return await dialog.WaitForResultAsync();
    }

    public static async Task<double?> EditNumericAsync(Window owner, string header, string subHeader, string format, double? initialValue, bool maskInput)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackDialogOpen(owner, nameof(NumericInputDialogWindow));
        var dialog = new NumericInputDialogWindow();
        dialog.DataContext = owner.DataContext;
        dialog.Initialize(header, subHeader, initialValue?.ToString(format, System.Globalization.CultureInfo.InvariantCulture), format, maskInput);
        await dialog.ShowDialog(owner);
        return await dialog.WaitForResultAsync();
    }

    public static async Task<ulong?> EditHexAsync(Window owner, string header, string subHeader, int digits = 8, ulong? initialValue = null)
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackDialogOpen(owner, nameof(HexInputDialogWindow));
        var dialog = new HexInputDialogWindow();
        dialog.DataContext = owner.DataContext;
        dialog.Initialize(header, subHeader, initialValue, digits);
        await dialog.ShowDialog(owner);
        return await dialog.WaitForResultAsync();
    }

    public static async Task<bool> ConfirmAsync(Window owner, string header, string subHeader, string confirmText = "OK", string cancelText = "Cancel")
    {
        using var diagnosticsScope = UiResponsivenessDiagnostics.TrackDialogOpen(owner, "ConfirmDialogWindow");
        var result = false;

        var confirmButton = new Button
        {
            Content = confirmText,
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelButton = new Button
        {
            Content = cancelText,
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var window = new Window
        {
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Title = header,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = header,
                            FontSize = 16,
                            FontWeight = FontWeight.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = subHeader,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 10,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { cancelButton, confirmButton }
                        }
                    }
                }
            }
        };

        confirmButton.Click += (_, _) =>
        {
            result = true;
            window.Close();
        };

        cancelButton.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
        return result;
    }
}
