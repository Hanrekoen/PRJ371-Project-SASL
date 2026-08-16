using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Convai.Shared.Types;

namespace Convai.Runtime.Actions
{
    /// <summary>
    ///     Maps a property or field of an executor parameter object to a named action parameter.
    ///     Without the attribute the member name is used as the parameter name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class ConvaiActionParameterAttribute : Attribute
    {
        /// <summary>Action parameter name to bind the member to.</summary>
        public string Name { get; }

        /// <summary>Creates the attribute binding a member to <paramref name="name" />.</summary>
        public ConvaiActionParameterAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    ///     Executor base that binds invocation parameters onto a typed
    ///     <typeparamref name="TParameters" /> object before execution. Supported member types:
    ///     string, float, double, int, bool, <see cref="ConvaiResolvedActionTarget" />,
    ///     and <see cref="ConvaiActionParameterValue" />.
    /// </summary>
    public abstract class ConvaiActionExecutor<TParameters> : ConvaiActionExecutorBase
        where TParameters : new()
    {
        // Reflection member map is resolved once per closed TParameters type.
        private static PropertyInfo[] _boundProperties;
        private static string[] _boundPropertyNames;
        private static FieldInfo[] _boundFields;
        private static string[] _boundFieldNames;

        /// <inheritdoc />
        public sealed override Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken)
        {
            TParameters parameters = BindParameters(invocation);
            return ExecuteAsync(invocation, parameters, cancellationToken);
        }

        /// <summary>Runs the action with the bound parameter object.</summary>
        protected abstract Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            TParameters parameters,
            CancellationToken cancellationToken);

        /// <summary>Builds the parameter object for an invocation; override to customize binding.</summary>
        protected virtual TParameters BindParameters(ConvaiActionInvocation invocation)
        {
            EnsureMemberMap();

            var parameters = new TParameters();

            for (int i = 0; i < _boundProperties.Length; i++)
            {
                PropertyInfo property = _boundProperties[i];
                object value = ReadValue(invocation, _boundPropertyNames[i], property.PropertyType);
                if (value != null || !property.PropertyType.IsValueType)
                    property.SetValue(parameters, value);
            }

            for (int i = 0; i < _boundFields.Length; i++)
            {
                FieldInfo field = _boundFields[i];
                object value = ReadValue(invocation, _boundFieldNames[i], field.FieldType);
                if (value != null || !field.FieldType.IsValueType)
                    field.SetValue(parameters, value);
            }

            return parameters;
        }

        private static void EnsureMemberMap()
        {
            if (_boundProperties != null)
                return;

            Type type = typeof(TParameters);

            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            int writableCount = 0;
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i].CanWrite)
                    writableCount++;
            }

            var boundProperties = new PropertyInfo[writableCount];
            var boundPropertyNames = new string[writableCount];
            int index = 0;
            for (int i = 0; i < properties.Length; i++)
            {
                if (!properties[i].CanWrite)
                    continue;

                boundProperties[index] = properties[i];
                boundPropertyNames[index] = GetParameterName(properties[i]);
                index++;
            }

            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var boundFieldNames = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                boundFieldNames[i] = GetParameterName(fields[i]);

            _boundPropertyNames = boundPropertyNames;
            _boundFields = fields;
            _boundFieldNames = boundFieldNames;
            // Assigned last: EnsureMemberMap gates on _boundProperties being non-null.
            _boundProperties = boundProperties;
        }

        private static string GetParameterName(MemberInfo member)
        {
            ConvaiActionParameterAttribute attribute = member.GetCustomAttribute<ConvaiActionParameterAttribute>();
            return string.IsNullOrWhiteSpace(attribute?.Name) ? member.Name : attribute.Name;
        }

        private static object ReadValue(ConvaiActionInvocation invocation, string parameterName, Type targetType)
        {
            if (targetType == typeof(string))
                return invocation.GetString(parameterName);

            if (targetType == typeof(float))
                return invocation.GetNumber(parameterName);

            if (targetType == typeof(double))
                return (double)invocation.GetNumber(parameterName);

            if (targetType == typeof(int))
                return (int)invocation.GetNumber(parameterName);

            if (targetType == typeof(bool))
                return invocation.GetBool(parameterName);

            if (targetType == typeof(ConvaiResolvedActionTarget))
                return invocation.GetReference(parameterName);

            if (targetType == typeof(ConvaiActionParameterValue) &&
                invocation.TryGetParameter(parameterName, out ConvaiActionParameterValue value))
                return value;

            return null;
        }
    }
}
