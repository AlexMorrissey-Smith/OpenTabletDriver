using System;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Windows.Bindings;

namespace OpenTabletDriver.UX.Controls
{
    public class BindingDisplay : Panel
    {
        // A binding's human-readable string can get very long (e.g. wheel-mode actions with four
        // parameters). Cap what the button displays so a single binding can't force its row wider
        // than the viewport (which produced a horizontal scrollbar); the full value stays in the
        // tooltip.
        private const int MaxDisplayLength = 48;

        public BindingDisplay()
        {
            this.Content = new StackLayout
            {
                MinimumSize = new Size(220, 0),
                Orientation = Orientation.Horizontal,
                Items =
                {
                    new StackLayoutItem
                    {
                        Expand = true,
                        Control = mainButton = new Button()
                    }
                }
            };

            mainButton.TextBinding.Bind(this.StoreBinding.Convert<string?>(s => Truncate(s?.GetHumanReadableString())));
            mainButton.Bind(b => b.ToolTip, this.StoreBinding.Convert(s => s?.GetHumanReadableString() ?? string.Empty));

            mainButton.Click += async (sender, e) =>
            {
                var dialog = new BindingEditorDialog(Store);
                this.Store = await dialog.ShowModalAsync(this);
            };
        }

        private static string? Truncate(string? text)
        {
            if (text is null || text.Length <= MaxDisplayLength)
                return text;
            return text[..(MaxDisplayLength - 1)].TrimEnd() + "…";
        }

        private Button mainButton;

        public event EventHandler<EventArgs>? StoreChanged;

        private PluginSettingStore? store;
        public PluginSettingStore? Store
        {
            set
            {
                this.store = value;
                StoreChanged?.Invoke(this, new EventArgs());
            }
            get => this.store;
        }

        public BindableBinding<BindingDisplay, PluginSettingStore?> StoreBinding
        {
            get
            {
                return new BindableBinding<BindingDisplay, PluginSettingStore?>(
                    this,
                    c => c.Store,
                    (c, v) => c.Store = v,
                    (c, h) => c.StoreChanged += h,
                    (c, h) => c.StoreChanged -= h
                );
            }
        }
    }
}
