using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Amium.Items
{
    /// <summary>
    /// Provides data for item change notifications.
    /// </summary>
    public sealed class ItemChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ItemChangedEventArgs"/> class.
        /// </summary>
        /// <param name="item">The item whose parameter changed.</param>
        /// <param name="parameterName">The name of the changed parameter.</param>
        public ItemChangedEventArgs(Item item, string parameterName)
        {
            Item = item;
            ParameterName = parameterName;
        }

        /// <summary>
        /// Gets the item that raised the change notification.
        /// </summary>
        public Item Item { get; }

        /// <summary>
        /// Gets the name of the parameter that changed.
        /// </summary>
        public string ParameterName { get; }
    }

    /// <summary>
    /// Represents a named item parameter with change tracking.
    /// </summary>
    public sealed class Parameter
    {
        /// <summary>
        /// Occurs when the parameter value changes.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Gets the parameter name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the fully qualified parameter path.
        /// </summary>
        public string Path { get; set; } = string.Empty;
        private object? _value;

        /// <summary>
        /// Stores the last update timestamp in Unix milliseconds.
        /// </summary>
        public ulong LastUpdate;

        /// <summary>
        /// Gets or sets the current parameter value.
        /// </summary>
        public dynamic Value
        {
            get => _value!;
            set
            {
                if (value is null)
                {
                    _value = null;
                    LastUpdate = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    Changed?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (value.GetType() != _value?.GetType() && _value is not null)
                    throw new InvalidCastException($"Cannot assign value of type '{value.GetType().FullName}' to parameter '{Path.Replace("/", ".")}' of type '{_value?.GetType()}'.");

                _value = value;
                LastUpdate = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Parameter"/> class.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The initial parameter value.</param>
        /// <param name="path">The fully qualified parameter path.</param>
        public Parameter(string name, object? value, string path = "")
        {

            Name = name;
            Path = path;
            _value = value;
            LastUpdate = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Returns a textual representation of the parameter.
        /// </summary>
        /// <returns>A string containing the path, value, and runtime type.</returns>
        public override string ToString()
        {
            return $"Path: {Path.Replace("/", ".")}.{Name}: {_value} (Type: {_value?.GetType().FullName ?? "UnknownType"})";
        }
    }

    /// <summary>
    /// Represents an item with child items and named parameters.
    /// </summary>
    public class Item : ItemDictionary
    {
        /// <summary>
        /// Occurs when one of the item's parameters changes.
        /// </summary>
        public event EventHandler<ItemChangedEventArgs>? Changed;

        /// <summary>
        /// Gets or sets the parameter collection of this item.
        /// </summary>
        public ParameterDictionary Params { get; set; }

        /// <summary>
        /// Gets the item name stored in the <c>Name</c> parameter.
        /// </summary>
        public string? Name => Params["Name"].Value?.ToString();

        /// <summary>
        /// Gets the item path stored in the <c>Path</c> parameter.
        /// </summary>
        public string? Path => Params["Path"].Value?.ToString();

        /// <summary>
        /// Initializes a new item with the specified name.
        /// </summary>
        /// <param name="name">The item name.</param>
        public Item(string name)
        {
            _path = CombinePath(_path, name);
            Params = new ParameterDictionary(_path, OnParameterChanged);
            Params["Name"].Value = name;
            Params["Path"].Value = _path;
            Params["Value"].Value = null!;
        }

        /// <summary>
        /// Ensures a child item with the specified name exists.
        /// </summary>
        /// <param name="name">The child item name.</param>
        public void AddItem(string name)
        {
            var item = this[name];

        }


        /// <summary>
        /// Initializes a new item with the specified name, value, and optional parent path.
        /// </summary>
        /// <param name="name">The item name.</param>
        /// <param name="value">The initial item value.</param>
        /// <param name="path">The parent path used to build the full item path.</param>
        public Item(string name, object? value = null, string? path = null)
        {
            if (path == null)
            {
                _path = name;
            }
            else
            {
                _path = CombinePath(path, name);
            }
            Params = new ParameterDictionary(_path, OnParameterChanged);
            Params["Name"].Value = name;
            Params["Path"].Value = _path;
            Params["Value"].Value = value!;

        }

        /// <summary>
        /// Gets or sets the value stored in the <c>Value</c> parameter.
        /// </summary>
        public dynamic Value
        {
            get
            {
                return Params["Value"].Value;
            }

            set
            {
                Params["Value"].Value = value!;
            }
        }

        /// <summary>
        /// Gets the value of a named parameter.
        /// </summary>
        /// <param name="paramName">The name of the parameter to read.</param>
        /// <returns>The current value of the requested parameter.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the parameter does not exist.</exception>
        public dynamic GetParamter(string paramName)
        {
            if (!Params.Has(paramName))
                throw new KeyNotFoundException($"Parameter '{paramName}' not found in item '{Name}'.");
            return Params[paramName].Value;
        }

        private void OnParameterChanged(Parameter parameter)
        {
            Changed?.Invoke(this, new ItemChangedEventArgs(this, parameter.Name));
        }

        private static string CombinePath(string? path, string name)
        {
            var normalizedParent = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('/', '.').Replace('\\', '.').Trim('.');
            var normalizedName = string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : name.Replace('/', '.').Replace('\\', '.').Trim('.');

            if (string.IsNullOrWhiteSpace(normalizedParent))
            {
                return normalizedName;
            }

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return normalizedParent;
            }

            return normalizedParent + "." + normalizedName;
        }
    }



    /// <summary>
    /// Provides dictionary-style access to child items.
    /// </summary>
    public class ItemDictionary
    {

        internal ConcurrentDictionary<string, Item> Dictionary = new ConcurrentDictionary<string, Item>();

        /// <summary>
        /// Returns a copy of the current child item dictionary.
        /// </summary>
        /// <returns>A new dictionary containing the current child items.</returns>
        public ConcurrentDictionary<string, Item> GetDictionary() => new ConcurrentDictionary<string, Item>(Dictionary);

        /// <summary>
        /// Replaces the internal child item dictionary.
        /// </summary>
        /// <param name="newDictionary">The new dictionary to store.</param>
        public void SetDictionary(ConcurrentDictionary<string, Item> newDictionary)
        {
            Dictionary = newDictionary;
        }


        internal string _path = "";

        /// <summary>
        /// Initializes a new empty item dictionary.
        /// </summary>
        public ItemDictionary() { }

        /// <summary>
        /// Initializes a new item dictionary for the specified path.
        /// </summary>
        /// <param name="path">The base path used for lazily created items.</param>
        public ItemDictionary(string path)
        {
            _path = path;
        }

        /// <summary>
        /// Removes all child items.
        /// </summary>
        public void Clear()
        {
            Dictionary.Clear();
        }


        /// <summary>
        /// Determines whether a child item with the specified key exists.
        /// </summary>
        /// <param name="id">The child item key.</param>
        /// <returns><see langword="true"/> if the key exists; otherwise, <see langword="false"/>.</returns>
        public bool Has(string id)
        {
            return Dictionary.ContainsKey(id);
        }

        /// <summary>
        /// Removes the child item with the specified key.
        /// </summary>
        /// <param name="id">The child item key to remove.</param>
        public void Remove(string id)
        {
            Dictionary.TryRemove(id, out _);
        }

        private void Add(string id, Item item)
        {
            if (!Dictionary.TryAdd(id, item))
                throw new ArgumentException($"An element with the key '{id}' already exists.", nameof(id));
        }

            /// <summary>
            /// Gets or sets a child item by key.
            /// </summary>
            /// <param name="id">The child item key.</param>
            /// <value>The child item stored for the specified key.</value>
        public Item this[string id]
        {
            get
            {
                var path = _path;
                return Dictionary.GetOrAdd(id, key => new Item(key, path: path));
            }
            set
            {
                if (value == null)
                {
                    return;
                }
                Dictionary[id] = value;

            }
        }
    }

    /// <summary>
    /// Provides dictionary-style access to item parameters.
    /// </summary>
    public class ParameterDictionary
    {
        internal string _path = "";
        internal ConcurrentDictionary<string, Parameter> Dictionary = new ConcurrentDictionary<string, Parameter>();
        private readonly Action<Parameter>? _onParameterChanged;

        /// <summary>
        /// Removes all parameters.
        /// </summary>
        public void Clear()
        {
            Dictionary.Clear();
        }

        /// <summary>
        /// Returns a copy of the current parameter dictionary.
        /// </summary>
        /// <returns>A new dictionary containing the current parameters.</returns>
        public ConcurrentDictionary<string, Parameter> GetDictionary() => new ConcurrentDictionary<string, Parameter>(Dictionary);

        /// <summary>
        /// Replaces the internal parameter dictionary and reattaches change handlers.
        /// </summary>
        /// <param name="newDictionary">The new parameter dictionary.</param>
        public void SetDictionary(ConcurrentDictionary<string, Parameter> newDictionary)
        {
            foreach (var parameter in Dictionary.Values)
            {
                parameter.Changed -= OnParameterChanged;
            }

            Dictionary = newDictionary;

            foreach (var parameter in Dictionary.Values)
            {
                parameter.Changed += OnParameterChanged;
            }
        }

        /// <summary>
        /// Initializes a new parameter dictionary for the specified path.
        /// </summary>
        /// <param name="path">The base path used for lazily created parameters.</param>
        /// <param name="onParameterChanged">An optional callback invoked when a parameter changes.</param>
        public ParameterDictionary(string path, Action<Parameter>? onParameterChanged = null)
        {
            _path = path;
            _onParameterChanged = onParameterChanged;
        }

        /// <summary>
        /// Determines whether a parameter with the specified key exists.
        /// </summary>
        /// <param name="id">The parameter key.</param>
        /// <returns><see langword="true"/> if the key exists; otherwise, <see langword="false"/>.</returns>
        public bool Has(string id)
        {
            return Dictionary.ContainsKey(id);
        }

        /// <summary>
        /// Removes the parameter with the specified key.
        /// </summary>
        /// <param name="id">The parameter key to remove.</param>
        public void Remove(string id)
        {
            Dictionary.TryRemove(id, out _);
        }


        /// <summary>
        /// Gets or sets a parameter by key.
        /// </summary>
        /// <param name="id">The parameter key.</param>
        /// <value>The parameter stored for the specified key.</value>
        public Parameter this[string id]
        {
            get
            {
                string path = string.IsNullOrWhiteSpace(_path) ? id : _path + "." + id;
                return Dictionary.GetOrAdd(id, key =>
                {
                    var parameter = new Parameter(key, null, path);
                    parameter.Changed += OnParameterChanged;
                    return parameter;
                });
            }
            set
            {
                if (value == null)
                {
                    return;
                }

                value.Changed -= OnParameterChanged;
                value.Changed += OnParameterChanged;
                Dictionary[id] = value;
            }
        }

        private void OnParameterChanged(object? sender, EventArgs e)
        {
            if (sender is Parameter parameter)
            {
                _onParameterChanged?.Invoke(parameter);
            }
        }
    }


}
