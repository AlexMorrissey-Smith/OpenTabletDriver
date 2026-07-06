using System;

namespace OpenTabletDriver.Desktop.Binding
{
    /// <summary>
    /// On-screen HUD request raised by bindings and forwarded to RPC clients as
    /// the daemon's <c>Overlay</c> event. On macOS the daemon draws HUDs itself
    /// via the bundled Swift helpers; on other platforms the GUI renders them.
    /// </summary>
    public class OverlayRequest
    {
        public string Kind { set; get; } = string.Empty;
        public object? Payload { set; get; }
    }

    public static class OverlayHub
    {
        public static event EventHandler<OverlayRequest>? OverlayRequested;

        public static void Publish(string kind, object? payload)
        {
            OverlayRequested?.Invoke(null, new OverlayRequest { Kind = kind, Payload = payload });
        }
    }
}
