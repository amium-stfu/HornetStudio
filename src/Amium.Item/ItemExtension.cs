using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Amium.Items
{
    /// <summary>
    /// Provides serialization and cloning helpers for <see cref="Item"/> and <see cref="Parameter"/> instances.
    /// </summary>
    public static class ItemExtension
    {



        #region Item to json string

        /// <summary>
        /// Converts the specified item to its JSON string representation.
        /// </summary>
        /// <remarks>If the item's name is null, 'RootItem' will be used as the key in the JSON
        /// output.</remarks>
        /// <param name="item">The item to be converted to JSON. Must not be null.</param>
        /// <returns>A JSON string representing the item, formatted with indentation for readability.</returns>
        public static string ToJsonString(this Item item)
        {
            var payload = new Dictionary<string, object?>
            {
                { item.Name ?? "RootItem", BuildItemNode(item) }
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        private static Dictionary<string, object?> BuildItemNode(Item item)
        {
            var parameters = new Dictionary<string, Dictionary<string, object?>>();
            foreach (var param in item.Params.Dictionary.Values)
            {
                parameters[param.Name] = ParseParameterJson(param.ParameterToJsonString());
            }

            var items = new Dictionary<string, object?>();
            foreach (Item subItem in item.GetDictionary().Values)
            {
                items[subItem.Name ?? "UnnamedItem"] = BuildItemNode(subItem);
            }

            return new Dictionary<string, object?>
            {
                { "Parameter", parameters },
                { "Items", items }
            };
        }

        private static Dictionary<string, object?> ParseParameterJson(string parameterJson)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(parameterJson)
                ?? new Dictionary<string, object?>();
        }

        /// <summary>
        /// Converts the specified parameter to its JSON string representation.
        /// </summary>
        /// <param name="parameter">The parameter to serialize.</param>
        /// <returns>A JSON string representing the parameter metadata and value.</returns>
        public static string ParameterToJsonString(this Parameter parameter)
        {
            var runtimeType = parameter.Value?.GetType();

            Dictionary<string, string?> payload = new Dictionary<string, string?>
            {
                { "Name", parameter.Name } ,
                { "Value", SerializeParameterValue(parameter.Value, runtimeType) },
                { "LastUpdate", parameter.LastUpdate.ToString() },
                { "Type", runtimeType?.AssemblyQualifiedName ?? "UnknownType" }
            };

            return System.Text.Json.JsonSerializer.Serialize(payload);
        }

        private static string SerializeParameterValue(object? value, Type? runtimeType)
        {
            if (value is null)
                return "null";

            try
            {
                var options = new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.IgnoreCycles
                };

                return JsonSerializer.Serialize(value, runtimeType ?? value.GetType(), options);
            }
            catch
            {
                return JsonSerializer.Serialize(value.ToString());
            }
        }

        #endregion


        #region Copy / Clone Item

        /// <summary>
        /// Creates a new instance of the Item class that is a copy of the specified item.
        /// </summary>
        /// <remarks>This method performs a deep copy of the item, ensuring that the cloned item has its
        /// own separate instances of any referenced objects.</remarks>
        /// <param name="item">The item to clone. This parameter cannot be null.</param>
        /// <returns>A new Item instance that is a clone of the specified item, with the same properties and parameters.</returns>
        public static Item Clone(this Item item)
        {
            var clonedItem = new Item(item.Name ?? "UnnamedItem");
            clonedItem._path = item._path;
            clonedItem.Params.SetDictionary(CloneParameters(item.Params.GetDictionary()));
            clonedItem.SetDictionary(CloneItems(item.GetDictionary()));
            return clonedItem;
        }

        private static ConcurrentDictionary<string, Item> CloneItems(ConcurrentDictionary<string, Item> items)
        {
            var clonedItems = new ConcurrentDictionary<string, Item>();
            foreach (var entry in items)
            {
                clonedItems[entry.Key] = entry.Value.Clone();
            }

            return clonedItems;
        }

        private static ConcurrentDictionary<string, Parameter> CloneParameters(ConcurrentDictionary<string, Parameter> parameters)
        {
            var clonedParameters = new ConcurrentDictionary<string, Parameter>();
            foreach (var entry in parameters)
            {
                clonedParameters[entry.Key] = CloneParameter(entry.Value);
            }

            return clonedParameters;
        }

        private static Parameter CloneParameter(Parameter parameter)
        {
            var clonedParameter = new Parameter(
                parameter.Name,
                CloneParameterValue(parameter.Value),
                parameter.Path);

            clonedParameter.LastUpdate = parameter.LastUpdate;
            return clonedParameter;
        }

        private static object? CloneParameterValue(object? value)
        {
            if (value is null)
                return null;

            var runtimeType = value.GetType();
            if (runtimeType == typeof(string) || runtimeType.IsValueType)
                return value;

            try
            {
                var serializedValue = JsonSerializer.Serialize(value, runtimeType);
                return JsonSerializer.Deserialize(serializedValue, runtimeType);
            }
            catch
            {
                return value;
            }
        }


        #endregion


        #region Item from Json
        /// <summary>
        /// Deserializes a JSON string into an instance of the Item class.
        /// </summary>
        /// <remarks>This method expects the JSON string to represent a single item in the format of a
        /// JSON object. Ensure that the JSON structure matches the expected format for successful
        /// deserialization.</remarks>
        /// <param name="json">The JSON string representing the item to be deserialized. Must be a valid JSON object.</param>
        /// <returns>An instance of the Item class populated with data from the provided JSON string.</returns>
        /// <exception cref="ArgumentException">Thrown if the JSON format is invalid or does not represent a valid Item.</exception>
        public static Item ItemFromJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            var rootElement = document.RootElement;

            if (rootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Invalid JSON format for Item.");

            var rootEnumerator = rootElement.EnumerateObject();
            if (!rootEnumerator.MoveNext())
                throw new ArgumentException("Invalid JSON format for Item.");

            var rootProperty = rootEnumerator.Current;
            var rootItem = new Item(rootProperty.Name);
            BuildItemFromJson(rootItem, rootProperty.Value);
            return rootItem;
        }

        private static void BuildItemFromJson(Item item, JsonElement jsonData)
        {
            if (jsonData.ValueKind != JsonValueKind.Object)
                return;

            if (jsonData.TryGetProperty("Parameter", out var paramElement) && paramElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var param in paramElement.EnumerateObject())
                {
                    item.Params[param.Name] = ParameterFromJson(param.Value.GetRawText());
                }
            }

            if (jsonData.TryGetProperty("Items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var subItem in itemsElement.EnumerateObject())
                {
                    var subItemInstance = new Item(subItem.Name);
                    BuildItemFromJson(subItemInstance, subItem.Value);
                    item[subItem.Name] = subItemInstance;
                }
            }
        }


        /// <summary>
        /// Deserializes a JSON string into a Parameter object, extracting the parameter's name and value from the
        /// provided data.
        /// </summary>
        /// <remarks>If the 'Type' or 'Value' keys are missing from the JSON, default values are used. The
        /// method attempts to resolve the type from the 'Type' key and convert the 'Value' accordingly.</remarks>
        /// <param name="json">A JSON-formatted string that represents a parameter. The string must contain a 'Name' key and may optionally
        /// include 'Type' and 'Value' keys.</param>
        /// <returns>A Parameter object initialized with the name and value extracted from the JSON string.</returns>
        /// <exception cref="ArgumentException">Thrown if the JSON string is null, invalid, or does not contain the required 'Name' key.</exception>
        public static Parameter ParameterFromJson(string json)
        {
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (deserialized == null || !deserialized.ContainsKey("Name"))
                throw new ArgumentException("Invalid JSON format for Parameter.");
            var name = deserialized["Name"].GetString() ?? "UnnamedParameter";

            string typeName = deserialized.TryGetValue("Type", out var typeElement)
                ? typeElement.GetString() ?? "UnknownType"
                : "UnknownType";
            string valueJson = deserialized.TryGetValue("Value", out var valueElement)
                ? valueElement.GetString() ?? "null"
                : "null";

            Type? type = ResolveType(typeName);
            object? value = ConvertToType(valueJson, type);

            return new Parameter(name, value);
        }

        private static object? ConvertValueToType(string json)
        {
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (deserialized == null || !deserialized.ContainsKey("Name"))
                throw new ArgumentException("Invalid JSON format for Parameter.");

            string typeName = deserialized.TryGetValue("Type", out var typeElement)
                ? typeElement.GetString() ?? "UnknownType"
                : "UnknownType";
            string valueJson = deserialized.TryGetValue("Value", out var valueElement)
                ? valueElement.GetString() ?? "null"
                : "null";

            Type? type = ResolveType(typeName);

            return ConvertToType(valueJson, type);

        }

        private static Type? ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeName == "UnknownType")
                return null;

            Type? type = Type.GetType(typeName, throwOnError: false);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static object? ConvertToType(string valueJson, Type? type)
        {
            if (type == null || valueJson == "null")
                return null;

            try
            {
                return JsonSerializer.Deserialize(valueJson, type);
            }
            catch
            {
            }

            string valueStr;
            try
            {
                valueStr = JsonSerializer.Deserialize<string>(valueJson) ?? valueJson;
            }
            catch
            {
                valueStr = valueJson;
            }

            try
            {
                if (type == typeof(string))
                    return valueStr;

                if (type.IsEnum)
                    return Enum.Parse(type, valueStr, ignoreCase: true);

                if (type == typeof(Guid))
                    return Guid.Parse(valueStr);

                if (type == typeof(DateTime))
                    return DateTime.Parse(valueStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                if (type == typeof(DateTimeOffset))
                    return DateTimeOffset.Parse(valueStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

                if (typeof(IConvertible).IsAssignableFrom(type))
                    return Convert.ChangeType(valueStr, type, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }

            return null;
        }

        #endregion




    }
}
