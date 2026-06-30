using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Plugin
{
    public interface IReportBinding : IStateBinding
    {
        /// <summary>
        /// Invoked for every report while the binding is held (button pressed),
        /// in addition to the single <see cref="IStateBinding.Press"/> /
        /// <see cref="IStateBinding.Release"/> calls on state change.
        /// </summary>
        /// <param name="tablet">The tablet that this report is from.</param>
        /// <param name="report">The report received while held.</param>
        void OnReport(TabletReference tablet, IDeviceReport report);
    }
}
