using System.Text;

public static class MarkdownReport
{
    public static string Render(Report report)
    {
        if (report.Problems == 0) return "ok\n";

        var output = new StringBuilder();
        output.Append("| File | Line | Rule | Message |\n");
        output.Append("| --- | ---: | --- | --- |\n");
        foreach (var finding in report.Findings)
        {
            output.Append("| ").Append(Escape(finding.Path)).Append(" | ")
                .Append(finding.Line).Append(" | ").Append(finding.Rule).Append(" | ")
                .Append(Escape(finding.Message)).Append(" |\n");
        }

        var projectPaths = new HashSet<string>(report.Findings.Select(f => f.Path), StringComparer.Ordinal);
        output.Append('\n');
        output.Append(report.Problems).Append(" problem").Append(report.Problems == 1 ? "" : "s")
            .Append(" in ").Append(projectPaths.Count).Append(" project").Append(projectPaths.Count == 1 ? "" : "s")
            .Append('\n');
        return output.ToString();
    }

    public static string Escape(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\r' or '\n')
            {
                output.Append(' ');
            }
            else
            {
                if (character is '\\' or '|' or '*' or '_' or '`' or '<' or '>' or '[' or ']')
                    output.Append('\\');
                output.Append(character);
            }
        }

        return output.ToString();
    }
}
