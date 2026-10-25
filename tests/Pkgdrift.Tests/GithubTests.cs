using System.Text;

namespace Pkgdrift.Tests;

public sealed class GithubTests
{
    const string ExpectedGithub =
        "::warning file=app/app.csproj,line=7,title=PD002::floating version * of Serilog\n" +
        "::error file=lib/lib.csproj,line=1,title=PD004::no TargetFramework or TargetFrameworks\n" +
        "::error file=lib/lib.csproj,line=3,title=PD001::Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\n" +
        "::error file=lib/lib.csproj,line=4,title=PD001::Serilog 3.1.0-beta.2 differs from * in app/app.csproj\n" +
        "::warning file=lib/lib.csproj,line=4,title=PD005::prerelease version 3.1.0-beta.2 of Serilog\n" +
        "5 problems in 2 projects\n";

    [Fact]
    public void Github_cli_matches_worked_example_with_options_before_or_after_directory()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        Assert.Equal((1, ExpectedGithub, string.Empty), Run("--format", "github", tree.Root));
        Assert.Equal((1, ExpectedGithub, string.Empty), Run(tree.Root, "--format", "github"));
    }

    [Fact]
    public void Github_cli_handles_empty_results_filters_and_summaries()
    {
        using var tree = new TempTree();
        Assert.Equal((0, "ok\n", string.Empty), Run("--format", "github", tree.Root));

        AddExampleProjects(tree);
        Assert.Equal((0, "ok\n", string.Empty), Run("--format", "github", "--only", "PD003", tree.Root));
        Assert.Equal(
            (1,
                "::error file=lib/lib.csproj,line=1,title=PD004::no TargetFramework or TargetFrameworks\n" +
                "1 problem in 1 project\n",
                string.Empty),
            Run("--format", "github", "--only", "PD004", tree.Root));
        Assert.Equal(
            (1,
                "::warning file=app/app.csproj,line=7,title=PD002::floating version * of Serilog\n" +
                "1 problem in 1 project\n",
                string.Empty),
            Run("--format", "github", "--disable", "PD001", "--disable", "PD004", "--disable", "PD005", tree.Root));
    }

    [Fact]
    public void Github_cli_escapes_path_and_message_and_writes_lf()
    {
        using var tree = new TempTree();
        tree.Write(
            "a,b/app.csproj",
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Pct%\" Version=\"1.*\" /></ItemGroup></Project>");

        var result = RunWithNewLine("\r\n", "--format", "github", tree.Root);
        Assert.Equal(1, result.Code);
        Assert.Equal(
            "::warning file=a%2Cb/app.csproj,line=1,title=PD002::floating version 1.* of Pct%25\n" +
            "1 problem in 1 project\n",
            result.Out);
        Assert.Empty(result.Err);
        Assert.DoesNotContain('\r', result.Out);
    }

    [Fact]
    public void Github_escape_data_replaces_percent_cr_and_lf_in_order()
    {
        Assert.Equal("50%25 a%0Db%0A c%0D%0Ad: e, f", GithubReport.EscapeData("50% a\rb\n c\r\nd: e, f"));
    }

    [Fact]
    public void Github_escape_property_escapes_delimiters_and_does_not_reescape_percent_sequences()
    {
        Assert.Equal("a%2Cb%3Ac%25d%0Ae", GithubReport.EscapeProperty("a,b:c%d\ne"));
        Assert.Equal("%2525", GithubReport.EscapeProperty("%25"));
    }

    [Fact]
    public void Github_render_uses_the_severity_for_each_rule()
    {
        var report = new Report(1, new[]
        {
            new Finding("p.csproj", 1, "PD001", "m1"),
            new Finding("p.csproj", 2, "PD002", "m2"),
            new Finding("p.csproj", 3, "PD003", "m3"),
            new Finding("p.csproj", 4, "PD004", "m4"),
            new Finding("p.csproj", 5, "PD005", "m5")
        });

        Assert.Equal(
            "::error file=p.csproj,line=1,title=PD001::m1\n" +
            "::warning file=p.csproj,line=2,title=PD002::m2\n" +
            "::error file=p.csproj,line=3,title=PD003::m3\n" +
            "::error file=p.csproj,line=4,title=PD004::m4\n" +
            "::warning file=p.csproj,line=5,title=PD005::m5\n" +
            "5 problems in 1 project\n",
            GithubReport.Render(report));
    }

    [Fact]
    public void Github_format_errors_are_single_line_and_leave_stdout_empty()
    {
        using var tree = new TempTree();

        Assert.Equal(
            (2, string.Empty, "pkgdrift: --format must be text, json, markdown or github\n"),
            Run("--format", "GitHub", tree.Root));
        Assert.Equal(
            (2, string.Empty, "pkgdrift: unknown rule \"PD009\"\n"),
            Run("--format", "github", "--only", "PD009", tree.Root));
        Assert.Equal(
            (2, string.Empty, "pkgdrift: directory required\n"),
            Run("--format", "github"));
    }

    [Fact]
    public void Readme_documents_github_usage_command_and_worked_example()
    {
        var readmePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "README.md"));
        var readme = File.ReadAllText(readmePath).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("pkgdrift [--format text|json|markdown|github]", readme);
        Assert.Contains("## GitHub annotations\n", readme);
        Assert.Contains("pkgdrift --format github .", readme);
        Assert.Contains(
            new string((char)96, 3) + "text\n" + ExpectedGithub.TrimEnd('\n') + "\n" + new string((char)96, 3),
            readme);
        Assert.True(
            readme.IndexOf("## GitHub annotations\n", StringComparison.Ordinal) <
            readme.IndexOf("## Exit codes\n", StringComparison.Ordinal));
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

    static (int Code, string Out, string Err) Run(params string[] args) => RunWithNewLine("\n", args);

    static (int Code, string Out, string Err) RunWithNewLine(string newLine, params string[] args)
    {
        using var stdout = new StringWriter { NewLine = newLine };
        using var stderr = new StringWriter { NewLine = newLine };
        var code = Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    sealed class TempTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pkgdrift-github-tests-" + Guid.NewGuid().ToString("N"));

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
