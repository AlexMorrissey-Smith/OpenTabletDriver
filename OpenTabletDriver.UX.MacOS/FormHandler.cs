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
        ApplyVibrancy();
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

    // Gives the window the translucent "frosted glass" macOS material behind its content.
    // Best-effort: any failure here must never take down the app, so it is fully guarded.
    private void ApplyVibrancy()
    {
        try
        {
            var window = Control;
            var content = window?.ContentView;
            if (window == null || content == null)
                return;

            // Edge-to-edge translucent titlebar for the modern look.
            window.TitlebarAppearsTransparent = true;
            window.TitleVisibility = NSWindowTitleVisibility.Hidden;
            window.StyleMask |= NSWindowStyle.FullSizeContentView;
            window.BackgroundColor = NSColor.Clear;
            window.IsOpaque = false;

            var effect = new NSVisualEffectView(content.Bounds)
            {
                Material = (NSVisualEffectMaterial)21,        // NSVisualEffectMaterialUnderWindowBackground
                BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
                State = (NSVisualEffectState)1,               // NSVisualEffectStateActive
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
            };

            // Add the material as a background layer BEHIND Eto's content (do not reparent/replace
            // the content view — that breaks Eto's layout). Eto's controls render on top.
            content.AddSubview(effect, NSWindowOrderingMode.Below, null);
        }
        catch
        {
            // Vibrancy is cosmetic; ignore unsupported OS/runtime combinations.
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
