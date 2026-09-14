using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Tablet;

#nullable enable

namespace OpenTabletDriver.Desktop.Binding
{
    /// <summary>
    /// Runs a user script when the bound control is pressed/released. The script
    /// comes from inline source or a file path, and executes with an installed
    /// runtime (Node.js, Python 3.x, AppleScript on macOS). Syntax is checked
    /// when the binding is applied and failures are logged as notifications.
    /// </summary>
    [PluginName(PLUGIN_NAME)]
    public class ScriptBinding : IStateBinding
    {
        private const string PLUGIN_NAME = "Run Script";
        private const int RunTimeoutMs = 30_000;

        [Property("Runtime"), PropertyValidated(nameof(ValidRuntimes)),
         ToolTip("Which installed runtime executes the script.")]
        public string? Runtime { set; get; }

        [Property("Script"), MultilineProperty,
         ToolTip("Inline script source. Ignored when Script Path is set.")]
        public string? Script { set; get; }

        [Property("Script Path"),
         ToolTip("Path to a script file. Takes precedence over the inline script.")]
        public string? ScriptPath { set; get; }

        // ---- runtime discovery ---------------------------------------------

        private static IReadOnlyDictionary<string, string>? runtimeExes;

        /// <summary>Display name → executable path for every runtime found on this machine.</summary>
        private static IReadOnlyDictionary<string, string> RuntimeExes => runtimeExes ??= DetectRuntimes();

        public static IEnumerable<string> ValidRuntimes => RuntimeExes.Keys;

        private static Dictionary<string, string> DetectRuntimes()
        {
            var found = new Dictionary<string, string>();

            if (Which("node") is { } node)
                found["Node.js"] = node;

            // Every python3 / python3.x on the search path, versioned entries included.
            foreach (var python in FindPythons())
                found[$"Python ({Path.GetFileName(python)})"] = python;

            if (OperatingSystem.IsMacOS() && Which("osascript") is { } osascript)
                found["AppleScript"] = osascript;

            if (found.Count == 0)
                found["None found (install node/python)"] = string.Empty;

            return found;
        }

        private static IEnumerable<string> SearchDirs()
        {
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            // ponytail: app-spawned daemons get the bare launchd PATH; add the
            // usual package-manager prefixes. Extend if a user's runtime lives elsewhere.
            if (!OperatingSystem.IsWindows())
            {
                dirs.Add("/opt/homebrew/bin");
                dirs.Add("/usr/local/bin");
            }
            return dirs.Distinct();
        }

        private static string? Which(string name)
        {
            var exts = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : new[] { string.Empty };
            foreach (var dir in SearchDirs())
                foreach (var ext in exts)
                {
                    var candidate = Path.Combine(dir, name + ext);
                    if (File.Exists(candidate))
                        return candidate;
                }
            return null;
        }

        private static IEnumerable<string> FindPythons()
        {
            var names = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pattern = OperatingSystem.IsWindows()
                ? new Regex(@"^python3?(\.\d+)?\.exe$", RegexOptions.IgnoreCase)
                : new Regex(@"^python3(\.\d+)?$");

            foreach (var dir in SearchDirs())
            {
                if (!Directory.Exists(dir))
                    continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir); }
                catch { continue; }
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (pattern.IsMatch(name))
                        names.TryAdd(name, file); // first hit on the path wins per name
                }
            }
            return names.Values;
        }

        // ---- validation ("compile checker") --------------------------------

        [OnDependencyLoad]
        public void Validate()
        {
            var (exe, source, isFile, error) = Resolve();
            if (error != null)
            {
                Log.WriteNotify(PLUGIN_NAME, error, LogLevel.Error);
                return;
            }

            // Off-thread: checkers spawn a process and must not stall SetSettings.
            _ = Task.Run(() =>
            {
                var check = SyntaxCheck(exe!, source!, isFile);
                if (check != null)
                    Log.WriteNotify(PLUGIN_NAME, $"Script failed syntax check: {check}", LogLevel.Error);
                else
                    Log.Write(PLUGIN_NAME, $"Script OK ({Runtime})");
            });
        }

        /// <summary>Returns stderr on failure, null when the script parses.</summary>
        private string? SyntaxCheck(string exe, string source, bool isFile)
        {
            try
            {
                // Checkers want a file: stage inline source into a temp file.
                var file = isFile ? source : StageTempScript(source);
                try
                {
                    return Runtime switch
                    {
                        "AppleScript" => RunCapture(Which("osacompile") ?? "osacompile",
                            new[] { "-o", Path.Combine(Path.GetTempPath(), "otd-script-check.scpt"), file }),
                        "Node.js" => RunCapture(exe, new[] { "--check", file }),
                        _ when Runtime?.StartsWith("Python") == true => RunCapture(exe, new[] { "-m", "py_compile", file }),
                        _ => null // unknown runtime: skip the check, Run() still works
                    };
                }
                finally
                {
                    if (!isFile)
                        File.Delete(file);
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        private string StageTempScript(string source)
        {
            var ext = Runtime switch
            {
                "Node.js" => ".js",
                "AppleScript" => ".applescript",
                _ => ".py",
            };
            var file = Path.Combine(Path.GetTempPath(), $"otd-script-{Guid.NewGuid():N}{ext}");
            File.WriteAllText(file, source);
            return file;
        }

        /// <summary>Run to completion, return stderr when exit != 0, else null.</summary>
        private static string? RunCapture(string exe, string[] args)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi)!;
            // Drain async first: ReadToEnd before WaitForExit deadlocks on a hung child.
            var stderrTask = proc.StandardError.ReadToEndAsync();
            _ = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(RunTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return $"timed out after {RunTimeoutMs / 1000}s";
            }
            var stderr = stderrTask.GetAwaiter().GetResult();
            return proc.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(stderr) ? $"exit code {proc.ExitCode}" : stderr.Trim());
        }

        // ---- execution ------------------------------------------------------

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            Run();
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
        }

        private void Run()
        {
            var (exe, source, isFile, error) = Resolve();
            if (error != null)
            {
                Log.Write(PLUGIN_NAME, error, LogLevel.Error);
                return;
            }

            // Fire and forget off the report thread; log failures.
            _ = Task.Run(() =>
            {
                try
                {
                    var args = BuildRunArgs(source!, isFile);
                    var stderr = RunCapture(exe!, args);
                    if (stderr != null)
                        Log.Write(PLUGIN_NAME, $"Script failed: {stderr}", LogLevel.Error);
                }
                catch (Exception e)
                {
                    Log.Exception(e);
                }
            });
        }

        private string[] BuildRunArgs(string source, bool isFile)
        {
            return Runtime switch
            {
                _ when Runtime?.StartsWith("Python") == true =>
                    isFile ? new[] { source } : new[] { "-c", source },
                // Node.js / AppleScript / fallback share the -e inline flag.
                _ => isFile ? new[] { source } : new[] { "-e", source },
            };
        }

        /// <summary>Resolve runtime + script source. error != null when unusable.</summary>
        private (string? exe, string? source, bool isFile, string? error) Resolve()
        {
            if (Runtime == null || !RuntimeExes.TryGetValue(Runtime, out var exe) || string.IsNullOrEmpty(exe))
                return (null, null, false, $"No usable runtime selected ('{Runtime}').");

            if (!string.IsNullOrWhiteSpace(ScriptPath))
            {
                var path = ScriptPath.Trim();
                return File.Exists(path)
                    ? (exe, path, true, null)
                    : (null, null, false, $"Script file not found: {path}");
            }

            if (!string.IsNullOrWhiteSpace(Script))
                return (exe, Script, false, null);

            return (null, null, false, "No script set (fill in Script or Script Path).");
        }

        public override string ToString() => $"{PLUGIN_NAME}: {Runtime}";
    }
}
