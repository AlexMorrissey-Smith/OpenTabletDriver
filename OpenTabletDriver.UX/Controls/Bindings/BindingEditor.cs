using System;
using Eto.Forms;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletDriver.UX.Controls.Bindings
{
    public abstract class BindingEditor : Panel
    {
        // Reads/writes BindingSettingsOverride when set (app-specific editing), otherwise
        // falls back to the selected tablet Profile's own BindingSettings (today's behavior).
        public DirectBinding<BindingSettings> SettingsBinding => new DelegateBinding<BindingSettings>(
            () => BindingSettingsOverride ?? Profile?.BindingSettings!,
            v =>
            {
                if (BindingSettingsOverride != null)
                    BindingSettingsOverride = v;
                else if (Profile != null)
                    Profile.BindingSettings = v;
            },
            h => { ProfileChanged += h; BindingSettingsOverrideChanged += h; },
            h => { ProfileChanged -= h; BindingSettingsOverrideChanged -= h; }
        );

        private Profile? profile;
        public Profile? Profile
        {
            set
            {
                this.profile = value;
                this.OnProfileChanged();
            }
            get => this.profile;
        }

        public event EventHandler<EventArgs>? ProfileChanged;

        protected virtual void OnProfileChanged() => ProfileChanged?.Invoke(this, new EventArgs());

        public BindableBinding<BindingEditor, Profile?> ProfileBinding
        {
            get
            {
                return new BindableBinding<BindingEditor, Profile?>(
                    this,
                    c => c.Profile,
                    (c, v) => c.Profile = v,
                    (c, h) => c.ProfileChanged += h,
                    (c, h) => c.ProfileChanged -= h
                );
            }
        }

        private BindingSettings? bindingSettingsOverride;
        public BindingSettings? BindingSettingsOverride
        {
            set
            {
                this.bindingSettingsOverride = value;
                this.BindingSettingsOverrideChanged?.Invoke(this, EventArgs.Empty);
            }
            get => this.bindingSettingsOverride;
        }

        public event EventHandler<EventArgs>? BindingSettingsOverrideChanged;
    }
}
