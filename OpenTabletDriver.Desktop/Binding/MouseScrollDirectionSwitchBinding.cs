using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding
{
    [PluginName(PLUGIN_NAME)]
    public class MouseScrollDirectionSwitchBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Mouse Scroll Direction Switch";

        public static ScrollDirection CurrentDirection { get; private set; } = ScrollDirection.Vertical;

        public static void ResetDirection() => CurrentDirection = ScrollDirection.Vertical;

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            CurrentDirection = CurrentDirection == ScrollDirection.Vertical
                ? ScrollDirection.Horizontal
                : ScrollDirection.Vertical;
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }

        public override string ToString() => $"{PLUGIN_NAME}: {CurrentDirection}";
    }
}
