using System.Collections.Generic;

namespace OpenTabletDriver.Desktop.Contracts
{
    // Serializable snapshot of the desktop's virtual screen + its individual
    // displays, in the driver's coordinate space. The webview frontend must map
    // the display area against THIS (not the browser's own monitor list), or the
    // area it saves won't match how the driver actually maps the tablet.

    public class DisplayInfo
    {
        public int Index { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class VirtualScreenInfo
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        /// <summary>Individual monitors (the IVirtualScreen wrapper is excluded).</summary>
        public List<DisplayInfo> Displays { get; set; } = new();
    }
}
