using System.Text;

namespace Pkgdrift.Tests;

public sealed class IgnoreTests
{
    [Fact]
    public void Ignore_skips_matching_names_at_any_depth_and_preserves_default_scan()
    {
        using var tree = new TempTree();
        tree.Write("app/app.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"Serilog\" Version=\"3.1.0\" />\n  </ItemGroup>\n</Project>");
        foreach (var path in new[] { "samples/demo/demo.csproj", "src/samples/inner.csproj", "vendor/Samples/vend.csproj" })
            tree.Write(path, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"Serilog\" Version=\"2.0.0\" />\n  </ItemGroup>\n</Project>");

        var defaultResult = Run(tree.Root);
        Assert.Equal(1, defaultResult.Code);
        Assert.Equal("samples/demo/demo.csproj:1: PD004 no TargetFramework or TargetFrameworks\nsamples/demo/demo.csproj:3: PD001 Serilog 2.0.0 differs from 3.1.0 in app/app.csproj\nsrc/samples/inner.csproj:1: PD004 no TargetFramework or TargetFrameworks\nsrc/samples/inner.csproj:3: PD001 Serilog 2.0.0 differs from 3.1.0 in app/app.csproj\nvendor/Samples/vend.csproj:1: PD004 no TargetFramework or TargetFrameworks\nvendor/Samples/vend.csproj:3: PD001 Serilog 2.0.0 differs from 3.1.0 in app/app.csproj\n6 problems in 3 projects\n", defaultResult.Out);
        Assert.Equal((0, "ok\n", string.Empty), Run("--ignore", "samples", tree.Root));
        Assert.Equal((0, "{\"projects\":1,\"findings\":[],\"problems\":0}\n", string.Empty), Run("--ignore", "SAMPLES", "--ignore", "samples", "--format", "json", tree.Root));
        Assert.Equal((0, "ok\n", string.Empty), Run("--ignore", "samples", "--ignore", "app", tree.Root));

        tree.Write("bin/bin.csproj", Project(""));
        tree.Write("samples/found.csproj", Project(""));
        var scanned = new ProjectScanner().Scan(tree.Root).Select(p => p.Path).ToArray();
        Assert.Contains("samples/found.csproj", scanned);
        Assert.DoesNotContain(scanned, p => p.StartsWith("bin/", StringComparison.Ordinal));
    }

    [Fact]
    public void Ignore_does_not_skip_scan_root_and_controls_props_detection()
    {
        using var tree = new TempTree();
        tree.Write("app/app.csproj", Project("<TargetFramework>net10.0</TargetFramework>"));
        var rootNamedIgnored = tree.Write("samples/app/app.csproj", Project("<TargetFramework>net10.0</TargetFramework>"));
        var scanRoot = Path.GetDirectoryName(Path.GetDirectoryName(rootNamedIgnored)!)!;
        Assert.Equal((0, "{\"projects\":1,\"findings\":[],\"problems\":0}\n", string.Empty), Run("--ignore", "samples", "--format", "json", scanRoot));

        using var propsTree = new TempTree();
        propsTree.Write("ignored/Directory.Build.props", "<Directory/>");
        propsTree.Write("p/p.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>");
        Assert.Equal((0, "ok\n", string.Empty), Run(propsTree.Root));
        Assert.Equal((1, "p/p.csproj:1: PD004 no TargetFramework or TargetFrameworks\n1 problem in 1 project\n", string.Empty), Run("--ignore", "ignored", propsTree.Root));
    }

    [Theory]
    [InlineData(new[] { "a/b" }, "pkgdrift: invalid --ignore value \"a/b\"")]
    [InlineData(new[] { "a\\b" }, "pkgdrift: invalid --ignore value \"a\\b\"")]
    [InlineData(new[] { "." }, "pkgdrift: invalid --ignore value \".\"")]
    [InlineData(new[] { ".." }, "pkgdrift: invalid --ignore value \"..\"")]
    [InlineData(new[] { "" }, "pkgdrift: invalid --ignore value \"\"")]
    public void Ignore_rejects_invalid_values(string[] values, string expected)
    {
        using var tree = new TempTree();
        var result = Run(new[] { "--ignore" }.Concat(values).Append(tree.Root).ToArray());
        AssertError(result, expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--format json DIR")]
    public void Ignore_requires_value(string trailing)
    {
        var result = trailing.Length == 0 ? Run("--ignore") : Run("--ignore", "--format", "json", "DIR");
        AssertError(result, "pkgdrift: missing value for --ignore");
    }

    [Fact]
    public void Ignore_combines_with_other_options_and_documentation_describes_feature()
    {
        using var tree = new TempTree();
        tree.Write("samples/p.csproj", "<Project />");
        Assert.Equal((0, "ok\n", string.Empty), Run("--disable", "PD004", "--ignore", "samples", tree.Root));
        tree.Write("app/p.csproj", "<Project><TargetFramework>net10.0</TargetFramework></Project>");
        Assert.Equal((0, "{\"projects\":1,\"findings\":[],\"problems\":0}\n", string.Empty), Run("--format", "json", "--ignore", "samples", tree.Root));

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pkgdrift.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var readme = File.ReadAllText(Path.Combine(root!.FullName, "README.md"));
        Assert.Contains("pkgdrift [--format text|json|markdown] [--disable RULE]... [--only RULE]... [--ignore DIR]... DIR", readme);
        Assert.Contains("## Ignoring directories", readme);
        Assert.Contains("pkgdrift --ignore samples --ignore legacy .", readme);
    }

    static string Project(string body) => $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{body}</PropertyGroup></Project>";

    static (int Code, string Out, string Err) Run(params string[] args) => Run((IEnumerable<string>)args);

    static (int Code, string Out, string Err) Run(IEnumerable<string> args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        var code = Program.Run(args.ToArray(), stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    static void AssertError((int Code, string Out, string Err) result, string expected)
    {
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Out);
        Assert.Equal(expected + "\n", result.Err);
    }

    sealed class TempTree : IDisposable
    {
        public string Root { get; }
        public TempTree() : this(Path.Combine(Path.GetTempPath(), "pkgdrift-ignore-tests-" + Guid.NewGuid().ToString("N"))) { }
        public TempTree(string root) { Root = root; Directory.CreateDirectory(root); }
        public string Write(string relative, string text)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
