namespace OpenTabletDriver.Desktop.Contracts
{
    public enum VMultiDeviceStatusKind
    {
        Ready,
        Missing,
        Incomplete,
        OpenFailed
    }

    /// <summary>
    /// Install/health state of the external VMulti VirtualHID driver that the
    /// Windows Ink output mode depends on. Surfaced to the GUI over RPC.
    /// </summary>
    public class VMultiDeviceStatus
    {
        public VMultiDeviceStatusKind Kind { set; get; }
        public bool IsAvailable { set; get; }
        public bool IsExtendedDigitizerAvailable { set; get; }
        public string Message { set; get; } = string.Empty;
        public string DownloadUrl { set; get; } = string.Empty;
    }
}
