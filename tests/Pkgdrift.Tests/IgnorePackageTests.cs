using System.Text;
using System.Text.Json;

namespace Pkgdrift.Tests;

public sealed class IgnorePackageTests
{
    [Fact]
    public void Ignore_package_documentation_matches_the_requested_usage_and_section()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pkgdrift.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var readme = File.ReadAllText(Path.Combine(root!.FullName, "README.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("pkgdrift [--format text|json|markdown] [--disable RULE]... [--only RULE]... [--ignore DIR]... [--ignore-package NAME]... DIR", readme);
        Assert.Contains("pkgdrift --ignore-package Serilog .", readme);
        var expectedSection = string.Join("\n", new[]
        {
            "## Ignoring packages",
            "",
            "`--ignore-package NAME` removes every reference to the package NAME (compared without regard to case, like package names elsewhere) before any rule runs, so it produces no PD001, PD002, PD003 or PD005 finding and does not serve as a version baseline for the other projects. The option may be repeated. Projects, the project count and PD004 are not affected. A missing value (the last argument, or a next argument starting with `--`) or a value that is empty or only white space is an argument error with exit code 2.",
            "",
            "For the example projects in the Example section below, run:",
            "",
            "```sh",
            "pkgdrift --ignore-package serilog .",
            "```",
            "",
            "The output is:",
            "",
            "```text",
            "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks",
            "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj",
            "2 problems in 1 project",
            "```"
        });
        Assert.Contains(expectedSection, readme);
    }

    [Fact]
    public void Ignore_package_removes_the_named_package_from_all_rules()
    {
        using var tree = new TempTree();
        WriteExampleProjects(tree);

        Assert.Equal(
            (1, "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks\n" +
                "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\n" +
                "2 problems in 1 project\n", string.Empty),
            Run("--ignore-package", "serilog", tree.Root));
    }

    [Fact]
    public void Ignore_package_is_repeatable_and_case_insensitive()
    {
        using var tree = new TempTree();
        WriteExampleProjects(tree);

        Assert.Equal(
            (1, "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks\n1 problem in 1 project\n", string.Empty),
            Run("--ignore-package", "Serilog", "--ignore-package", "newtonsoft.json", tree.Root));
    }

    [Fact]
    public void Ignore_package_with_no_match_does_not_change_findings()
    {
        using var tree = new TempTree();
        WriteExampleProjects(tree);

        var ordinary = Run(tree.Root);
        var ignored = Run("--ignore-package", "Nope", tree.Root);

        Assert.Equal(1, ordinary.Code);
        Assert.Equal(ordinary, ignored);
    }

    [Fact]
    public void Ignore_package_preserves_the_json_report_format()
    {
        using var tree = new TempTree();
        WriteExampleProjects(tree);

        Assert.Equal(
            (1, "{\"projects\":2,\"findings\":[{\"path\":\"lib/lib.csproj\",\"line\":1,\"rule\":\"PD004\",\"message\":\"no TargetFramework or TargetFrameworks\"},{\"path\":\"lib/lib.csproj\",\"line\":3,\"rule\":\"PD001\",\"message\":\"Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\"}],\"problems\":2}\n", string.Empty),
            Run("--ignore-package", "serilog", "--format", "json", tree.Root));
    }

    [Fact]
    public void Ignore_package_combines_with_only()
    {
        using var tree = new TempTree();
        WriteExampleProjects(tree);

        Assert.Equal(
            (1, "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\n1 problem in 1 project\n", string.Empty),
            Run("--ignore-package", "Serilog", "--only", "PD001", tree.Root));
    }

    [Fact]
    public void Ignore_package_removes_duplicates_without_hiding_other_packages()
    {
        using var tree = new TempTree();
        tree.Write("a/a.csproj", ProjectWithReferences(
            "<PackageReference Include=\"Dup\" Version=\"1.0.0\" />",
            "<PackageReference Include=\"dup\" Version=\"1.0.0\" />",
            "<PackageReference Include=\"Other\" Version=\"2.0.0\" />"));
        tree.Write("b/b.csproj", ProjectWithReferences(
            "<PackageReference Include=\"Dup\" Version=\"9.0.0\" />",
            "<PackageReference Include=\"Other\" Version=\"2.0.0\" />"));

        Assert.Equal(
            (1, "a/a.csproj:7: PD003 duplicate reference to dup\n" +
                "b/b.csproj:6: PD001 Dup 9.0.0 differs from 1.0.0 in a/a.csproj\n" +
                "2 problems in 2 projects\n", string.Empty),
            Run(tree.Root));
        Assert.Equal((0, "ok\n", string.Empty), Run("--ignore-package", "DUP", tree.Root));
    }

    [Fact]
    public void Ignore_package_keeps_projects_and_pd004_findings()
    {
        using var tree = new TempTree();
        tree.Write("a/a.csproj", ProjectWithoutFramework("<PackageReference Include=\"Only\" Version=\"1.0.0\" />"));
        tree.Write("b/b.csproj", ProjectWithoutFramework("<PackageReference Include=\"Only\" Version=\"2.0.0\" />"));

        Assert.Equal(
            (1, "{\"projects\":2,\"findings\":[{\"path\":\"a/a.csproj\",\"line\":1,\"rule\":\"PD004\",\"message\":\"no TargetFramework or TargetFrameworks\"},{\"path\":\"b/b.csproj\",\"line\":1,\"rule\":\"PD004\",\"message\":\"no TargetFramework or TargetFrameworks\"}],\"problems\":2}\n", string.Empty),
            Run("--ignore-package", "only", "--format", "json", tree.Root));
    }

    [Fact]
    public void Ignore_package_combines_with_format_disable_only_and_directory_ignore()
    {
        using var tree = new TempTree();
        WriteExampleProjects(tree);
        tree.Write("samples/extra.csproj", "<Project />");

        Assert.Equal(
            (1, "{\"projects\":2,\"findings\":[{\"path\":\"lib/lib.csproj\",\"line\":3,\"rule\":\"PD001\",\"message\":\"Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\"}],\"problems\":1}\n", string.Empty),
            Run("--format", "json", "--disable", "PD004", "--only", "PD001", "--ignore", "samples", "--ignore-package", "Serilog", tree.Root));
    }

    [Fact]
    public void Ignore_package_is_accepted_after_the_directory()
    {
        using var tree = new TempTree();
        WriteExampleProjects(tree);

        Assert.Equal(
            (1, "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks\n" +
                "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\n" +
                "2 problems in 1 project\n", string.Empty),
            Run(tree.Root, "--ignore-package", "Serilog"));
    }

    [Fact]
    public void Ignore_package_requires_a_value_at_the_end()
    {
        AssertError(Run("--ignore-package"), "pkgdrift: missing value for --ignore-package");
    }

    [Fact]
    public void Ignore_package_requires_a_value_before_another_option()
    {
        AssertError(Run("--ignore-package", "--format", "json", "DIR"), "pkgdrift: missing value for --ignore-package");
    }

    [Fact]
    public void Ignore_package_reports_invalid_values_and_unknown_spellings_in_argument_order()
    {
        using var tree = new TempTree();

        AssertError(Run("--ignore-package", "", tree.Root), "pkgdrift: invalid --ignore-package value \"\"");
        AssertError(Run("--ignore-package", " \t ", tree.Root), "pkgdrift: invalid --ignore-package value \" \t \"");
        AssertError(Run("--ignore-packages", tree.Root), "pkgdrift: unknown option \"--ignore-packages\"");
        AssertError(Run("--ignore-package", " ", "--format", "unknown"), "pkgdrift: invalid --ignore-package value \" \"");
    }

    static string ProjectWithoutFramework(string packageReference) =>
        $"<Project>\n  <ItemGroup>\n    {packageReference}\n  </ItemGroup>\n</Project>";

    static string ProjectWithReferences(params string[] packageReferences) =>
        "<Project>\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "  </PropertyGroup>\n" +
        "  <ItemGroup>\n" +
        string.Join("\n", packageReferences.Select(reference => "    " + reference)) + "\n" +
        "  </ItemGroup>\n" +
        "</Project>";

    static void WriteExampleProjects(TempTree tree)
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

    static void AssertError((int Code, string Out, string Err) result, string expected)
    {
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Out);
        Assert.Equal(expected + "\n", result.Err);
    }

    sealed class TempTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pkgdrift-ignore-package-tests-" + Guid.NewGuid().ToString("N"));

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
