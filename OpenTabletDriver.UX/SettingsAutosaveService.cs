using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX
{
    public sealed class SettingsAutosaveService : IDisposable
    {
        private readonly Func<FileInfo> settingsFileProvider;
        private readonly Func<Settings, Task> applySettings;
        private readonly TimeSpan debounceDelay;
        private readonly SemaphoreSlim flushLock = new(1, 1);
        private readonly List<Action> subscriptions = [];
        private readonly HashSet<object> observed = new(ReferenceEqualityComparer.Instance);
        private readonly object gate = new();

        private CancellationTokenSource? debounce;
        private Settings? settings;
        private bool disposed;

        public SettingsAutosaveService(
            Func<FileInfo> settingsFileProvider,
            Func<Settings, Task> applySettings,
            TimeSpan? debounceDelay = null)
        {
            this.settingsFileProvider = settingsFileProvider;
            this.applySettings = applySettings;
            this.debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(750);
        }

        public void Track(Settings? settings, bool scheduleSave)
        {
            if (disposed)
                return;

            this.settings = settings;
            RebuildSubscriptions();

            if (scheduleSave)
                ScheduleSave();
        }

        public async Task FlushNow()
        {
            CancelDebounce();
            await FlushCore(CancellationToken.None);
        }

        private void HandleObservedChange(object? sender, EventArgs e)
        {
            RebuildSubscriptions();
            ScheduleSave();
        }

        private void RebuildSubscriptions()
        {
            foreach (var unsubscribe in subscriptions)
                unsubscribe();

            subscriptions.Clear();
            observed.Clear();
            Observe(settings);
        }

        private void Observe(object? obj)
        {
            if (obj == null)
                return;

            var type = obj.GetType();
            if (IsLeaf(type) || !observed.Add(obj))
                return;

            if (obj is INotifyPropertyChanged propertyChanged)
            {
                PropertyChangedEventHandler handler = HandleObservedChange;
                propertyChanged.PropertyChanged += handler;
                subscriptions.Add(() => propertyChanged.PropertyChanged -= handler);
            }

            if (obj is INotifyCollectionChanged collectionChanged)
            {
                NotifyCollectionChangedEventHandler handler = HandleObservedChange;
                collectionChanged.CollectionChanged += handler;
                subscriptions.Add(() => collectionChanged.CollectionChanged -= handler);
            }

            if (obj is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                    Observe(item);
            }

            foreach (var property in GetObservableProperties(type))
            {
                try
                {
                    Observe(property.GetValue(obj));
                }
                catch
                {
                }
            }
        }

        private static IEnumerable<PropertyInfo> GetObservableProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttribute<JsonPropertyAttribute>() != null);
        }

        private static bool IsLeaf(Type type)
        {
            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(TimeSpan) ||
                   type == typeof(JToken) ||
                   type.IsSubclassOf(typeof(JToken));
        }

        private void ScheduleSave()
        {
            if (disposed || settings == null)
                return;

            CancellationTokenSource cts;
            lock (gate)
            {
                CancelDebounce();
                debounce = cts = new CancellationTokenSource();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(debounceDelay, cts.Token);
                    await FlushCore(cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Exception(ex);
                }
            });
        }

        private async Task FlushCore(CancellationToken token)
        {
            if (disposed || settings == null || token.IsCancellationRequested)
                return;

            await flushLock.WaitAsync(token);
            try
            {
                if (disposed || settings == null || token.IsCancellationRequested)
                    return;

                var file = settingsFileProvider();
                if (file.DirectoryName != null)
                    Directory.CreateDirectory(file.DirectoryName);

                settings.Serialize(file);
                await applySettings(settings);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
            finally
            {
                flushLock.Release();
            }
        }

        private void CancelDebounce()
        {
            debounce?.Cancel();
            debounce?.Dispose();
            debounce = null;
        }

        public void Dispose()
        {
            disposed = true;
            CancelDebounce();

            foreach (var unsubscribe in subscriptions)
                unsubscribe();

            subscriptions.Clear();
            observed.Clear();
            flushLock.Dispose();
        }
    }
}
