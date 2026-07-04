using System;
using System.Collections.Generic;
using Eto.Forms;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX
{
    public sealed class TrayIcon : IDisposable
    {
        public TrayIcon(MainForm window)
        {
            this.window = window;

            Indicator = new TrayIndicator
            {
                Title = "OpenTabletDriver",
                Image = App.Logo
            };

            RefreshMenuItems();

            Indicator.Activated += (_, _) =>
            {
                window.Show();
                window.BringToFront();
            };
        }

        public TrayIndicator Indicator { get; }
        private MainForm window;

        public void Dispose()
        {
            Indicator.Hide();
            Indicator.Dispose();
        }

        // macOS doesn't render a menu bar for windows of background apps
        // This is used to clone the items of the menu bar to the context menu
        private static MenuItem CloneMenuItem(MenuItem original)
        {
            if (original is SeparatorMenuItem)
            {
                return new SeparatorMenuItem();
            }

            if (original is ButtonMenuItem buttonItem)
            {
                var cloned = new ButtonMenuItem
                {
                    Text = buttonItem.Text,
                    Enabled = buttonItem.Enabled,
                    Shortcut = buttonItem.Shortcut
                };

                // Clone sub-items recursively
                foreach (var subItem in buttonItem.Items)
                {
                    cloned.Items.Add(CloneMenuItem(subItem));
                }

                // Re-create the click handler by triggering the original item's click
                cloned.Click += (sender, e) => buttonItem.PerformClick();

                return cloned;
            }

            return original;
        }

        public void RefreshMenuItems()
        {
            var showWindow = new ButtonMenuItem
            {
                Text = "Show Window"
            };
            showWindow.Click += (sender, e) =>
            {
                window.Show();
                window.BringToFront();
            };

            var close = new ButtonMenuItem
            {
                Text = "Close"
            };

            if (DesktopInterop.CurrentPlatform == PluginPlatform.MacOS)
            {
                // It's more idiomatic for macOS to include the name here
                showWindow.Text = "Show OpenTabletDriver";

                // Applications on macOS will keep running even after closing all their windows
                // Offering a way to quit the app here is more idiomatic
                close.Text = "Quit";
                close.Click += (sender, e) =>
                {
                    MainForm.SignalQuitting();
                    Application.Instance.Quit();
                };
            }
            else
            {
                close.Click += (sender, e) => window.Close();
            }

            var items = new List<MenuItem>();
            var presets = AppInfo.PresetManager.GetPresets();

            if (presets.Count != 0)
            {
                foreach (var preset in presets)
                {
                    var presetItem = new ButtonMenuItem
                    {
                        Text = preset.Name
                    };
                    presetItem.Click += MainForm.PresetButtonHandler;

                    items.Add(presetItem);
                }

                items.Add(new SeparatorMenuItem());
            }

            items.Add(showWindow);

            // ponytail: everything else is reachable from the window's menu bar
            // once it's shown, so the tray only carries quick actions.
            if (DesktopInterop.CurrentPlatform == PluginPlatform.MacOS && window.Menu != null)
            {
                items.Add(new SeparatorMenuItem());

                var tabletsMenu = window.Menu.Items.GetSubmenu("Tablets") as ButtonMenuItem;
                if (tabletsMenu != null)
                {
                    foreach (var item in tabletsMenu.Items)
                    {
                        if (item.Text == "Detect tablet")
                        {
                            items.Add(CloneMenuItem(item));
                            items.Add(new SeparatorMenuItem());
                            break;
                        }
                    }
                }
            }

            items.Add(close);

            Indicator.Menu = new ContextMenu(items);
        }
    }
}
