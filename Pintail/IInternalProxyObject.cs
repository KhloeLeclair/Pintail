using System;
using System.Diagnostics.CodeAnalysis;

namespace Nanoray.Pintail
{
    internal interface IInternalProxyObject
    {
        object ProxyTargetInstance { get; }

        /// <summary>
        /// Returns the target of a given proxy instance.
        /// </summary>
        /// <param name="potentialProxyInstance">The instance which may be a proxy.</param>
        /// <param name="targetInstance">The target instance of the proxy instance.</param>
        /// <returns>Whether the given instance is a proxy.</returns>
        internal static bool TryGetProxyTargetInstance(object potentialProxyInstance, [NotNullWhen(true)] out object? targetInstance)
        {
            if (potentialProxyInstance is IInternalProxyObject proxyInstance)
            {
                targetInstance = proxyInstance.ProxyTargetInstance;
                return true;
            }

            // A delegate proxy is an actual delegate which calls a proxy object, and not a proxy object itself,
            // so it needs special handling.
            if (potentialProxyInstance is Delegate @delegate &&
                @delegate.Target is IInternalProxyObject proxyTarget &&
                proxyTarget.ProxyTargetInstance is Delegate targetDelegate &&
                @delegate.GetInvocationList().Length == 1)
            {
                targetInstance = targetDelegate;
                return true;
            }

            targetInstance = null;
            return false;
        }
    }
}
