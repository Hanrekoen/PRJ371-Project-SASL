using System;
using System.Globalization;
using System.Reflection;
using Convai.Runtime.DynamicContext;
using UnityEngine;

namespace Convai.Runtime.SceneMetadata
{
    /// <summary>
    ///     Designer-authored tracked property on a world object. Values are pushed at runtime
    ///     through <see cref="ConvaiObjectMetadata.SetTrackedPropertyValue" /> or polling.
    /// </summary>
    [Serializable]
    public sealed class ConvaiTrackedContextProperty
    {
        [SerializeField] private string _propertyName = string.Empty;
        [SerializeField] private ConvaiRespondMode _reaction = ConvaiRespondMode.Silent;
        [SerializeField] private string _initialValue = string.Empty;
        [SerializeField] private Component _sourceComponent;
        [SerializeField] private string _sourceMemberName = string.Empty;

        public string PropertyName => _propertyName ?? string.Empty;
        public ConvaiRespondMode Reaction => _reaction;
        public string InitialValue => _initialValue ?? string.Empty;
        public bool HasRuntimeSource => _sourceComponent != null && !string.IsNullOrWhiteSpace(_sourceMemberName);

        public string ResolveInitialValue() =>
            TryReadRuntimeValue(out string value) ? value : InitialValue;

        public bool TryReadRuntimeValue(out string value)
        {
            value = null;
            if (!HasRuntimeSource) return false;

            string memberName = _sourceMemberName.Trim();
            Type type = _sourceComponent.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                PropertyInfo property = type.GetProperty(memberName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return TryFormatValue(property.GetValue(_sourceComponent), out value);

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                    return TryFormatValue(field.GetValue(_sourceComponent), out value);

                MethodInfo method = type.GetMethod(memberName, flags);
                if (method != null && method.GetParameters().Length == 0)
                    return TryFormatValue(method.Invoke(_sourceComponent, null), out value);
            }
            catch (Exception)
            {
                value = null;
                return false;
            }

            return false;
        }

        private static bool TryFormatValue(object rawValue, out string value)
        {
            value = rawValue switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => rawValue.ToString()
            };

            return value != null;
        }
    }
}
