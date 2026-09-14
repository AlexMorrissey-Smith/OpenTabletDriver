using System;

namespace OpenTabletDriver.Plugin.Attributes
{
    /// <summary>
    /// Marks a string <see cref="PropertyAttribute"/> member as multiline text
    /// (script/source editors render a textarea instead of a single-line input).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MultilinePropertyAttribute : Attribute
    {
    }
}
