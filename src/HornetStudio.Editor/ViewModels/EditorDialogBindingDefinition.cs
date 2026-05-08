using System;
using System.Collections.Generic;
using ItemModel = Amium.Items.Item;
using Amium.Items;
using HornetStudio.Editor.Models;

namespace HornetStudio.Editor.ViewModels;

public sealed class EditorDialogBindingDefinition
{
    public EditorDialogBindingDefinition(
        string key,
        string label,
        EditorPropertyType propertyType,
        Func<FolderItemModel, string> readValue,
        Func<FolderItemModel, string, string?>? applyValue = null,
        bool isReadOnly = false,
        Func<FolderItemModel, IEnumerable<string>>? optionsFactory = null,
        Func<FolderItemModel, string>? toolTipFactory = null)
    {
        Key = key;
        Label = label;
        PropertyType = propertyType;
        ReadValue = readValue;
        ApplyValue = applyValue;
        IsReadOnly = isReadOnly;
        OptionsFactory = optionsFactory;
        ToolTipFactory = toolTipFactory;
    }

    public string Key { get; }

    public string Label { get; }

    public EditorPropertyType PropertyType { get; }

    public bool IsReadOnly { get; }

    public Func<FolderItemModel, string> ReadValue { get; }

    public Func<FolderItemModel, string, string?>? ApplyValue { get; }

    public Func<FolderItemModel, IEnumerable<string>>? OptionsFactory { get; }

    public Func<FolderItemModel, string>? ToolTipFactory { get; }

    public EditorDialogField CreateField(FolderItemModel item)
    {
        var parameterPath = string.IsNullOrWhiteSpace(item.Path) ? Key : $"{item.Path}.{Key}";
        var field = new EditorDialogField(this, new ItemProperty(Key, ReadValue(item), parameterPath));
        field.OwnerItem = item;
        if (OptionsFactory is not null)
        {
            foreach (var option in OptionsFactory(item))
            {
                field.Options.Add(option);
            }
        }

        field.ToolTipText = ToolTipFactory?.Invoke(item) ?? string.Empty;
        field.InitializeChartSeriesEditor();
        field.InitializeAttachItemEditor();
        field.InitializeInteractionRuleEditor();
        return field;
    }

    public string? Apply(FolderItemModel item, string value)
        => ApplyValue?.Invoke(item, value);
}
