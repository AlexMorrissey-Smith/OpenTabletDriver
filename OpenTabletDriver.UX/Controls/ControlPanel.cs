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
using OpenTabletDriver.UX.Controls.Output;

namespace OpenTabletDriver.UX.Controls
{
    public class ControlPanel : Panel
    {
        private const string NativeTabTrailingPadding = "\u00a0\u00a0\u00a0";

        public ControlPanel()
        {
            var control = new TabControl();

            control.Pages.Add(new TabPage
            {
                Text = NativeTabText("Tablet"),
                Content = outputModeEditor = new()
            });

            control.Pages.Add(new TabPage
            {
                Text = NativeTabText("Pen"),
                Content = penBindingEditor = new PenBindingEditor()
            });

            control.Pages.Add(new TabPage
            {
                Text = NativeTabText("Buttons"),
                Content = auxBindingEditor = new AuxiliaryBindingEditor()
            });

            control.Pages.Add(new TabPage
            {
                ID = "mouse",
                Text = NativeTabText("Mouse"),
                Content = mouseBindingEditor = new MouseBindingEditor()
            });

            control.Pages.Add(new TabPage
            {
                Text = NativeTabText("Tools"),
                Padding = 5,
                Content = toolEditor = new()
            });

            control.Pages.Add(new TabPage
            {
                Text = NativeTabText("Filters"),
                Padding = 5,
                Content = filterEditor = new()
            });

            control.Pages.Add(new TabPage
            {
                Text = NativeTabText("Info"),
                Padding = 5,
                Content = placeholder = new Placeholder
                {
                    Text = "No tablets are detected."
                }
            });

            control.Pages.Add(new TabPage
            {
                Text = NativeTabText("Console"),
                Padding = 5,
                Content = logView = new()
            });

            this.Content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new StackLayoutItem { Control = appSelector = new ApplicationBindingSelector() },
                    new StackLayoutItem { Expand = true, Control = tabControl = control }
                }
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

        private TabControl tabControl;
        private Placeholder placeholder;
        private LogView logView;
        private OutputModeEditor outputModeEditor;
        private BindingEditor penBindingEditor, auxBindingEditor, mouseBindingEditor;
        private List<BindingEditor> wheelBindingEditors = [];
        private ApplicationBindingSelector appSelector;
        private PluginSettingStoreCollectionEditor<IPositionedPipelineElement<IDeviceReport>> filterEditor;
        private PluginSettingStoreCollectionEditor<ITool> toolEditor;

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

            if (Platform.IsMac)
                tabControl.Pages.Clear();

            if (tablet != null)
            {
                bool switchToTablet = tabControl.SelectedPage == placeholder.Parent;

                SetPageVisibility(placeholder, false);
                SetPageVisibility(outputModeEditor, true);
                SetPageVisibility(penBindingEditor, true);
                SetPageVisibility(auxBindingEditor, tablet.Properties.Specifications.AuxiliaryButtons != null);

                for (int i = 0; i < wheelBindingEditors.Count; i++)
                    SetPageVisibility(wheelBindingEditors[i], (tablet.Properties.Specifications.Wheels?.Count ?? 0) > i);

                SetPageVisibility(mouseBindingEditor, tablet.Properties.Specifications.MouseButtons != null);
                SetPageVisibility(toolEditor, true);
                SetPageVisibility(filterEditor, true);

                if (switchToTablet)
                    tabControl.SelectedIndex = 0;
            }
            else
            {
                SetPageVisibility(placeholder, true);
                SetPageVisibility(outputModeEditor, false);
                SetPageVisibility(penBindingEditor, false);
                SetPageVisibility(auxBindingEditor, false);
                foreach (var controlItem in wheelBindingEditors)
                    SetPageVisibility(controlItem, false);
                SetPageVisibility(mouseBindingEditor, false);
                SetPageVisibility(toolEditor, false);
                SetPageVisibility(filterEditor, false);

                if (tabControl.SelectedPage != logView.Parent)
                {
                    tabControl.SelectedIndex = Profile == null ?
                        tabControl.Pages.IndexOf(placeholder.Parent as TabPage) :
                        0;
                }
            }

            SetPageVisibility(logView, true);
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
                    var pageIndex = tabControl.Pages.IndexOf(toolEditor.Parent as TabPage);
                    wheelBindingEditors.Add(wheelBindingEditor);
                    var wheelLabel = tabletWheels > 1 ? $"Wheel {i + 1}" : "Wheel";
                    var wheelPage = new TabPage(wheelBindingEditor) { Text = NativeTabText(wheelLabel) };
                    if (pageIndex >= 0)
                        tabControl.Pages.Insert(pageIndex, wheelPage);
                    else
                        tabControl.Pages.Add(wheelPage);
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

        private void SetPageVisibility(Control control, bool visible)
        {
            if (Platform.IsMac)
            {
                if (visible)
                {
                    var page = control.Parent as TabPage;
                    tabControl.Pages.Add(page);
                }
            }
            else
            {
                control.Parent.Visible = visible;
            }
        }

        // Native tab text: no padding hack — let macOS render the standard (Liquid Glass) tab bar.
        private static string NativeTabText(string text) => text;
    }
}
