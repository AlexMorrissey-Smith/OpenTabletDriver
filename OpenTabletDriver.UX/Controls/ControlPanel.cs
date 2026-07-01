using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.UX.Controls.Bindings;
using OpenTabletDriver.UX.Controls.Generic;
using OpenTabletDriver.UX.Controls.Output;

namespace OpenTabletDriver.UX.Controls
{
    public class ControlPanel : Panel
    {
        public ControlPanel()
        {
            outputModeEditor = new();
            penBindingEditor = new PenBindingEditor();
            auxBindingEditor = new AuxiliaryBindingEditor();
            mouseBindingEditor = new MouseBindingEditor();
            toolEditor = new();
            filterEditor = new();
            placeholder = new Placeholder
            {
                Text = "No tablets are detected."
            };
            logView = new();

            tabletItem = new SidebarNav.NavItem("Tablet", "Tablet", outputModeEditor);
            penItem = new SidebarNav.NavItem("Pen", "Pen", penBindingEditor);
            auxItem = new SidebarNav.NavItem("Buttons", "Buttons", auxBindingEditor);
            mouseItem = new SidebarNav.NavItem("Mouse", "Mouse", mouseBindingEditor);
            toolsItem = new SidebarNav.NavItem("Tools", "Tools", toolEditor);
            filtersItem = new SidebarNav.NavItem("Filters", "Filters", filterEditor);
            infoItem = new SidebarNav.NavItem("Info", "Info", placeholder);
            consoleItem = new SidebarNav.NavItem("Console", "Console", logView);

            // Canonical, never-shrinking ordering: wheel entries get spliced into this (and only
            // this) list as tablets with wheels are seen. The sidebar holds the live *visible*
            // subset, always kept in this same relative order.
            masterOrder = [tabletItem, penItem, auxItem, mouseItem, toolsItem, filtersItem, infoItem, consoleItem];

            contentPanel = new Panel();

            sidebar = new SidebarNav();
            sidebar.SelectedIndexChanged += (_, _) =>
            {
                contentPanel.Content = sidebar.SelectedItem?.Tag as Control;
            };

            appSelector = new ApplicationBindingSelector();

            // App selector is a header of the content pane (not spanning the sidebar), so it lines
            // up with the settings cards and the sidebar runs the full window height beside it.
            var contentPane = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new StackLayoutItem { Control = appSelector },
                    new StackLayoutItem { Expand = true, Control = contentPanel }
                }
            };

            this.Content = new Splitter
            {
                Orientation = Orientation.Horizontal,
                FixedPanel = SplitterFixedPanel.Panel1,
                Panel1MinimumSize = 170,
                Position = 190,
                Panel1 = sidebar,
                Panel2 = contentPane
            };

            outputModeEditor.ProfileBinding.Bind(ProfileBinding);
            penBindingEditor.ProfileBinding.Bind(ProfileBinding);
            auxBindingEditor.ProfileBinding.Bind(ProfileBinding);
            mouseBindingEditor.ProfileBinding.Bind(ProfileBinding);
            appSelector.ProfileBinding.Bind(ProfileBinding);
            filterEditor.StoreCollectionBinding.Bind(ProfileBinding.Child(p => p!.Filters)!);
            toolEditor.StoreCollectionBinding.Bind(App.Current, a => a.Settings.Tools);

            // App-specific bindings cover pen/buttons/wheel only (matches Wacom): the
            // selector never touches outputModeEditor/toolEditor/filterEditor.
            appSelector.SelectedAppChanged += (_, _) =>
            {
                var settings = appSelector.SelectedApp?.BindingSettings;
                penBindingEditor.BindingSettingsOverride = settings;
                auxBindingEditor.BindingSettingsOverride = settings;
                mouseBindingEditor.BindingSettingsOverride = settings;
                foreach (var wheelEditor in wheelBindingEditors)
                    wheelEditor.BindingSettingsOverride = settings;
            };

            outputModeEditor.SetDisplaySize(DesktopInterop.VirtualScreen?.Displays);

            DesktopInterop.DisplaysChanged += HandleDisplaysChanged;
        }

        // Refresh the editor's display rectangles when monitors are connected/disconnected so the
        // background reflects the live layout (no stale "ghost" of a removed display).
        private void HandleDisplaysChanged() => Application.Instance.AsyncInvoke(() =>
        {
            outputModeEditor.SetDisplaySize(DesktopInterop.VirtualScreen?.Displays);
            outputModeEditor.Invalidate();
        });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DesktopInterop.DisplaysChanged -= HandleDisplaysChanged;
            base.Dispose(disposing);
        }

        private readonly SidebarNav sidebar;
        private readonly Panel contentPanel;
        private readonly List<SidebarNav.NavItem> masterOrder;

        private Placeholder placeholder;
        private LogView logView;
        private OutputModeEditor outputModeEditor;
        private BindingEditor penBindingEditor, auxBindingEditor, mouseBindingEditor;
        private List<BindingEditor> wheelBindingEditors = [];
        private List<SidebarNav.NavItem> wheelItems = [];
        private ApplicationBindingSelector appSelector;
        private PluginSettingStoreCollectionEditor<IPositionedPipelineElement<IDeviceReport>> filterEditor;
        private PluginSettingStoreCollectionEditor<ITool> toolEditor;

        private readonly SidebarNav.NavItem tabletItem, penItem, auxItem, mouseItem, toolsItem, filtersItem, infoItem, consoleItem;

        private Profile? profile;

        private Profile? Profile
        {
            set
            {
                this.profile = value;
                this.OnProfileChanged();
            }
            get => this.profile;
        }

        public event EventHandler<EventArgs>? ProfileChanged;

        // ReSharper disable once AsyncVoidMethod
        protected virtual void OnProfileChanged() => Application.Instance.AsyncInvoke(async void () =>
        {
            ProfileChanged?.Invoke(this, EventArgs.Empty);

            var tablet = Profile != null ? await Profile.GetTabletReference() : null;

            OnTabletChanged(tablet);

            if (tablet != null)
            {
                bool switchToTablet = sidebar.SelectedItem == infoItem;

                SetPageVisibility(infoItem, false);
                SetPageVisibility(tabletItem, true);
                SetPageVisibility(penItem, true);
                SetPageVisibility(auxItem, tablet.Properties.Specifications.AuxiliaryButtons != null);

                for (int i = 0; i < wheelItems.Count; i++)
                    SetPageVisibility(wheelItems[i], (tablet.Properties.Specifications.Wheels?.Count ?? 0) > i);

                SetPageVisibility(mouseItem, tablet.Properties.Specifications.MouseButtons != null);
                SetPageVisibility(toolsItem, true);
                SetPageVisibility(filtersItem, true);

                if (switchToTablet || sidebar.SelectedIndex < 0)
                    sidebar.SelectedIndex = 0;
            }
            else
            {
                SetPageVisibility(infoItem, true);
                SetPageVisibility(tabletItem, false);
                SetPageVisibility(penItem, false);
                SetPageVisibility(auxItem, false);
                foreach (var wheelItem in wheelItems)
                    SetPageVisibility(wheelItem, false);
                SetPageVisibility(mouseItem, false);
                SetPageVisibility(toolsItem, false);
                SetPageVisibility(filtersItem, false);

                if (sidebar.SelectedItem != consoleItem)
                    sidebar.SelectedIndex = sidebar.IndexOf(infoItem);
            }

            SetPageVisibility(consoleItem, true);

            // Fall back to a valid selection if the previously-selected page was just hidden.
            if (sidebar.SelectedIndex < 0 && sidebar.Count > 0)
                sidebar.SelectedIndex = 0;
        });

        /// <summary>
        /// Regenerates the current profile's button/pen/wheel bindings from defaults,
        /// keeping the tablet area, output mode, and filters untouched.
        /// </summary>
        public async Task ResetBindingsToDefaults()
        {
            if (Profile is not { } profile)
                return;

            var tablet = await profile.GetTabletReference();
            if (tablet is null)
                return;

            var defaults = BindingSettings.GetDefaults(tablet.Properties.Specifications);
            defaults.ApplyTabletSpecificDefaults(tablet.Properties.Name);
            profile.BindingSettings = defaults;

            // Re-push the profile so each editor's controls re-read the new BindingSettings.
            penBindingEditor.Profile = profile;
            auxBindingEditor.Profile = profile;
            mouseBindingEditor.Profile = profile;
            foreach (var wheelEditor in wheelBindingEditors)
                wheelEditor.Profile = profile;
        }

        private void OnTabletChanged(TabletReference? tablet)
        {
            int tabletWheels = tablet?.Properties.Specifications.Wheels?.Count ?? 0;
            if (tabletWheels > wheelBindingEditors.Count)
            {
                for (int i = wheelBindingEditors.Count; i < tabletWheels; i++)
                {
                    var wheelBindingEditor = new WheelBindingEditor(i);
                    wheelBindingEditor.ProfileBinding.Bind(ProfileBinding);
                    wheelBindingEditor.BindingSettingsOverride = appSelector.SelectedApp?.BindingSettings;
                    wheelBindingEditors.Add(wheelBindingEditor);
                    var wheelLabel = tabletWheels > 1 ? $"Wheel {i + 1}" : "Wheel";
                    var wheelItem = new SidebarNav.NavItem(wheelLabel, "Wheel", wheelBindingEditor);
                    wheelItems.Add(wheelItem);

                    int insertIndex = masterOrder.IndexOf(toolsItem);
                    masterOrder.Insert(insertIndex, wheelItem);
                }
            }
        }

        public BindableBinding<ControlPanel, Profile?> ProfileBinding
        {
            get
            {
                return new BindableBinding<ControlPanel, Profile?>(
                    this,
                    c => c.Profile,
                    (c, v) => c.Profile = v,
                    (c, h) => c.ProfileChanged += h,
                    (c, h) => c.ProfileChanged -= h
                );
            }
        }

        // Adds/removes the entry from the visible sidebar, preserving masterOrder's relative
        // ordering. Uniform across platforms - no TabControl-specific quirks to work around here.
        private void SetPageVisibility(SidebarNav.NavItem item, bool visible)
        {
            if (visible)
            {
                if (sidebar.Contains(item))
                    return;

                int insertAt = 0;
                foreach (var candidate in masterOrder)
                {
                    if (candidate == item)
                        break;
                    if (sidebar.Contains(candidate))
                        insertAt++;
                }
                sidebar.Insert(insertAt, item);
            }
            else
            {
                sidebar.Remove(item);
            }
        }
    }
}
