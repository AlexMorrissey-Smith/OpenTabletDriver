using System;

namespace OpenTabletDriver.Desktop.Binding
{
    /// <summary>
    /// Hides a binding from the binding-type picker (e.g. wheel-rotation-only actions that make no
    /// sense on a button). The type is still constructible as a programmatic default.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HideFromBindingListAttribute : Attribute
    {
    }
}
