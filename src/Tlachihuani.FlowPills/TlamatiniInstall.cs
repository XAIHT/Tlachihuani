// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (counts every agent template of every candidate; one summary line says what was chosen)
using Microsoft.Win32;
using Tlachihuani.Contracts;

namespace Tlachihuani.FlowPills;

// The Tlamatini installation whose agents a flow runs: the agent templates, the Python that runs
// them, Tlamatini's config.json (models, Ollama) and its Temp and Templates folders. It is found
// the way Tlamatini publishes itself for companion apps (HKCU\Software\XAIHT\Tlamatini, written
// by its installer and refreshed on every launch), never by scanning drives.
public sealed record TlamatiniInstall(string AgentsRoot, string AppRoot, string Python, string ConfigPath,
    string TempFolder, string TemplatesFolder, int Templates, string FoundBy)
{
    public string PythonHome => Path.GetDirectoryName(Python) ?? "";

    public static TlamatiniInstall Discover(FlowPillsSettings settings)
    {
        var looked = new List<string>();
        foreach (var (path, source) in Candidates(settings))
        {
            string root;
            try { root = Path.GetFullPath(path.Trim().Trim('"')); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            int count = CountTemplates(root);
            looked.Add($"{source}: {root} ({count} agent templates)");
            if (count == 0 || !File.Exists(Path.Combine(root, "starter", "starter.py"))) continue;
            string agentsParent = Directory.GetParent(root)?.FullName ?? root;
            string app = AppRootOf(agentsParent);
            string python = FindPython(settings, app);
            if (python.Length == 0)
                throw new FlowPillsException("python_missing", $"Tlamatini's agents are in {root}, but no Python was found to run them. "
                    + $"Install Tlamatini, or set \"python\" in {FlowPillsPaths.SettingsFile}.");
            TraceLog.I($"Flow Pills uses the Tlamatini agents in {root} ({count} templates, found by {source}) with Python {python}");
            return new TlamatiniInstall(root, app, python, Path.Combine(agentsParent, "config.json"),
                Path.Combine(app, "Temp"), Path.Combine(app, "Templates"), count, source);
        }
        throw new FlowPillsException("agents_missing", "Tlamatini's agents were not found. Looked at:\n"
            + (looked.Count == 0 ? "(no location is known)" : string.Join("\n", looked))
            + $"\nInstall Tlamatini or start it once, or set \"agents_root\" in {FlowPillsPaths.SettingsFile}.");
    }

    private static List<(string Path, string Source)> Candidates(FlowPillsSettings settings)
    {
        var candidates = new List<(string, string)>();
        if (settings.AgentsRoot.Length > 0) candidates.Add((settings.AgentsRoot, "settings.json agents_root"));
        if (Environment.GetEnvironmentVariable("TLAMATINI_AGENTS_DIR") is { Length: > 0 } variable) candidates.Add((variable, "TLAMATINI_AGENTS_DIR"));
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\XAIHT\Tlamatini");
            if (key?.GetValue("AgentsRoot") is string agents && agents.Length > 0) candidates.Add((agents, "registry AgentsRoot"));
            if (key?.GetValue("SourceAgentsRoot") is string source && source.Length > 0) candidates.Add((source, "registry SourceAgentsRoot"));
            if (key?.GetValue("InstallLocation") is string install && install.Length > 0) candidates.Add((Path.Combine(install, "agents"), "registry InstallLocation"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        candidates.Add((Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Tlamatini", "agents"), "default install folder"));
        return candidates;
    }

    // A complete template is a folder with {name}.py and config.yaml, exactly what Tlamatini's
    // flow compiler accepts.
    internal static int CountTemplates(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return 0;
            int count = 0;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(folder);
                if (name.Equals("pools", StringComparison.OrdinalIgnoreCase) || name.StartsWith("__", StringComparison.Ordinal)) continue;
                if (File.Exists(Path.Combine(folder, name.ToLowerInvariant() + ".py")) && File.Exists(Path.Combine(folder, "config.yaml"))) count++;
            }
            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    // Installed:  C:\Tlamatini\agents           -> C:\Tlamatini (beside Tlamatini.exe)
    // Source:     {repo}\Tlamatini\agent\agents  -> {repo}, where Tlamatini keeps Temp and Templates
    private static string AppRootOf(string agentsParent)
    {
        if (File.Exists(Path.Combine(agentsParent, "Tlamatini.exe"))) return agentsParent;
        if (Path.GetFileName(agentsParent).Equals("agent", StringComparison.OrdinalIgnoreCase)
            && Directory.GetParent(agentsParent)?.Parent is { } repository) return repository.FullName;
        return agentsParent;
    }

    // The order Tlamatini's own agents use: the Python an installed Tlamatini carries, a source
    // checkout's virtual environment, PYTHON_HOME, then the PATH (without the Store alias).
    private static string FindPython(FlowPillsSettings settings, string app)
    {
        var candidates = new List<string>();
        if (settings.Python.Length > 0) candidates.Add(settings.Python);
        candidates.Add(Path.Combine(app, "python", "python.exe"));
        candidates.Add(Path.Combine(app, "venv", "Scripts", "python.exe"));
        candidates.Add(Path.Combine(app, ".venv", "Scripts", "python.exe"));
        try
        {
            using var environment = Registry.CurrentUser.OpenSubKey("Environment");
            if (environment?.GetValue("PYTHON_HOME") is string home && home.Length > 0) candidates.Add(Path.Combine(home, "python.exe"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        if (Environment.GetEnvironmentVariable("PYTHON_HOME") is { Length: > 0 } processHome) candidates.Add(Path.Combine(processHome, "python.exe"));
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!folder.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) candidates.Add(Path.Combine(folder, "python.exe"));
        foreach (var candidate in candidates)
        {
            try
            {
                string full = Path.GetFullPath(candidate.Trim().Trim('"'));
                if (File.Exists(full)) return full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return "";
    }
}
