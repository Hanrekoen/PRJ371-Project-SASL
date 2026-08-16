using System;
using UnityEngine;

namespace Convai.Runtime.Utilities
{
    /// <summary>
    ///     Attribute that restricts a MonoBehaviour or Component field to objects that implement a specific interface.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class RequireInterfaceAttribute : PropertyAttribute
    {
        /// <summary>
        ///     Creates a new RequireInterfaceAttribute.
        /// </summary>
        /// <param name="interfaceType">The interface type to require.</param>
        /// <exception cref="ArgumentException">Thrown if the type is not an interface.</exception>
        public RequireInterfaceAttribute(Type interfaceType)
        {
            if (interfaceType == null) throw new ArgumentNullException(nameof(interfaceType));

            if (!interfaceType.IsInterface)
                throw new ArgumentException($"Type {interfaceType.Name} is not an interface.", nameof(interfaceType));

            InterfaceType = interfaceType;
        }

        /// <summary>
        ///     The interface type that the assigned object must implement.
        /// </summary>
        public Type InterfaceType { get; }
    }
}
