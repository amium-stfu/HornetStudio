using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HornetStudio.Editor.Controls;
using HornetStudio.Editor.Models;
using HornetStudio.Editor.ViewModels;

namespace HornetStudio.Editor.Widgets;

public partial class EditorSignalControl : EditorTemplateWidget
{
    private FolderItemModel? Item => DataContext as FolderItemModel;

    private MainWindowViewModel? ViewModel
        => this.GetVisualRoot() is Window { DataContext: MainWindowViewModel viewModel } ? viewModel : null;

    public EditorSignalControl()
    {
        InitializeComponent();
        var parameterPresenter = this.FindControl<PropertyControl>("ParameterPresenter")!;
        parameterPresenter.BitChoiceClicked += OnBitChoiceClicked;
        parameterPresenter.BoolChoiceClicked += OnBoolChoiceClicked;
    }

    private void OnInteractivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HandleInteractivePointerPressed(e);
    }

    private async void OnParameterPressed(object? sender, PointerPressedEventArgs e)
    {
        var viewModel = ViewModel;
        if ((viewModel is { IsEditMode: true, IsShiftInteractionMode: false }) || Item is null)
        {
            return;
        }

        if (Item.TargetPropertyView.Definition.Kind == PropertyVisualKind.Bits)
        {
            e.Handled = true;
            return;
        }

        if (viewModel is null)
        {
            return;
        }

        var interactionEvent = GetInteractionEvent(e, sender as Control);
        if (interactionEvent is not null)
        {
            if (Item.TryGetOpenValueEditorTarget(interactionEvent.Value, out var explicitTargetPath))
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                {
                    await OpenValueDialogForTargetAsync(owner, viewModel, Item, explicitTargetPath);
                    e.Handled = true;
                }

                return;
            }

            if (Item.TryExecuteInteraction(interactionEvent.Value, viewModel, out _))
            {
                e.Handled = true;
                return;
            }

            if (interactionEvent != ItemInteractionEvent.BodyLeftClick || Item.HasInteractionRules || !Item.CanOpenValueEditor)
            {
                return;
            }
        }

        if (!Item.CanOpenValueEditor)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is Window defaultOwner)
        {
            await OpenValueDialogForTargetAsync(defaultOwner, viewModel, Item, null);
            e.Handled = true;
            return;
        }

        viewModel.OpenValueInput(Item);
        e.Handled = true;
    }

    private void OnBitChoiceClicked(object? sender, BitChoiceClickedEventArgs e)
    {
        var viewModel = ViewModel;
        if (Item is null || viewModel is { IsEditMode: true, IsShiftInteractionMode: false })
        {
            return;
        }

        _ = Item.TryToggleTargetBit(e.BitIndex, out _);
    }

    private void OnBoolChoiceClicked(object? sender, BoolChoiceClickedEventArgs e)
    {
        var viewModel = ViewModel;
        if (Item is null || viewModel is { IsEditMode: true, IsShiftInteractionMode: false })
        {
            return;
        }

        _ = Item.TrySendInput(e.Value, out _);
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        HandleSettingsClicked(e);
    }

    private static ItemInteractionEvent? GetInteractionEvent(PointerPressedEventArgs e, Control? control)
    {
        var point = e.GetCurrentPoint(control);
        return point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => ItemInteractionEvent.BodyLeftClick,
            PointerUpdateKind.RightButtonPressed => ItemInteractionEvent.BodyRightClick,
            _ => null
        };
    }

    private static async Task OpenValueDialogForTargetAsync(Window owner, MainWindowViewModel viewModel, FolderItemModel sourceItem, string? targetPath)
    {
        var target = viewModel.ResolveValueInputTarget(targetPath, sourceItem);
        if (target is null || !target.CanOpenValueEditor)
        {
            return;
        }

        var presentation = target.TargetPropertyView;
        var definition = presentation.Definition;
        var header = target.ValueEditorTitle;
        var subHeader = presentation.UnitText ?? string.Empty;

        switch (definition.Kind)
        {
            case PropertyVisualKind.Text:
            {
                var initialText = presentation.Property?.Value?.ToString() ?? string.Empty;
                var result = await EditorInputDialogs.EditTextAsync(owner, header, subHeader, initialText);
                if (result is not null)
                {
                    string error;
                    target.TrySendInput(result, out error);
                }

                break;
            }

            case PropertyVisualKind.Numeric:
            {
                double? initial = null;
                if (presentation.Property?.Value is IConvertible convertible)
                {
                    try
                    {
                        initial = Convert.ToDouble(convertible, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        initial = null;
                    }
                }

                var format = string.IsNullOrWhiteSpace(definition.PatternOrOptionsText)
                    ? "0.##"
                    : definition.PatternOrOptionsText;

                var result = await EditorInputDialogs.EditNumericAsync(owner, header, subHeader, format, initial);
                if (result.HasValue)
                {
                    string error;
                    target.TrySendInput(result.Value, out error);
                }

                break;
            }

            case PropertyVisualKind.Hex:
            {
                ulong? initial = null;
                if (presentation.Property?.Value is { } raw)
                {
                    initial = ToUInt64(raw);
                }

                var digits = 0;
                if (!string.IsNullOrWhiteSpace(definition.PatternOrOptionsText)
                    && int.TryParse(definition.PatternOrOptionsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDigits)
                    && parsedDigits > 0)
                {
                    digits = parsedDigits;
                }

                var result = await EditorInputDialogs.EditHexAsync(owner, header, subHeader, digits, initial);
                if (result.HasValue)
                {
                    string error;
                    target.TrySendInput(result.Value, out error);
                }

                break;
            }

            default:
                break;
        }
    }

    private static ulong ToUInt64(object value)
    {
        return value switch
        {
            byte byteValue => byteValue,
            sbyte sbyteValue => unchecked((ulong)sbyteValue),
            short shortValue => unchecked((ulong)shortValue),
            ushort ushortValue => ushortValue,
            int intValue => unchecked((ulong)intValue),
            uint uintValue => uintValue,
            long longValue => unchecked((ulong)longValue),
            ulong ulongValue => ulongValue,
            float floatValue => unchecked((ulong)floatValue),
            double doubleValue => unchecked((ulong)doubleValue),
            decimal decimalValue => unchecked((ulong)decimalValue),
            string text when ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0UL
        };
    }
}

public partial class EditorSignalWidget : EditorSignalControl
{
}

public partial class EditorItemControl : EditorSignalControl
{
}

public partial class EditorItemWidget : EditorSignalControl
{
}
