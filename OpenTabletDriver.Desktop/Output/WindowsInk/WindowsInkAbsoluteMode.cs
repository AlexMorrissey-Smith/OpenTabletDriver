using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Platform.Pointer;

#nullable enable

namespace OpenTabletDriver.Desktop.Output.WindowsInk
{
    /// <summary>
    /// Absolute output through the external VMulti VirtualHID driver, giving
    /// apps real Windows Ink pen input (pressure, tilt, eraser) instead of
    /// synthesized mouse events. Ported from upstream PR #4866.
    /// The pointer implements IMouseButtonHandler, so tip/button bindings
    /// route into the VMulti report via CreateBindingHandler's registration.
    /// </summary>
    [PluginName("Windows Ink Absolute Mode"), SupportedPlatform(PluginPlatform.Windows)]
    public class WindowsInkAbsoluteMode : AbsoluteOutputMode
    {
        private WindowsInkPointer? _pointer;
        private bool _sync = true;
        private bool _forcedSync;

        [Resolved]
        public IVirtualScreen? VirtualScreen { set; get; }

        public override IAbsolutePointer? Pointer
        {
            // The DI container resolves IAbsolutePointer to the system mouse;
            // ignore it — this mode always drives its own VMulti pointer.
            set { }
            get
            {
                if (_pointer == null && VirtualScreen != null)
                {
                    _pointer = new WindowsInkPointer(VirtualScreen)
                    {
                        Sync = _sync,
                        ForcedSync = _forcedSync
                    };
                }
                return _pointer;
            }
        }

        [BooleanProperty("Sync OS Cursor", "Synchronize the normal OS cursor with the Windows Ink pen position when the pen leaves range."), DefaultPropertyValue(true)]
        public bool Sync
        {
            set
            {
                _sync = value;
                if (_pointer is not null)
                    _pointer.Sync = value;
            }
            get => _sync;
        }

        [BooleanProperty("Forced Sync", "Synchronize the normal OS cursor on every Windows Ink report."), DefaultPropertyValue(false)]
        public bool ForcedSync
        {
            set
            {
                _forcedSync = value;
                if (_pointer is not null)
                    _pointer.ForcedSync = value;
            }
            get => _forcedSync;
        }
    }
}
