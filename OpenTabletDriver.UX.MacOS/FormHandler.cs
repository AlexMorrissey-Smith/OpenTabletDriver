using System;
using System.Linq;
using Eto.Forms;
using MonoMac.AppKit;

namespace OpenTabletDriver.UX.MacOS;

internal class FormHandler : Eto.Mac.Forms.FormHandler
{
    protected override void Initialize()
    {
        base.Initialize();
        Widget.WindowStateChanged += UpdateActivationPolicy;
        Widget.LostFocus += UpdateActivationPolicy;
        Widget.GotFocus += UpdateActivationPolicy;
        ApplyVibrancy();
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

            var effect = new NSVisualEffectView(content.Frame)
            {
                Material = (NSVisualEffectMaterial)21,        // NSVisualEffectMaterialUnderWindowBackground
                BlendingMode = NSVisualEffectBlendingMode.BehindWindow,
                State = (NSVisualEffectState)1,               // NSVisualEffectStateActive
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
            };

            // Reparent Eto's content view inside the effect view so the material sits behind everything.
            window.ContentView = effect;
            content.Frame = effect.Bounds;
            content.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
            effect.AddSubview(content);
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
