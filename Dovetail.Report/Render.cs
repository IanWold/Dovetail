using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Dovetail;

namespace Dovetail.Report;

internal static class Render
{
    private static readonly Regex UnsafeFileNameCharacters = new(@"[^A-Za-z0-9._-]", RegexOptions.Compiled);

    private const string BrandSvg =
        """
                <svg height="32" viewBox="2 16 96 68" role="img" aria-label="Dovetail">
                  <defs>
                    <linearGradient id="dovetailLogoGradient" x1="0%" y1="0%" x2="100%" y2="0%" gradientUnits="userSpaceOnUse" gradientTransform="rotate(45)">
                      <stop offset="0%" stop-color="#FF674D" />
                      <stop offset="100%" stop-color="#8A4FA8" />
                    </linearGradient>
                  </defs>
                  <path d="M12,50 L28,50 L20,26 L48,26 L40,50 L60,50 L52,74 L80,74 L72,50 L88,50"
                        fill="none" stroke="url(#dovetailLogoGradient)" stroke-width="7"
                        stroke-linecap="round" stroke-linejoin="round" />
                  <circle cx="12" cy="50" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="28" cy="50" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="20" cy="26" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="48" cy="26" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="40" cy="50" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="60" cy="50" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="52" cy="74" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="80" cy="74" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="72" cy="50" r="6" fill="url(#dovetailLogoGradient)" />
                  <circle cx="88" cy="50" r="6" fill="url(#dovetailLogoGradient)" />
                </svg>
        """;

    private const string PageStyle =
        """
            <style>
              .mermaid {
                background-color: transparent;

                :is(p, dl, ol, ul, table) {
                  color: inherit;
                }
              }

              table, hr {
                margin: 0;

                th, td {
                  background-color: transparent;
                }
              }
            </style>
        """;

    private const string DropdownCloseScript =
        """
            <script>
              document.querySelector("nav").addEventListener("click", (e) => {
                if (e.target.tagName === "A") {
                  e.target.closest("details")?.removeAttribute("open");
                }
              });
            </script>
        """;

    internal static string GetFullyQualifiedName(TypeDeclarationModel type)
    {
        var parts = new List<string>();

        if (type.Namespace.Length > 0)
        {
            parts.Add(type.Namespace);
        }

        foreach (var containingType in type.ContainingTypes)
        {
            parts.Add(containingType.Name);
        }

        parts.Add(type.Name);

        return string.Join(".", parts) + type.TypeParameterList;
    }

    internal static string GetPageFileName(TypeDeclarationModel type) =>
        UnsafeFileNameCharacters.Replace(GetFullyQualifiedName(type), "_") + ".html";

    internal static string RenderIndexPage(
        string projectName,
        string sourceLabel,
        string sourceValue,
        IReadOnlyList<PipelineGraphModel> graphs,
        Version? dovetailVersion,
        Version toolVersion,
        DateTimeOffset generatedAt
    )
    {
        var pipelineLinks = graphs
            .Select(static g => (Name: GetFullyQualifiedName(g.ContainingType), FileName: GetPageFileName(g.ContainingType)))
            .OrderBy(static p => p.Name, StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();

        AppendHead(builder, $"{projectName} | Dovetail Report");
        AppendNavOpen(builder, projectName, "Pipelines", pipelineLinks);

        builder.AppendLine("    <main class=\"container\">");
        builder.AppendLine("      <div class=\"grid\">");
        builder.AppendLine("        <div>");
        builder.AppendLine("          <table>");
        builder.AppendLine("            <thead><tr><th>Pipelines</th></tr></thead>");
        builder.AppendLine("            <tbody>");

        foreach (var (name, fileName) in pipelineLinks)
        {
            builder.AppendLine($"              <tr><td><a href=\"{fileName}\">{Html(name)}</a></td></tr>");
        }

        builder.AppendLine( "            </tbody>");
        builder.AppendLine( "          </table>");
        builder.AppendLine( "        </div>");
        builder.AppendLine( "        <div>");
        builder.AppendLine( "          <article>");
        builder.AppendLine( "            <hr/>");
        builder.AppendLine( "            <table>");
        builder.AppendLine( "              <tbody>");
        builder.AppendLine($"                <tr><th scope=\"row\">{Html(sourceLabel)}</th><td>{Html(sourceValue)}</td></tr>");
        builder.AppendLine($"                <tr><th scope=\"row\">Pipelines discovered</th><td>{graphs.Count}</td></tr>");
        builder.AppendLine($"                <tr><th scope=\"row\">Dovetail version</th><td>{Html(dovetailVersion?.ToString() ?? "unknown")}</td></tr>");
        builder.AppendLine($"                <tr><th scope=\"row\">Dovetail.Report version</th><td>{Html(toolVersion.ToString())}</td></tr>");
        builder.AppendLine($"                <tr><th scope=\"row\">Generated</th><td>{generatedAt:yyyy-MM-dd HH:mm} UTC</td></tr>");
        builder.AppendLine( "              </tbody>");
        builder.AppendLine( "            </table>");
        builder.AppendLine( "          </article>");
        builder.AppendLine( "        </div>");
        builder.AppendLine( "      </div>");
        builder.AppendLine( "    </main>");

        AppendBodyClose(builder, includeMermaid: false);

        return builder.ToString();
    }

    internal static string RenderPipelinePage(string projectName, PipelineGraphModel graph, IReadOnlyList<(string Name, string FileName)> allPipelineLinks)
    {
        var shortName = graph.ContainingType.Name;
        var resultTypeName = PipelineSourceGenerator.SimplifyTypeNameForDiagram(graph.PipelineResultTypeName);
        var inputCount = graph.PipelineInputTypeNames.Length;

        var diagram = PipelineSourceGenerator.GenerateMermaidDiagram(graph.PipelineInputTypeNames, graph.Segments, graph.Dependencies, graph.TerminalParameterName);

        var builder = new StringBuilder();

        AppendHead(builder, $"{shortName} | {projectName} | Dovetail Report");
        AppendNavOpen(builder, projectName, shortName, allPipelineLinks);

        builder.AppendLine( "    <main class=\"container\">");
        builder.AppendLine($"      <p><small>{Html(graph.ContainingType.Namespace)}</small></p>");
        builder.AppendLine($"      <h2>{Html(shortName)}</h2>");
        builder.AppendLine( "      <p>");
        builder.AppendLine($"        {inputCount} input{(inputCount == 1 ? "" : "s")} ·");
        builder.AppendLine($"        {graph.Segments.Length} segments ·");
        builder.AppendLine($"        → <strong>{Html(resultTypeName)}</strong>");

        if (graph.MaxConcurrency is int maxConcurrency)
        {
            builder.AppendLine($"        · <mark>MaxConcurrency {maxConcurrency}</mark>");
        }

        builder.AppendLine("      </p>");
        builder.AppendLine("        <pre class=\"mermaid\">%%{init: {\"theme\":\"neutral\"}}%%");
        builder.AppendLine(diagram);
        builder.AppendLine("        </pre>");
        builder.AppendLine("    </main>");

        AppendBodyClose(builder, includeMermaid: true);

        return builder.ToString();
    }

    private static void AppendHead(StringBuilder builder, string title)
    {
        builder.AppendLine( "<html>");
        builder.AppendLine( "  <head>");
        builder.AppendLine($"    <title>{Html(title)}</title>");
        builder.AppendLine();
        builder.AppendLine( "    <link rel=\"stylesheet\" href=\"vendor/pico.indigo.min.css\">");
        builder.AppendLine();
        
        builder.AppendLine(PageStyle);

        builder.AppendLine( "  </head>");
        builder.AppendLine( "  <body>");
    }

    private static void AppendNavOpen(StringBuilder builder, string projectName, string summaryText, IReadOnlyList<(string Name, string FileName)> pipelines)
    {
        builder.AppendLine( "    <header class=\"container\">");
        builder.AppendLine( "      <nav aria-label=\"Pipelines\">");
        builder.AppendLine( "        <ul>");
        builder.AppendLine( "          <li>");
        builder.AppendLine( "            <a href=\"index.html\">");

        builder.AppendLine(BrandSvg);

        builder.AppendLine($"              <strong>{Html(projectName)}</strong>");
        builder.AppendLine( "            </a>");
        builder.AppendLine( "          </li>");
        builder.AppendLine( "        </ul>");
        builder.AppendLine( "        <ul>");
        builder.AppendLine( "          <li>");
        builder.AppendLine( "            <details class=\"dropdown\">");
        builder.AppendLine($"              <summary>{Html(summaryText)}</summary>");
        builder.AppendLine( "              <ul dir=\"rtl\">");

        foreach (var (name, fileName) in pipelines)
        {
            builder.AppendLine($"                <li><a href=\"{fileName}\">{Html(name)}</a></li>");
        }

        builder.AppendLine( "              </ul>");
        builder.AppendLine( "            </details>");
        builder.AppendLine( "          </li>");
        builder.AppendLine( "        </ul>");
        builder.AppendLine( "      </nav>");
        builder.AppendLine( "    </header>");
        builder.AppendLine();
    }

    private static void AppendBodyClose(StringBuilder builder, bool includeMermaid)
    {
        builder.AppendLine();

        if (includeMermaid)
        {
            builder.AppendLine("    <script src=\"vendor/mermaid.min.js\"></script>");
            builder.AppendLine("    <script>");
            builder.AppendLine("      mermaid.initialize({ startOnLoad: false });");
            builder.AppendLine("      mermaid.run();");
            builder.AppendLine("    </script>");
        }

        builder.AppendLine(DropdownCloseScript);

        builder.AppendLine("  </body>");
        builder.AppendLine("</html>");
    }

    private static string Html(string text) =>
        WebUtility.HtmlEncode(text);
}
