using System;
using System.Linq;
using Eto.Forms;
using MonoMac.AppKit;
using MonoMac.Foundation;
using OpenTabletDriver.Desktop.Interop;

namespace OpenTabletDriver.UX.MacOS;

internal class FormHandler : Eto.Mac.Forms.FormHandler
{
    private NSObject? screenChangeObserver;

    protected override void Initialize()
    {
        base.Initialize();
        Widget.WindowStateChanged += UpdateActivationPolicy;
        Widget.LostFocus += UpdateActivationPolicy;
        Widget.GotFocus += UpdateActivationPolicy;
        ApplyWindowChrome();
        ObserveScreenChanges();
    }

    // Forward OS monitor-layout changes to the cross-platform display-layout watcher.
    private void ObserveScreenChanges()
    {
        try
        {
            screenChangeObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
                NSApplication.DidChangeScreenParametersNotification,
                _ => DesktopInterop.NotifyDisplaysChanged());
        }
        catch
        {
            // Non-fatal: the watcher's poll still catches changes, just less promptly.
        }
    }

    public override void Show()
    {
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        base.Show();
    }

    // Solid, opaque window with an edge-to-edge titlebar (no translucent "frosted glass"
    // material — the UI uses solid backgrounds so content never shows the desktop through it).
    // Best-effort: any failure here must never take down the app, so it is fully guarded.
    private void ApplyWindowChrome()
    {
        try
        {
            var window = Control;
            if (window == null)
                return;

            // Titlebar that blends into the solid window body, but NOT full-size content: the
            // content view stays below the titlebar so nothing renders under the traffic lights.
            window.TitlebarAppearsTransparent = true;
            window.TitleVisibility = NSWindowTitleVisibility.Hidden;

            // Opaque, theme-following window background (adapts to light/dark automatically).
            window.IsOpaque = true;
            window.BackgroundColor = NSColor.WindowBackground;

            // The old vibrancy view forced the window to be layer-backed, which is what made the
            // custom-drawn cards/sidebar render crisp. Removing it dropped layer-backing and the
            // Drawables started rendering jagged. Re-enable layer-backing explicitly so solid mode
            // is just as sharp.
            if (window.ContentView is { } content)
                content.WantsLayer = true;
        }
        catch
        {
            // Window chrome is cosmetic; ignore unsupported OS/runtime combinations.
        }
    }

    private void UpdateActivationPolicy(object sender, EventArgs e)
    {
        var hasNonMinimizedVisibleWindow =
            Application.Instance.Windows.Any(window => window.Visible && window.WindowState != WindowState.Minimized);
        NSApplication.SharedApplication.ActivationPolicy = hasNonMinimizedVisibleWindow
            ? NSApplicationActivationPolicy.Regular : NSApplicationActivationPolicy.Accessory;
    }
}
