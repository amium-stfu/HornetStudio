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
            PropertyName = parameterName;
        }

        /// <summary>
        /// Gets the item that raised the change notification.
        /// </summary>
        public Item Item { get; }

        /// <summary>
        /// Gets the name of the parameter that changed.
        /// </summary>
        public string PropertyName { get; }
    }

    /// <summary>
    /// Represents a named item property with change tracking.
    /// </summary>
    public sealed class ItemProperty
    {
        /// <summary>
        /// Occurs when the property value changes.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Gets the property name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the fully qualified property path.
        /// </summary>
        public string Path { get; set; } = string.Empty;
        private object? _value;

        /// <summary>
        /// Stores the last update timestamp in Unix milliseconds.
        /// </summary>
        public ulong LastUpdate;

        /// <summary>
        /// Gets or sets the current property value.
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
                    throw new InvalidCastException($"Cannot assign value of type '{value.GetType().FullName}' to property '{Path.Replace("/", ".")}' of type '{_value?.GetType()}'.");

                _value = value;
                LastUpdate = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemProperty"/> class.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">The initial property value.</param>
        /// <param name="path">The fully qualified property path.</param>
        public ItemProperty(string name, object? value, string path = "")
        {

            Name = name;
            Path = path;
            _value = value;
            LastUpdate = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Returns a textual representation of the property.
        /// </summary>
        /// <returns>A string containing the path, value, and runtime type.</returns>
        public override string ToString()
        {
            return $"Path: {Path.Replace("/", ".")}.{Name}: {_value} (Type: {_value?.GetType().FullName ?? "UnknownType"})";
        }
    }

    /// <summary>
    /// Represents an item with child items and named properties.
    /// </summary>
    public class Item : ItemDictionary
    {
        private const string DefaultValuePropertyName = "read";
        private const string FallbackValuePropertyName = "value";

        /// <summary>
        /// Occurs when one of the item's properties changes.
        /// </summary>
        public event EventHandler<ItemChangedEventArgs>? Changed;

        /// <summary>
        /// Gets or sets the property collection of this item.
        /// </summary>
        public ItemPropertyDictionary Properties { get; set; }

        /// <summary>
        /// Gets the item name stored in the <c>name</c> property.
        /// </summary>
        public string? Name => Properties["name"].Value?.ToString();

        /// <summary>
        /// Gets the item path stored in the <c>path</c> property.
        /// </summary>
        public string? Path => Properties["path"].Value?.ToString();

        /// <summary>
        /// Initializes a new item with the specified name.
        /// </summary>
        /// <param name="name">The item name.</param>
        public Item(string name)
        {
            _path = ItemPath.Combine(_path, name);
            Properties = new ItemPropertyDictionary(_path, OnItemPropertyChanged);
            Properties["name"].Value = name;
            Properties["path"].Value = _path;
            Properties[DefaultValuePropertyName].Value = null!;
            Properties["meta"].Value = "{}";
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
        /// <param name="hasWriteChannel">A value indicating whether the item exposes a separate write channel.</param>
        /// <param name="hasReadChannel">A value indicating whether the item exposes a readable value channel.</param>
        public Item(string name, object? value = null, string? path = null, bool hasWriteChannel = false, bool hasReadChannel = true)
        {
            if (path == null)
            {
                _path = ItemPath.Normalize(name);
            }
            else
            {
                _path = ItemPath.Combine(path, name);
            }
            Properties = new ItemPropertyDictionary(_path, OnItemPropertyChanged);
            Properties["name"].Value = name;
            Properties["path"].Value = _path;
            if (hasReadChannel)
            {
                Properties[DefaultValuePropertyName].Value = value!;
            }
            else if (value is not null)
            {
                Properties[FallbackValuePropertyName].Value = value;
            }

            if (hasWriteChannel)
            {
                Properties["write"].Value = null!;
            }

            Properties["meta"].Value = "{}";

        }

        /// <summary>
        /// Gets the value stored in the <c>read</c> property and writes through <c>write</c> when available.
        /// </summary>
        public dynamic Value
        {
            get
            {
                if (Properties.Has(DefaultValuePropertyName))
                {
                    return Properties[DefaultValuePropertyName].Value;
                }

                if (Properties.Has(FallbackValuePropertyName))
                {
                    return Properties[FallbackValuePropertyName].Value;
                }

                return null!;
            }

            set
            {
                var targetPropertyName = Properties.Has("write")
                    ? "write"
                    : (Properties.Has(DefaultValuePropertyName) ? DefaultValuePropertyName : FallbackValuePropertyName);
                Properties[targetPropertyName].Value = value!;
            }
        }

        /// <summary>
        /// Gets the value of a named property.
        /// </summary>
        /// <param name="propertyName">The name of the property to read.</param>
        /// <returns>The current value of the requested property.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the property does not exist.</exception>
        public dynamic GetPropertyValue(string propertyName)
        {
            if (!Properties.Has(propertyName))
                throw new KeyNotFoundException($"Property '{propertyName}' not found in item '{Name}'.");
            return Properties[propertyName].Value;
        }

        private void OnItemPropertyChanged(ItemProperty parameter)
        {
            Changed?.Invoke(this, new ItemChangedEventArgs(this, parameter.Name));
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
    /// Provides dictionary-style access to item properties.
    /// </summary>
    public class ItemPropertyDictionary
    {
        internal string _path = "";
        internal ConcurrentDictionary<string, ItemProperty> Dictionary = new ConcurrentDictionary<string, ItemProperty>();
        private readonly Action<ItemProperty>? _onParameterChanged;

        /// <summary>
        /// Removes all properties.
        /// </summary>
        public void Clear()
        {
            Dictionary.Clear();
        }

        /// <summary>
        /// Returns a copy of the current property dictionary.
        /// </summary>
        /// <returns>A new dictionary containing the current properties.</returns>
        public ConcurrentDictionary<string, ItemProperty> GetDictionary() => new ConcurrentDictionary<string, ItemProperty>(Dictionary);

        /// <summary>
        /// Replaces the internal property dictionary and reattaches change handlers.
        /// </summary>
        /// <param name="newDictionary">The new property dictionary.</param>
        public void SetDictionary(ConcurrentDictionary<string, ItemProperty> newDictionary)
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
        /// Initializes a new property dictionary for the specified path.
        /// </summary>
        /// <param name="path">The base path used for lazily created properties.</param>
        /// <param name="onParameterChanged">An optional callback invoked when a property changes.</param>
        public ItemPropertyDictionary(string path, Action<ItemProperty>? onParameterChanged = null)
        {
            _path = path;
            _onParameterChanged = onParameterChanged;
        }

        /// <summary>
        /// Determines whether a property with the specified key exists.
        /// </summary>
        /// <param name="id">The property key.</param>
        /// <returns><see langword="true"/> if the key exists; otherwise, <see langword="false"/>.</returns>
        public bool Has(string id)
        {
            return Dictionary.ContainsKey(id);
        }

        /// <summary>
        /// Removes the property with the specified key.
        /// </summary>
        /// <param name="id">The property key to remove.</param>
        public void Remove(string id)
        {
            Dictionary.TryRemove(id, out _);
        }


        /// <summary>
        /// Gets or sets a property by key.
        /// </summary>
        /// <param name="id">The property key.</param>
        /// <value>The property stored for the specified key.</value>
        public ItemProperty this[string id]
        {
            get
            {
                string path = string.IsNullOrWhiteSpace(_path) ? id : _path + "." + id;
                return Dictionary.GetOrAdd(id, key =>
                {
                    var parameter = new ItemProperty(key, null, path);
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
            if (sender is ItemProperty parameter)
            {
                _onParameterChanged?.Invoke(parameter);
            }
        }
    }


}
