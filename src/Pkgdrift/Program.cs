using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;

public sealed record PackageReferenceInfo(string Path, int Line, string Package, string? Version);
public sealed record ProjectInfo(string Path, int Line, IReadOnlyList<PackageReferenceInfo> References, bool HasTargetFramework);
public sealed record Finding(string Path, int Line, string Rule, string Message);

public sealed class Report
{
    public int Projects { get; }
    public IReadOnlyList<Finding> Findings { get; }
    public int Problems => Findings.Count;
    public Report(int projects, IEnumerable<Finding> findings)
    {
        Projects = projects;
        Findings = findings.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Rule, StringComparer.Ordinal).ToArray();
    }
}

public sealed class ProjectScanner
{
    public sealed class InvalidProjectXmlException(string path, Exception innerException) : Exception(path, innerException)
    {
        public string DisplayPath { get; } = path;
    }
    readonly HashSet<string> skipped;
    public ProjectScanner(IEnumerable<string>? ignored = null)
    {
        skipped = new HashSet<string>(new[] { "bin", "obj", "node_modules", ".git" }, StringComparer.OrdinalIgnoreCase);
        if (ignored is not null) skipped.UnionWith(ignored);
    }
    public IReadOnlyList<ProjectInfo> Scan(string root)
    {
        var files = Enumerate(root).Where(f => string.Equals(Path.GetExtension(f), ".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Relative(root, f), StringComparer.Ordinal).ToArray();
        var result = new List<ProjectInfo>();
        foreach (var file in files)
        {
            XDocument doc;
            try { doc = XDocument.Load(file, LoadOptions.SetLineInfo); }
            catch (System.Xml.XmlException ex) { throw new InvalidProjectXmlException(Relative(root, file), ex); }
            var rootElement = doc.Root!;
            var refs = doc.Descendants().Where(e => e.Name.LocalName == "PackageReference")
                .Select(e =>
                {
                    var include = (string?)e.Attribute("Include");
                    var versionAttribute = e.Attribute("Version");
                    var rawVersion = versionAttribute is not null
                          ? versionAttribute.Value
                          : e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value;
                    var version = rawVersion?.Trim();
                    if (string.IsNullOrEmpty(version)) version = null;
                    return string.IsNullOrWhiteSpace(include) ? null : new PackageReferenceInfo(Relative(root, file), ((IXmlLineInfo)e).LineNumber, include, version);
                }).Where(x => x is not null).Cast<PackageReferenceInfo>().ToArray();
            result.Add(new ProjectInfo(Relative(root, file), ((IXmlLineInfo)rootElement).LineNumber,
                refs, doc.Descendants().Any(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")));
        }
        return result;
    }
    public bool HasDirectoryBuildProps(string root) => Enumerate(root).Any(f => string.Equals(Path.GetFileName(f), "Directory.Build.props", StringComparison.OrdinalIgnoreCase));
    IEnumerable<string> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                if (skipped.Contains(Path.GetFileName(child))) continue;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(child);
            }
        }
    }
    static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}

public static class Rules
{
    static readonly Regex Prerelease = new(@"^[0-9]+(\.[0-9]+){0,3}-[0-9A-Za-z.-]+$", RegexOptions.CultureInvariant);
    public static Report Create(IReadOnlyList<ProjectInfo> projects, bool hasDirectoryBuildProps = false, IEnumerable<string>? disabled = null)
    {
        var off = new HashSet<string>(disabled ?? Array.Empty<string>(), StringComparer.Ordinal);
        var findings = new List<Finding>();
        var baselines = new Dictionary<string, PackageReferenceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in projects.SelectMany(p => p.References).Where(r => r.Version is not null).OrderBy(r => r.Path, StringComparer.Ordinal).ThenBy(r => r.Line))
            if (!baselines.ContainsKey(reference.Package)) baselines.Add(reference.Package, reference);
        foreach (var project in projects)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in project.References)
            {
                var v = reference.Version;
                if (!seen.Add(reference.Package)) Add("PD003", $"duplicate reference to {reference.Package}");
                if (v is null) continue;
                var baseline = baselines[reference.Package];
                if (!string.Equals(v, baseline.Version, StringComparison.Ordinal)) Add("PD001", $"{reference.Package} {v} differs from {baseline.Version} in {baseline.Path}");
                if (v.Contains('*')) Add("PD002", $"floating version {v} of {reference.Package}");
                if (Prerelease.IsMatch(v)) Add("PD005", $"prerelease version {v} of {reference.Package}");
                void Add(string id, string message) { if (!off.Contains(id)) findings.Add(new Finding(reference.Path, reference.Line, id, message)); }
            }
            if (!hasDirectoryBuildProps && !project.HasTargetFramework && !off.Contains("PD004"))
                findings.Add(new Finding(project.Path, project.Line, "PD004", "no TargetFramework or TargetFrameworks"));
        }
        return new Report(projects.Count, findings);
    }
}

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string format = "text";
        var disabled = new List<string>();
        var ignored = new List<string>();
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--format")
            {
                if (++i >= args.Length) return Error("pkgdrift: missing value for --format");
                format = args[i];
                if (format is not ("text" or "json")) return Error("pkgdrift: --format must be text or json");
            }
            else if (arg == "--disable")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) return Error("pkgdrift: missing value for --disable");
                disabled.Add(args[i]);
            }
            else if (arg == "--ignore")
            {
                if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) return Error("pkgdrift: missing value for --ignore");
                var value = args[i];
                if (value.Length == 0 || value is "." or ".." || value.Contains('/') || value.Contains('\\')) return Error($"pkgdrift: invalid --ignore value \"{value}\"");
                ignored.Add(value);
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal)) return Error($"pkgdrift: unknown option \"{arg}\"");
            else positional.Add(arg);
        }
        if (positional.Count == 0) return Error("pkgdrift: directory required");
        if (positional.Count != 1) return Error("pkgdrift: exactly one directory required");
        foreach (var id in disabled)
            if (id is not ("PD001" or "PD002" or "PD003" or "PD004" or "PD005")) return Error($"pkgdrift: unknown rule \"{id}\"");
        var dir = positional[0];
        if (!Directory.Exists(dir)) return Error($"pkgdrift: {dir}: directory not found");
        Report report;
        try
        {
            var scanner = new ProjectScanner(ignored);
            var projects = scanner.Scan(dir);
            report = Rules.Create(projects, scanner.HasDirectoryBuildProps(dir), disabled);
        }
        catch (ProjectScanner.InvalidProjectXmlException ex)
        {
            return Error($"pkgdrift: {ex.DisplayPath}: invalid XML");
        }
        if (format == "json")
        {
            var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            stdout.Write(JsonSerializer.Serialize(new { projects = report.Projects, findings = report.Findings.Select(f => new { path = f.Path, line = f.Line, rule = f.Rule, message = f.Message }), problems = report.Problems }, options));
            stdout.Write('\n');
        }
        else if (report.Problems == 0) stdout.WriteLine("ok");
        else
        {
            foreach (var f in report.Findings) stdout.WriteLine($"{f.Path}:{f.Line}: {f.Rule} {f.Message}");
            var projectCount = report.Findings.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count();
            stdout.WriteLine($"{report.Problems} problem{(report.Problems == 1 ? "" : "s")} in {projectCount} project{(projectCount == 1 ? "" : "s")}");
        }
        return report.Problems == 0 ? 0 : 1;
        int Error(string message) { stderr.WriteLine(message); return 2; }
    }
}
