using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace HornetStudio.Editor.ViewModels;

public sealed class EditorDialogSection : ObservableObject
{
    private static readonly IReadOnlyList<EditorDialogField> EmptyFields = [];
    private readonly List<Func<EditorDialogField>> _fieldFactories = [];
    private bool _isExpanded;
    private bool _isLoadingFields;
    private int _nextFieldFactoryIndex;
    private int _fieldLoadGeneration;

    public EditorDialogSection(string title, bool isExpanded = false)
    {
        Title = title;
        _isExpanded = isExpanded;
    }

    public string Title { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ToggleGlyph));
            RaisePropertyChanged(nameof(VisibleFields));
        }
    }

    public string ToggleGlyph => IsExpanded ? "▼" : "▶";

    public ObservableCollection<EditorDialogField> Fields { get; } = [];

    public IReadOnlyList<EditorDialogField> VisibleFields => IsExpanded ? Fields : EmptyFields;

    public bool IsLoadingFields
    {
        get => _isLoadingFields;
        private set => SetProperty(ref _isLoadingFields, value);
    }

    public event Action? FieldsLoaded;

    public void AddFieldFactory(Func<EditorDialogField> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _fieldFactories.Add(factory);
    }

    public void BeginLoadFields(int batchSize = 2)
    {
        if (batchSize < 1)
        {
            batchSize = 1;
        }

        if (IsLoadingFields || _nextFieldFactoryIndex >= _fieldFactories.Count)
        {
            return;
        }

        IsLoadingFields = true;
        ScheduleFieldBatchLoad(batchSize, _fieldLoadGeneration);
    }

    public void CancelFieldLoading()
    {
        _fieldLoadGeneration++;
        IsLoadingFields = false;
    }

    private void ScheduleFieldBatchLoad(int batchSize, int generation)
    {
        Dispatcher.UIThread.Post(() => LoadFieldBatch(batchSize, generation), DispatcherPriority.Background);
    }

    private void LoadFieldBatch(int batchSize, int generation)
    {
        if (generation != _fieldLoadGeneration)
        {
            return;
        }

        var addedCount = 0;
        while (_nextFieldFactoryIndex < _fieldFactories.Count && addedCount < batchSize)
        {
            Fields.Add(_fieldFactories[_nextFieldFactoryIndex]());
            _nextFieldFactoryIndex++;
            addedCount++;
        }

        if (_nextFieldFactoryIndex < _fieldFactories.Count)
        {
            ScheduleFieldBatchLoad(batchSize, generation);
            return;
        }

        IsLoadingFields = false;
        FieldsLoaded?.Invoke();
    }
}
