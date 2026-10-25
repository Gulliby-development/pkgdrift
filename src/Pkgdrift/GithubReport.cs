using System.Text;

public static class GithubReport
{
    public static string Render(Report report)
    {
        if (report.Problems == 0) return "ok\n";

        var output = new StringBuilder();
        foreach (var finding in report.Findings)
        {
            output.Append("::").Append(Level(finding.Rule))
                .Append(" file=").Append(EscapeProperty(finding.Path))
                .Append(",line=").Append(finding.Line)
                .Append(",title=").Append(finding.Rule)
                .Append("::").Append(EscapeData(finding.Message)).Append('\n');
        }

        var projectPaths = new HashSet<string>(report.Findings.Select(f => f.Path), StringComparer.Ordinal);
        output.Append(report.Problems).Append(" problem").Append(report.Problems == 1 ? "" : "s")
            .Append(" in ").Append(projectPaths.Count).Append(" project").Append(projectPaths.Count == 1 ? "" : "s")
            .Append('\n');
        return output.ToString();
    }

    public static string EscapeData(string text) =>
        text.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");

    public static string EscapeProperty(string text) =>
        EscapeData(text).Replace(":", "%3A").Replace(",", "%2C");

    static string Level(string rule) => rule is "PD002" or "PD005" ? "warning" : "error";
}
