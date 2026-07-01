using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.UX.Windows.Bindings;

namespace OpenTabletDriver.UX.Controls.Bindings
{
    /// <summary>
    /// Mirrors Wacom's Application list: "All Applications" (global bindings) plus one entry
    /// per app-specific override, with add/remove. Selecting an entry raises
    /// <see cref="SelectedAppChanged"/> so the host can point the binding editors at it.
    /// </summary>
    public class ApplicationBindingSelector : Panel
    {
        public ApplicationBindingSelector()
        {
            appDropDown = new DropDown
            {
                ItemTextBinding = Binding.Property<AppEntry, string>(e => e.Text)
            };
            removeButton = new Button { Text = "−", Width = 30, Enabled = false };
            var addButton = new Button { Text = "+", Width = 30 };

            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Padding = new Padding(8, 8),
                Items =
                {
                    new Label { Text = "Application", VerticalAlignment = VerticalAlignment.Center },
                    new StackLayoutItem { Expand = true, Control = appDropDown },
                    addButton,
                    removeButton
                }
            };

            appDropDown.SelectedIndexChanged += (_, _) =>
            {
                removeButton.Enabled = SelectedApp != null;
                SelectedAppChanged?.Invoke(this, EventArgs.Empty);
            };

            // ReSharper disable once AsyncVoidMethod
            addButton.Click += async void (_, _) => await AddApplication();
            removeButton.Click += (_, _) => RemoveSelected();

            RefreshEntries();
        }

        private readonly DropDown appDropDown;
        private readonly Button removeButton;

        private Profile? profile;
        public Profile? Profile
        {
            set
            {
                if (profile != null)
                    profile.AppBindings.CollectionChanged -= HandleAppBindingsChanged;

                profile = value;

                if (profile != null)
                    profile.AppBindings.CollectionChanged += HandleAppBindingsChanged;

                RefreshEntries();
                ProfileChanged?.Invoke(this, EventArgs.Empty);
            }
            get => profile;
        }

        public event EventHandler<EventArgs>? ProfileChanged;

        public BindableBinding<ApplicationBindingSelector, Profile?> ProfileBinding
        {
            get
            {
                return new BindableBinding<ApplicationBindingSelector, Profile?>(
                    this,
                    c => c.Profile,
                    (c, v) => c.Profile = v,
                    (c, h) => c.ProfileChanged += h,
                    (c, h) => c.ProfileChanged -= h
                );
            }
        }

        /// <summary>Null when "All Applications" (global) is selected.</summary>
        public AppBindingProfile? SelectedApp => (appDropDown.SelectedValue as AppEntry)?.AppProfile;

        public event EventHandler<EventArgs>? SelectedAppChanged;

        private void HandleAppBindingsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshEntries();

        private void RefreshEntries(string? selectBundleId = null)
        {
            var targetBundleId = selectBundleId ?? SelectedApp?.BundleIdentifier;

            var entries = new[] { new AppEntry(null, "All Applications") }
                .Concat((profile?.AppBindings ?? []).Select(a => new AppEntry(a, a.DisplayName)))
                .ToList();

            appDropDown.DataStore = entries;
            appDropDown.SelectedIndex = targetBundleId == null
                ? 0
                : Math.Max(0, entries.FindIndex(e => e.AppProfile?.BundleIdentifier == targetBundleId));

            removeButton.Enabled = SelectedApp != null;
        }

        private async Task AddApplication()
        {
            if (profile is not { } activeProfile)
                return;

            using var dialog = new AddApplicationDialog();
            if (await dialog.ShowModalAsync(this) is not { } picked)
                return;

            var existing = activeProfile.AppBindings.FirstOrDefault(a => a.BundleIdentifier == picked.BundleId);
            if (existing == null)
            {
                existing = AppBindingProfile.CreateFromGlobal(picked.BundleId, picked.DisplayName, activeProfile.BindingSettings);
                activeProfile.AppBindings.Add(existing);
            }

            RefreshEntries(existing.BundleIdentifier);
        }

        private void RemoveSelected()
        {
            if (profile is not { } activeProfile || SelectedApp is not { } selected)
                return;

            if (MessageBox.Show(
                    $"Remove the application-specific bindings for \"{selected.DisplayName}\"?",
                    "Remove Application",
                    MessageBoxButtons.OKCancel,
                    MessageBoxType.Question) != DialogResult.Ok)
                return;

            activeProfile.AppBindings.Remove(selected);
            appDropDown.SelectedIndex = 0;
        }

        private sealed class AppEntry(AppBindingProfile? appProfile, string text)
        {
            public AppBindingProfile? AppProfile { get; } = appProfile;
            public string Text { get; } = text;
        }
    }
}
