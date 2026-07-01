using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.UX.Controls.Generic;

namespace OpenTabletDriver.UX.Windows.Bindings
{
    /// <summary>
    /// Mirrors Wacom's "add application" picker: choose from currently-running apps, or
    /// browse to one that isn't running.
    /// </summary>
    public class AddApplicationDialog : Dialog<(string BundleId, string DisplayName)?>
    {
        public AddApplicationDialog()
        {
            Title = "Add Application";
            Resizable = true;
            ClientSize = new Size(320, 360);

            appList = new ListBox<RunningAppEntry>
            {
                ItemTextBinding = Binding.Property<RunningAppEntry, string>(a => a.DisplayName),
                Source = [.. DesktopInterop.ForegroundApp.GetRunningApplications()
                    .OrderBy(a => a.DisplayName)
                    .Select(a => new RunningAppEntry(a.BundleId, a.DisplayName))]
            };

            var browseButton = new Button { Text = "Browse…" };
            var cancelButton = new Button { Text = "Cancel" };
            var addButton = new Button { Text = "Add" };

            browseButton.Click += (_, _) => Browse();
            cancelButton.Click += (_, _) => Close(null);
            addButton.Click += (_, _) => Close(appList.SelectedItem is { } item ? (item.BundleId, item.DisplayName) : null);

            Content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = 10,
                Spacing = 8,
                Items =
                {
                    new Label { Text = "Currently Open Applications" },
                    new StackLayoutItem { Expand = true, Control = new Scrollable { Border = BorderType.None, Content = appList } },
                    browseButton,
                    new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 5,
                        Items =
                        {
                            new StackLayoutItem { Expand = true, Control = cancelButton },
                            new StackLayoutItem { Expand = true, Control = addButton }
                        }
                    }
                }
            };

            DefaultButton = addButton;
            AbortButton = cancelButton;
        }

        private readonly ListBox<RunningAppEntry> appList;

        private void Browse()
        {
            var dialog = Extensions.OpenFileDialog(
                "Choose Application",
                "/Applications",
                [new FileFilter("Applications", ".app")]
            );

            if (dialog.ShowDialog(this) != DialogResult.Ok)
                return;

            if (DesktopInterop.ForegroundApp.TryGetBundleInfo(dialog.FileName, out var bundleId, out var displayName))
                Close((bundleId, displayName));
        }

        private sealed class RunningAppEntry(string bundleId, string displayName)
        {
            public string BundleId { get; } = bundleId;
            public string DisplayName { get; } = displayName;
        }
    }
}
