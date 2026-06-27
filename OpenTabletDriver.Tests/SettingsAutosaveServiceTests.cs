using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public sealed class SettingsAutosaveServiceTests
    {
        [Fact]
        public async Task ProfilePropertyChangeSavesAndApplies()
        {
            using var env = new AutosaveTestEnvironment();
            var settings = CreateSettings();
            using var autosave = env.CreateService();

            autosave.Track(settings, false);
            settings.Profiles[0].BindingSettings.DisablePressure = true;

            await env.WaitForApply();
            Assert.Contains("\"DisablePressure\": true", await File.ReadAllTextAsync(env.SettingsPath));
        }

        [Fact]
        public async Task NestedAreaChangeSavesAndApplies()
        {
            using var env = new AutosaveTestEnvironment();
            var settings = CreateSettings();
            using var autosave = env.CreateService();

            autosave.Track(settings, false);
            settings.Profiles[0].AbsoluteModeSettings!.Tablet.Width = 42;

            await env.WaitForApply();
            Assert.Contains("\"Width\": 42.0", await File.ReadAllTextAsync(env.SettingsPath));
        }

        [Fact]
        public async Task PluginSettingValueChangeSavesAndApplies()
        {
            using var env = new AutosaveTestEnvironment();
            var settings = CreateSettings();
            var pluginSetting = settings.Profiles[0].OutputMode["SyntheticValue"];
            using var autosave = env.CreateService();

            autosave.Track(settings, false);
            pluginSetting.SetValue("changed");

            await env.WaitForApply();
            Assert.Contains("\"SyntheticValue\"", await File.ReadAllTextAsync(env.SettingsPath));
            Assert.Contains("\"changed\"", await File.ReadAllTextAsync(env.SettingsPath));
        }

        [Fact]
        public async Task PluginStoreEnableChangeSavesAndApplies()
        {
            using var env = new AutosaveTestEnvironment();
            var settings = CreateSettings();
            var store = new PluginSettingStore(typeof(AbsoluteMode), false);
            settings.Tools.Add(store);
            using var autosave = env.CreateService();

            autosave.Track(settings, false);
            store.Enable = true;

            await env.WaitForApply();
            Assert.Contains("\"Enable\": true", await File.ReadAllTextAsync(env.SettingsPath));
        }

        [Fact]
        public async Task GeneratedTabletProfileSavesByTabletName()
        {
            using var env = new AutosaveTestEnvironment();
            var settings = CreateSettings("Existing Tablet");
            using var autosave = env.CreateService();

            autosave.Track(settings, false);
            settings.Profiles.Add(CreateProfile("Gaomon WH851"));

            await env.WaitForApply();
            Assert.Contains("\"Tablet\": \"Gaomon WH851\"", await File.ReadAllTextAsync(env.SettingsPath));
        }

        private static Settings CreateSettings(string tabletName = "Gaomon WH851")
        {
            return new Settings
            {
                Profiles = new ProfileCollection
                {
                    CreateProfile(tabletName)
                },
                Tools = new PluginSettingStoreCollection()
            };
        }

        private static Profile CreateProfile(string tabletName)
        {
            return new Profile
            {
                Tablet = tabletName,
                OutputMode = new PluginSettingStore(typeof(AbsoluteMode)),
                AbsoluteModeSettings = new AbsoluteModeSettings
                {
                    Display = CreateArea(),
                    Tablet = CreateArea()
                },
                RelativeModeSettings = RelativeModeSettings.GetDefaults(),
                BindingSettings = new BindingSettings()
            };
        }

        private static AreaSettings CreateArea()
        {
            return new AreaSettings
            {
                Width = 100,
                Height = 100,
                X = 50,
                Y = 50
            };
        }

        private sealed class AutosaveTestEnvironment : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "otd-autosave-tests", Guid.NewGuid().ToString("N"));

            public string SettingsPath => Path.Combine(directory, "settings.json");
            public int ApplyCount { get; private set; }

            public SettingsAutosaveService CreateService()
            {
                return new SettingsAutosaveService(
                    () => new FileInfo(SettingsPath),
                    settings =>
                    {
                        ApplyCount++;
                        return Task.CompletedTask;
                    },
                    TimeSpan.FromMilliseconds(10)
                );
            }

            public async Task WaitForApply()
            {
                for (var i = 0; i < 50; i++)
                {
                    if (ApplyCount > 0)
                        return;

                    await Task.Delay(20);
                }

                Assert.Fail("Autosave did not apply settings.");
            }

            public void Dispose()
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }
    }
}
