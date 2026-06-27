using System;
using System.Collections.Generic;
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
        public ControlPanel()
        {
            tabControl = new SegmentedTabControl();

            tabletPage = tabControl.AddPage("Tablet", outputModeEditor = new());
            penPage = tabControl.AddPage("Pen", penBindingEditor = new PenBindingEditor());
            buttonsPage = tabControl.AddPage("Buttons", auxBindingEditor = new AuxiliaryBindingEditor());
            mousePage = tabControl.AddPage("Mouse", mouseBindingEditor = new MouseBindingEditor(), false);
            toolsPage = tabControl.AddPage("Tools", new Panel { Padding = 5, Content = toolEditor = new() });
            filtersPage = tabControl.AddPage("Filters", new Panel { Padding = 5, Content = filterEditor = new() });
            placeholderPage = tabControl.AddPage("Info", new Panel
            {
                Padding = 5,
                Content = placeholder = new Placeholder
                {
                    Text = "No tablets are detected."
                }
            }, false);
            consolePage = tabControl.AddPage("Console", new Panel { Padding = 5, Content = logView = new() });

            this.Content = tabControl;

            outputModeEditor.ProfileBinding.Bind(ProfileBinding);
            penBindingEditor.ProfileBinding.Bind(ProfileBinding);
            auxBindingEditor.ProfileBinding.Bind(ProfileBinding);
            mouseBindingEditor.ProfileBinding.Bind(ProfileBinding);
            filterEditor.StoreCollectionBinding.Bind(ProfileBinding.Child(p => p!.Filters)!);
            toolEditor.StoreCollectionBinding.Bind(App.Current, a => a.Settings.Tools);

            outputModeEditor.SetDisplaySize(DesktopInterop.VirtualScreen?.Displays);

            Log.Output += (_, message) => Application.Instance.AsyncInvoke(() =>
            {
                if (message.Level > LogLevel.Info)
                {
                    tabControl.SelectedPage = consolePage;
                }
            });
        }

        private SegmentedTabControl tabControl;
        private SegmentedTabPage tabletPage, penPage, buttonsPage, mousePage, toolsPage, filtersPage, placeholderPage, consolePage;
        private Placeholder placeholder;
        private LogView logView;
        private OutputModeEditor outputModeEditor;
        private BindingEditor penBindingEditor, auxBindingEditor, mouseBindingEditor;
        private List<BindingEditor> wheelBindingEditors = [];
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

            if (tablet != null)
            {
                bool switchToTablet = tabControl.SelectedPage == placeholderPage;

                SetPageVisibility(placeholderPage, false);
                SetPageVisibility(tabletPage, true);
                SetPageVisibility(penPage, true);
                SetPageVisibility(buttonsPage, tablet.Properties.Specifications.AuxiliaryButtons != null);
                SetPageVisibility(toolsPage, true);
                SetPageVisibility(filtersPage, true);

                for (int i = 0; i < wheelBindingEditors.Count; i++)
                    SetPageVisibility(wheelPages[i], (tablet.Properties.Specifications.Wheels?.Count ?? 0) > i);

                SetPageVisibility(mousePage, tablet.Properties.Specifications.MouseButtons != null);

                if (switchToTablet)
                    tabControl.SelectedIndex = 0;
            }
            else
            {
                SetPageVisibility(placeholderPage, true);
                SetPageVisibility(tabletPage, false);
                SetPageVisibility(penPage, false);
                SetPageVisibility(buttonsPage, false);
                SetPageVisibility(toolsPage, false);
                SetPageVisibility(filtersPage, false);
                foreach (var page in wheelPages)
                    SetPageVisibility(page, false);
                SetPageVisibility(mousePage, false);

                if (tabControl.SelectedPage != consolePage)
                {
                    tabControl.SelectedPage = placeholderPage;
                }
            }

            SetPageVisibility(consolePage, true);
        });

        private void OnTabletChanged(TabletReference? tablet)
        {
            // ensure we have enough wheel binding editors
            int tabletWheels = tablet?.Properties.Specifications.Wheels?.Count ?? 0;
            if (tabletWheels > wheelBindingEditors.Count)
            {
                for (int i = wheelBindingEditors.Count; i < tabletWheels; i++)
                {
                    var wheelBindingEditor = new WheelBindingEditor(i);
                    wheelBindingEditor.ProfileBinding.Bind(ProfileBinding);
                    var pageIndex = tabControl.IndexOf(toolsPage);
                    wheelBindingEditors.Add(wheelBindingEditor);
                    var wheelPage = new SegmentedTabPage($"Wheel {i + 1}", wheelBindingEditor, false);
                    wheelPages.Add(wheelPage);
                    tabControl.InsertPage(pageIndex, wheelPage);
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

        private readonly List<SegmentedTabPage> wheelPages = [];

        private void SetPageVisibility(SegmentedTabPage page, bool visible) => tabControl.SetPageVisible(page, visible);
    }
}
