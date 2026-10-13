using System.Text;

namespace Pkgdrift.Tests;

public sealed class PkgdriftTests
{
    [Fact]
    public void Scanner_reads_namespaced_projects_and_child_versions_with_lines()
    {
        using var tree = new TempTree();
        tree.Write("nested/one.CSPROJ", "<p:Project xmlns:p=\"urn:test\"><p:ItemGroup><p:PackageReference Include=\" Alpha \"><p:Version> 1.2 </p:Version></p:PackageReference></p:ItemGroup></p:Project>");
        var project = Assert.Single(new ProjectScanner().Scan(tree.Root));
        var reference = Assert.Single(project.References);
        Assert.Equal("nested/one.CSPROJ", reference.Path);
        Assert.Equal(" Alpha ", reference.Package);
        Assert.Equal("1.2", reference.Version);
        tree.Write("empty.csproj", "<Project><PackageReference Include=\"P\" Version=\" \" ><Version>1.*</Version></PackageReference></Project>");
        Assert.Null(new ProjectScanner().Scan(tree.Root).Single(p => p.Path == "empty.csproj").References[0].Version);
        Assert.Equal(1, reference.Line);
    }

    [Fact]
    public void Scanner_skips_excluded_directories_and_props_is_found_with_same_traversal()
    {
        using var tree = new TempTree();
        foreach (var name in new[] { "bin", "OBJ", "node_modules", ".GIT" })
            tree.Write($"{name}/ignored.csproj", Project("<PackageReference Include=\"X\" Version=\"1\" />"));
        tree.Write("src/ok.csproj", Project("<PackageReference Include=\"X\" Version=\"1\" />"));
        tree.Write("obj/Directory.Build.props", "<Project />");
        var scanner = new ProjectScanner();
        Assert.Equal("src/ok.csproj", Assert.Single(scanner.Scan(tree.Root)).Path);
        Assert.False(scanner.HasDirectoryBuildProps(tree.Root));
        tree.Write("src/Directory.Build.props", "<Project />");
        Assert.True(scanner.HasDirectoryBuildProps(tree.Root));
    }

    [Fact]
    public void Scanner_does_not_follow_directory_symbolic_links()
    {
        using var tree = new TempTree();
        using var target = new TempTree();
        target.Write("linked.csproj", "<Project />");
        var link = Path.Combine(tree.Root, "linked-dir");
        try
        {
            Directory.CreateSymbolicLink(link, target.Root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Empty(new ProjectScanner().Scan(tree.Root));
    }

    [Fact]
    public void Rules_cover_all_rules_case_insensitivity_duplicates_empty_versions_disable_and_order()
    {
        var projects = new[]
        {
            new ProjectInfo("a.csproj", 1, new[] { new PackageReferenceInfo("a.csproj", 2, "Foo", "1.0"), new PackageReferenceInfo("a.csproj", 3, "foo", "2.0"), new PackageReferenceInfo("a.csproj", 4, "Empty", null) }, true),
            new ProjectInfo("b.csproj", 1, new[] { new PackageReferenceInfo("b.csproj", 2, "FOO", "2.0-beta"), new PackageReferenceInfo("b.csproj", 3, "Float", "1.*"), new PackageReferenceInfo("b.csproj", 4, "Empty", "") }, false)
        };
        var findings = Rules.Create(projects).Findings;
        Assert.Equal(new[] { "PD001", "PD003", "PD004", "PD001", "PD005", "PD002" }, findings.Select(f => f.Rule));
        Assert.Equal("foo 2.0 differs from 1.0 in a.csproj", findings[0].Message);
        Assert.Equal("duplicate reference to foo", findings[1].Message);
        Assert.Equal("FOO 2.0-beta differs from 1.0 in a.csproj", findings[3].Message);
        Assert.DoesNotContain(Rules.Create(projects, disabled: new[] { "PD001" }).Findings, f => f.Rule == "PD001");
        Assert.Contains(Rules.Create(projects, disabled: new[] { "pd001" }).Findings, f => f.Rule == "PD001");
        Assert.DoesNotContain(Rules.Create(projects, hasDirectoryBuildProps: true).Findings, f => f.Rule == "PD004");
    }

    [Fact]
    public void Cli_matches_worked_example_in_text_and_json_and_json_does_not_escape_plus()
    {
        using var tree = new TempTree();
        tree.Write("app/app.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.1\" />\n    <PackageReference Include=\"Serilog\" Version=\"*\" />\n  </ItemGroup>\n</Project>");
        tree.Write("lib/lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"Newtonsoft.Json\" Version=\"12.0.3\" />\n    <PackageReference Include=\"Serilog\" Version=\"3.1.0-beta.2\" />\n  </ItemGroup>\n</Project>");
        var text = Run(tree.Root);
        Assert.Equal(1, text.Code);
        Assert.Equal("app/app.csproj:7: PD002 floating version * of Serilog\nlib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks\nlib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\nlib/lib.csproj:4: PD001 Serilog 3.1.0-beta.2 differs from * in app/app.csproj\nlib/lib.csproj:4: PD005 prerelease version 3.1.0-beta.2 of Serilog\n5 problems in 2 projects\n", text.Out);
        var json = Run(tree.Root, "--format", "json");
        Assert.Equal(1, json.Code);
        Assert.Equal("{\"projects\":2,\"findings\":[{\"path\":\"app/app.csproj\",\"line\":7,\"rule\":\"PD002\",\"message\":\"floating version * of Serilog\"},{\"path\":\"lib/lib.csproj\",\"line\":1,\"rule\":\"PD004\",\"message\":\"no TargetFramework or TargetFrameworks\"},{\"path\":\"lib/lib.csproj\",\"line\":3,\"rule\":\"PD001\",\"message\":\"Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj\"},{\"path\":\"lib/lib.csproj\",\"line\":4,\"rule\":\"PD001\",\"message\":\"Serilog 3.1.0-beta.2 differs from * in app/app.csproj\"},{\"path\":\"lib/lib.csproj\",\"line\":4,\"rule\":\"PD005\",\"message\":\"prerelease version 3.1.0-beta.2 of Serilog\"}],\"problems\":5}\n", json.Out);
        Assert.DoesNotContain("\r", json.Out);
        tree.Write("plus.csproj", "<Project><PackageReference Include=\"P\" Version=\"1.2+meta\" /><PackageReference Include=\"P\" Version=\"2.0\" /></Project>");
        Assert.Contains("1.2+meta", Run(tree.Root, "--format", "json").Out);
    }

    [Fact]
    public void Cli_errors_are_single_line_and_leave_stdout_empty()
    {
        using var tree = new TempTree();
        var cases = new (string[] Args, string Error)[]
        {
            (Array.Empty<string>(), "pkgdrift: directory required"),
            (new[] { tree.Root, tree.Root }, "pkgdrift: exactly one directory required"),
            (new[] { "--wat", tree.Root }, "pkgdrift: unknown option \"--wat\""),
            (new[] { "--format" }, "pkgdrift: missing value for --format"),
            (new[] { "--format", "xml", tree.Root }, "pkgdrift: --format must be text, json or markdown"),
            (new[] { "--disable" }, "pkgdrift: missing value for --disable"),
            (new[] { "--disable", "pd001", tree.Root }, "pkgdrift: unknown rule \"pd001\""),
            (new[] { "--only" }, "pkgdrift: missing value for --only"),
            (new[] { "--only", "pd001", tree.Root }, "pkgdrift: unknown rule \"pd001\""),
            (new[] { Path.Combine(tree.Root, "missing") }, $"pkgdrift: {Path.Combine(tree.Root, "missing")}: directory not found"),
            (new[] { tree.Write("not-directory", "file") }, $"pkgdrift: {tree.Write("not-directory", "file")}: directory not found")
        };
        foreach (var test in cases)
        {
            var result = Run(test.Args);
            Assert.Equal(2, result.Code);
            Assert.Equal(string.Empty, result.Out);
            Assert.Equal(test.Error + "\n", result.Err);
        }
        tree.Write("bin/ignored.csproj", "<Project>");
        tree.Write("z.csproj", "<Project>");
        tree.Write("bad.csproj", "<Project />");
        var malformed = Run(tree.Root);
        Assert.Equal(2, malformed.Code);
        Assert.Empty(malformed.Out);
        Assert.Equal("pkgdrift: z.csproj: invalid XML\n", malformed.Err);
    }

    [Fact]
    public void Empty_scan_succeeds_and_readme_contains_worked_output()
    {
        using var tree = new TempTree();
        Assert.Equal((0, "ok\n", string.Empty), Run(tree.Root));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pkgdrift.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var readme = File.ReadAllText(Path.Combine(root!.FullName, "README.md"));
        foreach (var line in new[] { "app/app.csproj:7: PD002 floating version * of Serilog", "lib/lib.csproj:1: PD004 no TargetFramework or TargetFrameworks", "lib/lib.csproj:3: PD001 Newtonsoft.Json 12.0.3 differs from 13.0.1 in app/app.csproj", "lib/lib.csproj:4: PD001 Serilog 3.1.0-beta.2 differs from * in app/app.csproj", "lib/lib.csproj:4: PD005 prerelease version 3.1.0-beta.2 of Serilog", "5 problems in 2 projects" })
            Assert.Contains(line, readme);
    }

    static string Project(string body) => $"<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{body}</ItemGroup></Project>";
    static (int Code, string Out, string Err) Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" }; using var stderr = new StringWriter { NewLine = "\n" };
        var code = Program.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    sealed class TempTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pkgdrift-tests-" + Guid.NewGuid().ToString("N"));
        public TempTree() => Directory.CreateDirectory(Root);
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
