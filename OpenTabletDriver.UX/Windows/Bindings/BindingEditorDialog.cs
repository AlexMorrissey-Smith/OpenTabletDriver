using System;
using System.Text;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.UX.Controls;
using OpenTabletDriver.UX.Controls.Generic;
using OpenTabletDriver.UX.Controls.Generic.Reflection;
using IBinding = OpenTabletDriver.Plugin.IBinding;

namespace OpenTabletDriver.UX.Windows.Bindings
{
    /// <summary>
    /// Single unified binding editor: capture a key/mouse button, or pick any other action from
    /// one dropdown. Replaces the old split "quick" + "advanced" dialogs.
    /// </summary>
    public class BindingEditorDialog : Dialog<PluginSettingStore?>
    {
        public BindingEditorDialog(PluginSettingStore? currentBinding = null)
        {
            Title = "Binding Editor";
            Result = currentBinding;
            current = currentBinding;
            Resizable = true;
            ClientSize = new Size(420, 460);

            bindingController = new BindingController { Height = 90 };
            actionDropDown = new TypeDropDown<IBinding>();
            actionSettings = new PluginSettingStoreEditor<IBinding>();

            this.Content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = 10,
                Spacing = 8,
                Items =
                {
                    new StackLayoutItem
                    {
                        Control = new Group
                        {
                            Text = "Key or Mouse Button",
                            Content = bindingController
                        }
                    },
                    new Group
                    {
                        Text = "Or pick an action",
                        Content = new StackLayout
                        {
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Spacing = 5,
                            Items = { actionDropDown, actionSettings }
                        }
                    },
                    new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 5,
                        Items =
                        {
                            new StackLayoutItem { Expand = true, Control = new Button(ClearBinding) { Text = "Clear" } },
                            new StackLayoutItem { Expand = true, Control = new Button(ApplyBinding) { Text = "Apply" } }
                        }
                    }
                }
            };

            bindingController.StoreChanged += (_, _) =>
            {
                if (updating) return;
                current = bindingController.Store;
                updating = true;
                actionDropDown.SelectedValue = null;
                actionSettings.Store = null;
                updating = false;
            };

            actionDropDown.SelectedValueChanged += (_, _) =>
            {
                if (updating || actionDropDown.SelectedItem == null) return;
                current = new PluginSettingStore(actionDropDown.SelectedItem);
                updating = true;
                bindingController.Store = null;
                updating = false;
                actionSettings.Store = current;
            };

            // Initialize from the existing binding without triggering the change handlers.
            updating = true;
            if (currentBinding != null && IsKeyOrMouse(currentBinding.Path))
            {
                bindingController.Store = currentBinding;
                actionDropDown.SelectedValue = null;
            }
            else if (currentBinding != null)
            {
                actionDropDown.SelectedValue = currentBinding.GetTypeInfo();
                actionSettings.Store = currentBinding;
            }
            else
            {
                actionDropDown.SelectedValue = null;
            }
            updating = false;
        }

        private readonly BindingController bindingController;
        private readonly TypeDropDown<IBinding> actionDropDown;
        private readonly PluginSettingStoreEditor<IBinding> actionSettings;
        private PluginSettingStore? current;
        private bool updating;

        private static bool IsKeyOrMouse(string? path) =>
            path == typeof(KeyBinding).FullName ||
            path == typeof(MultiKeyBinding).FullName ||
            path == typeof(MouseBinding).FullName;

        private void ClearBinding(object? sender, EventArgs e) => Close(null);

        private void ApplyBinding(object? sender, EventArgs e) => Close(current);

        private static string ParseMouseButton(MouseEventArgs e) => e.Buttons switch
        {
            MouseButtons.Primary => nameof(MouseButton.Left),
            MouseButtons.Middle => nameof(MouseButton.Middle),
            MouseButtons.Alternate => nameof(MouseButton.Right),
            _ => nameof(MouseButton.None)
        };

        private class BindingController : TextArea
        {
            public BindingController()
            {
                TextAlignment = TextAlignment.Center;
                ReadOnly = true;
                ToolTip = TOOLTIP;
                Text = TOOLTIP;
            }

            private const string TOOLTIP = "Press a key, combination of keys, or a mouse button.";

            public event EventHandler? StoreChanged;

            private PluginSettingStore? store;
            public PluginSettingStore? Store
            {
                set
                {
                    this.store = value;
                    this.Text = store?.GetHumanReadableString() ?? TOOLTIP;
                    StoreChanged?.Invoke(this, EventArgs.Empty);
                }
                get => this.store;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                Keys keys = e.KeyData;
                if (keys == Keys.None)
                    return;

                if (keys.HasFlag(Keys.Alt | Keys.LeftAlt) || keys.HasFlag(Keys.Alt | Keys.RightAlt))
                    keys &= ~Keys.Alt;
                else if (keys.HasFlag(Keys.Control | Keys.LeftControl) || keys.HasFlag(Keys.Control | Keys.RightControl))
                    keys &= ~Keys.Control;
                else if (keys.HasFlag(Keys.Shift | Keys.LeftShift) || keys.HasFlag(Keys.Shift | Keys.RightShift))
                    keys &= ~Keys.Shift;
                else if (keys.HasFlag(Keys.Application | Keys.LeftApplication) || keys.HasFlag(Keys.Application | Keys.RightApplication))
                    keys &= ~Keys.Application;

                PluginSettingStore newStore;
                if ((keys & Keys.ModifierMask) == 0)
                {
                    newStore = new PluginSettingStore(typeof(KeyBinding));
                    newStore[nameof(KeyBinding.Key)].SetValue(keys.ToString());
                }
                else
                {
                    newStore = new PluginSettingStore(typeof(MultiKeyBinding));
                    newStore[nameof(MultiKeyBinding.Keys)].SetValue(CreateShortcutString(keys));
                }
                this.Store = newStore;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                var newStore = new PluginSettingStore(typeof(MouseBinding));
                newStore[nameof(MouseBinding.Button)].SetValue(ParseMouseButton(e));
                this.Store = newStore;
            }

            private static void AppendSeparator(StringBuilder sb, string separator, string text)
            {
                if (sb.Length > 0)
                    sb.Append(separator);
                sb.Append(text);
            }

            private static string CreateShortcutString(Keys keys)
            {
                var sb = new StringBuilder();
                if (keys.HasFlag(Keys.Application)) AppendSeparator(sb, "+", nameof(Keys.Application));
                if (keys.HasFlag(Keys.Control)) AppendSeparator(sb, "+", nameof(Keys.Control));
                if (keys.HasFlag(Keys.Shift)) AppendSeparator(sb, "+", nameof(Keys.Shift));
                if (keys.HasFlag(Keys.Alt)) AppendSeparator(sb, "+", nameof(Keys.Alt));
                AppendSeparator(sb, "+", (keys & Keys.KeyMask).ToString());
                return sb.ToString();
            }
        }
    }
}
