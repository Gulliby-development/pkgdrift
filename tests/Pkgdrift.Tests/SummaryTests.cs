using System.Text;

namespace Pkgdrift.Tests;

public sealed class SummaryTests
{
    [Fact]
    public void Summary_prints_only_the_summary_line_and_keeps_the_exit_code()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        var result = Run("--summary", tree.Root);
        Assert.Equal((1, "5 problems in 2 projects\n", string.Empty), result);

        var single = Run("--summary", "--only", "PD004", tree.Root);
        Assert.Equal((1, "1 problem in 1 project\n", string.Empty), single);
    }

    [Fact]
    public void Summary_of_a_clean_tree_is_ok()
    {
        using var tree = new TempTree();
        tree.Write("app/app.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>");

        Assert.Equal((0, "ok\n", string.Empty), Run("--summary", tree.Root));
        Assert.Equal((0, "ok\n", string.Empty), Run(tree.Root, "--summary", "--format", "text"));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    [InlineData("github")]
    public void Summary_with_another_format_is_an_argument_error(string format)
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        var result = Run("--summary", "--format", format, tree.Root);
        Assert.Equal((2, string.Empty, "pkgdrift: --summary works only with --format text\n"), result);
    }

    [Fact]
    public void Summary_error_comes_after_the_directory_count_and_before_the_rule_checks()
    {
        using var tree = new TempTree();
        AddExampleProjects(tree);

        Assert.Equal((2, string.Empty, "pkgdrift: directory required\n"), Run("--summary", "--format", "json"));
        Assert.Equal((2, string.Empty, "pkgdrift: --summary works only with --format text\n"), Run("--summary", "--format", "json", "--only", "PD999", tree.Root));
        Assert.Equal((2, string.Empty, "pkgdrift: unknown rule \"PD999\"\n"), Run("--summary", "--only", "PD999", tree.Root));
    }

    [Fact]
    public void Readme_documents_the_summary_option()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pkgdrift.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var readme = File.ReadAllText(Path.Combine(root!.FullName, "README.md"));
        Assert.Contains("--summary", readme);
        Assert.Contains(
            readme.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Where(line => line.StartsWith("pkgdrift --summary", StringComparison.Ordinal)),
            line => line == "pkgdrift --summary .");
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
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pkgdrift-summary-tests-" + Guid.NewGuid().ToString("N"));

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
