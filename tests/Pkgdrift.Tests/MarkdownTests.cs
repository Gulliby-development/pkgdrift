using System.Text;

namespace Pkgdrift.Tests;

public sealed class MarkdownTests
{
    const string ExpectedMarkdown =
        "| File | Line | Rule | Message |\n" +
        "| --- | ---: | --- | --- |\n" +
        "| app/app.csproj | 7 | PD002 | floating version \\* of Serilog |\n" +
        "| lib/lib.csproj | 1 | PD004 | no TargetFramework or TargetFrameworks |\n" +
        "| lib/lib.csproj | 3 | PD001 | Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj |\n" +
        "| lib/lib.csproj | 4 | PD001 | Serilog 3.1.0-beta.2 differs from \\* in app/app.csproj |\n" +
        "| lib/lib.csproj | 4 | PD005 | prerelease version 3.1.0-beta.2 of Serilog |\n" +
        "\n" +
        "5 problems in 2 projects\n";

    const string ExpectedText =
        "app/app.csproj:7: PD002 floating version * of Serilog\n" +
        "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks\n" +
        "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\n" +
        "lib/lib.csproj:4: PD001 Serilog 3.1.0-beta.2 differs from * in app/app.csproj\n" +
        "lib/lib.csproj:4: PD005 prerelease version 3.1.0-beta.2 of Serilog\n" +
        "5 problems in 2 projects\n";

    const string ExpectedJson =
        "{\"projects\":2,\"findings\":[{\"path\":\"app/app.csproj\",\"line\":7,\"rule\":\"PD002\",\"message\":\"floating version * of Serilog\"},{\"path\":\"lib/lib.csproj\",\"line\":1,\"rule\":\"PD004\",\"message\":\"no TargetFramework or TargetFrameworks\"},{\"path\":\"lib/lib.csproj\",\"line\":3,\"rule\":\"PD001\",\"message\":\"Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\"},{\"path\":\"lib/lib.csproj\",\"line\":4,\"rule\":\"PD001\",\"message\":\"Serilog 3.1.0-beta.2 differs from * in app/app.csproj\"},{\"path\":\"lib/lib.csproj\",\"line\":4,\"rule\":\"PD005\",\"message\":\"prerelease version 3.1.0-beta.2 of Serilog\"}],\"problems\":5}\n";

    [Fact]
    public void Markdown_cli_matches_worked_example_with_options_before_or_after_directory()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        var formatBeforeDirectory = Run("--format", "markdown", tree.Root);
        Assert.Equal((1, ExpectedMarkdown, string.Empty), formatBeforeDirectory);

        var formatAfterDirectory = Run(tree.Root, "--format", "markdown");
        Assert.Equal((1, ExpectedMarkdown, string.Empty), formatAfterDirectory);

        tree.Write("samples/ignored.csproj", "<Project />");
        Assert.Equal(
            (1, ExpectedMarkdown, string.Empty),
            Run("--format", "markdown", "--ignore", "samples", tree.Root));
    }

    [Fact]
    public void Markdown_cli_handles_empty_results_filters_and_summaries()
    {
        using var tree = new TempTree();
        Assert.Equal((0, "ok\n", string.Empty), Run("--format", "markdown", tree.Root));

        AddExampleProjects(tree);
        Assert.Equal((0, "ok\n", string.Empty), Run("--format", "markdown", "--only", "PD003", tree.Root));

        Assert.Equal(
            (1,
                "| File | Line | Rule | Message |\n" +
                "| --- | ---: | --- | --- |\n" +
                "| lib/lib.csproj | 1 | PD004 | no TargetFramework or TargetFrameworks |\n" +
                "\n" +
                "1 problem in 1 project\n",
                string.Empty),
            Run("--format", "markdown", tree.Root, "--only", "PD004"));

        Assert.Equal(
            (1,
                "| File | Line | Rule | Message |\n" +
                "| --- | ---: | --- | --- |\n" +
                "| app/app.csproj | 7 | PD002 | floating version \\* of Serilog |\n" +
                "| lib/lib.csproj | 3 | PD001 | Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj |\n" +
                "| lib/lib.csproj | 4 | PD001 | Serilog 3.1.0-beta.2 differs from \\* in app/app.csproj |\n" +
                "\n" +
                "3 problems in 2 projects\n",
                string.Empty),
            Run("--format", "markdown", "--disable", "PD004", "--disable", "PD005", tree.Root));
    }

    [Fact]
    public void Markdown_cli_escapes_file_and_message_cells_and_writes_lf()
    {
        using var tree = new TempTree();
        tree.Write(
            "my_app/my_app.csproj",
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"A_b|c\" Version=\"1.*\" /></ItemGroup></Project>");

        var result = RunWithNewLine("\r\n", "--format", "markdown", tree.Root);
        Assert.Equal(1, result.Code);
        Assert.Equal(
            "| File | Line | Rule | Message |\n" +
            "| --- | ---: | --- | --- |\n" +
            "| my\\_app/my\\_app.csproj | 1 | PD002 | floating version 1.\\* of A\\_b\\|c |\n" +
            "\n" +
            "1 problem in 1 project\n",
            result.Out);
        Assert.Empty(result.Err);
        Assert.DoesNotContain('\r', result.Out);
    }

    [Fact]
    public void Markdown_escape_prefixes_each_special_character_and_replaces_cr_and_lf()
    {
        Assert.Equal(
            "a\\\\b\\|c\\*d\\_e\\" + (char)96 + "f\\<g\\>\\[h\\]",
            MarkdownReport.Escape("a\\b|c*d_e" + (char)96 + "f<g>[h]"));
        Assert.Equal("one two  three", MarkdownReport.Escape("one\ntwo\r\nthree"));
    }

    [Fact]
    public void Markdown_format_keeps_text_and_json_outputs_unchanged_and_later_format_wins()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        Assert.Equal((1, ExpectedText, string.Empty), Run(tree.Root));
        Assert.Equal((1, ExpectedJson, string.Empty), Run("--format", "json", tree.Root));
        Assert.Equal((1, ExpectedMarkdown, string.Empty), Run("--format", "text", "--format", "markdown", tree.Root));
        Assert.Equal((1, ExpectedText, string.Empty), Run("--format", "markdown", "--format", "text", tree.Root));
    }

    [Fact]
    public void Markdown_format_errors_are_single_line_and_leave_stdout_empty()
    {
        using var tree = new TempTree();
        var cases = new (string[] Args, string Error)[]
        {
            (new[] { "--format", "xml", tree.Root }, "pkgdrift: --format must be text, json or markdown"),
            (new[] { "--format", "Markdown", tree.Root }, "pkgdrift: --format must be text, json or markdown"),
            (new[] { "--format=markdown", tree.Root }, "pkgdrift: unknown option \"--format=markdown\""),
            (new[] { "--format" }, "pkgdrift: missing value for --format"),
            (new[] { "--format", "markdown", "--only", "PD009", tree.Root }, "pkgdrift: unknown rule \"PD009\"")
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
    public void Readme_documents_markdown_usage_command_and_worked_example()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pkgdrift.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var readme = File.ReadAllText(Path.Combine(root!.FullName, "README.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("pkgdrift [--format text|json|markdown]", readme);
        Assert.Contains("## Markdown output\n", readme);
        Assert.Contains("pkgdrift --format markdown .", readme);
        Assert.Contains(
            new string((char)96, 3) + "markdown\n" + ExpectedMarkdown.TrimEnd('\n') + "\n" + new string((char)96, 3),
            readme);
        Assert.Contains(
            "| 2 | Invalid arguments, missing or invalid scan directory, unknown " + (char)96 + "--disable" + (char)96 +
            " or " + (char)96 + "--only" + (char)96 + " rule, invalid " + (char)96 + "--ignore" + (char)96 +
            " value, or malformed project XML. |",
            readme);
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
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pkgdrift-markdown-tests-" + Guid.NewGuid().ToString("N"));

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
