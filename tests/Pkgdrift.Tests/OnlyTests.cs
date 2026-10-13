using System.Text;

namespace Pkgdrift.Tests;

public sealed class OnlyTests
{
    [Fact]
    public void Cli_only_matches_documented_text_and_json_outputs()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        var selectedText = Run("--only", "PD001", "--only", "PD005", tree.Root);
        Assert.Equal(1, selectedText.Code);
        Assert.Equal(
            "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\n" +
            "lib/lib.csproj:4: PD001 Serilog 3.1.0-beta.2 differs from * in app/app.csproj\n" +
            "lib/lib.csproj:4: PD005 prerelease version 3.1.0-beta.2 of Serilog\n" +
            "3 problems in 1 project\n",
            selectedText.Out);
        Assert.Empty(selectedText.Err);

        var pd004 = Run("--only", "PD004", tree.Root);
        Assert.Equal((1, "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks\n1 problem in 1 project\n", string.Empty), pd004);

        var pd002Json = Run("--only", "PD002", "--format", "json", tree.Root);
        Assert.Equal(1, pd002Json.Code);
        Assert.Equal(
            "{\"projects\":2,\"findings\":[{\"path\":\"app/app.csproj\",\"line\":7,\"rule\":\"PD002\",\"message\":\"floating version * of Serilog\"}],\"problems\":1}\n",
            pd002Json.Out);
        Assert.Empty(pd002Json.Err);

        var pd001Json = Run("--only", "PD001", "--format", "json", tree.Root);
        Assert.Equal(1, pd001Json.Code);
        Assert.Equal(
            "{\"projects\":2,\"findings\":[{\"path\":\"lib/lib.csproj\",\"line\":3,\"rule\":\"PD001\",\"message\":\"Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\"},{\"path\":\"lib/lib.csproj\",\"line\":4,\"rule\":\"PD001\",\"message\":\"Serilog 3.1.0-beta.2 differs from * in app/app.csproj\"}],\"problems\":2}\n",
            pd001Json.Out);
        Assert.Empty(pd001Json.Err);
    }

    [Fact]
    public void Cli_only_combines_with_disable_and_repeated_rules_are_idempotent()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        var disabled = Run("--only", "PD002", "--disable", "PD002", tree.Root);
        Assert.Equal((0, "ok\n", string.Empty), disabled);

        var disabledJson = Run("--only", "PD002", "--disable", "PD002", "--format", "json", tree.Root);
        Assert.Equal((0, "{\"projects\":2,\"findings\":[],\"problems\":0}\n", string.Empty), disabledJson);

        var repeated = Run("--only", "PD001", "--only", "PD001", tree.Root);
        Assert.Equal(1, repeated.Code);
        Assert.Equal(
            "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\n" +
            "lib/lib.csproj:4: PD001 Serilog 3.1.0-beta.2 differs from * in app/app.csproj\n" +
            "2 problems in 1 project\n",
            repeated.Out);
        Assert.Empty(repeated.Err);
    }

    [Fact]
    public void Cli_only_is_accepted_after_the_directory()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        Assert.Equal(
            (1, "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks\n1 problem in 1 project\n", string.Empty),
            Run(tree.Root, "--only", "PD004"));
    }

    [Fact]
    public void Cli_only_errors_keep_validation_order_and_single_line_output()
    {
        using var tree = new TempTree();
        var missingDirectory = Path.Combine(tree.Root, "missing");
        var cases = new (string[] Args, string Error)[]
        {
            (new[] { "--only" }, "pkgdrift: missing value for --only"),
            (new[] { "--only", "--disable", "PD001", tree.Root }, "pkgdrift: missing value for --only"),
            (new[] { "--only", "pd001", tree.Root }, "pkgdrift: unknown rule \"pd001\""),
            (new[] { "--only=PD001", tree.Root }, "pkgdrift: unknown option \"--only=PD001\""),
            (new[] { "--only", "PD009", "--disable", "PD008", tree.Root }, "pkgdrift: unknown rule \"PD008\""),
            (new[] { "--only", "PD009", missingDirectory }, "pkgdrift: unknown rule \"PD009\""),
            (new[] { "--only", "PD009", tree.Root, tree.Root }, "pkgdrift: exactly one directory required")
        };

        foreach (var test in cases)
        {
            var result = Run(test.Args);
            Assert.Equal(2, result.Code);
            Assert.Empty(result.Out);
            Assert.Equal(test.Error + "\n", result.Err);
        }
    }

    [Fact]
    public void Rules_create_applies_only_filter_and_keeps_default_rules()
    {
        var projects = new[]
        {
            new ProjectInfo(
                "app.csproj",
                1,
                new[] { new PackageReferenceInfo("app.csproj", 2, "Serilog", "1.*") },
                false)
        };

        Assert.Equal(
            new[] { "PD002" },
            Rules.Create(projects, only: new[] { "PD002" }).Findings.Select(finding => finding.Rule));
        Assert.Equal(
            new[] { "PD004", "PD002" },
            Rules.Create(projects).Findings.Select(finding => finding.Rule));
        Assert.Equal(
            new[] { "PD004", "PD002" },
            Rules.Create(projects, only: Array.Empty<string>()).Findings.Select(finding => finding.Rule));
    }

    [Fact]
    public void Readme_documents_the_only_option_and_an_only_command()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pkgdrift.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var readme = File.ReadAllText(Path.Combine(root!.FullName, "README.md"));
        Assert.Contains("--only RULE", readme);
        Assert.Contains(
            readme.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Where(line => line.StartsWith("pkgdrift --only", StringComparison.Ordinal)),
            line => line == "pkgdrift --only PD001 --only PD005 .");
    }

    static void AddExampleProjects(TempTree tree)
    {
        tree.Write(
            "app/app.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.1\" />\n" +
            "    <PackageReference Include=\"Serilog\" Version=\"*\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        tree.Write(
            "lib/lib.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"12.0.3\" />\n" +
            "    <PackageReference Include=\"Serilog\" Version=\"3.1.0-beta.2\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
    }

    static (int Code, string Out, string Err) Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        var code = Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    sealed class TempTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pkgdrift-only-tests-" + Guid.NewGuid().ToString("N"));

        public TempTree() => Directory.CreateDirectory(Root);

        public string Write(string relative, string text)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
