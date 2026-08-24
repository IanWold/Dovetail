using System.Xml.Linq;
using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class MermaidDiagramTests
{
    private static string GetGeneratedText(string source)
    {
        var result = RunGenerator(source, includeActivitySource: false);
        return Assert.Single(result.GeneratedTrees).GetText(TestContext.Current.CancellationToken).ToString();
    }

    private static string ExtractDocCommentXml(string generatedText)
    {
        var docLines = generatedText
            .Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .SkipWhile(static line => !line.TrimStart().StartsWith("/// <summary>"))
            .TakeWhile(static line => line.TrimStart().StartsWith("///"))
            .Select(static line => line.TrimStart().Substring(3).TrimStart())
            .ToArray();

        Assert.NotEmpty(docLines);
        return $"<root>{string.Join("\n", docLines)}</root>";
    }

    [Fact]
    public void EmitsMermaidDiagram_ForSimpleSingleSegmentPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class FooPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var text = GetGeneratedText(source);

        Assert.Contains("/// <code lang=\"mermaid\">", text);
        Assert.Contains("/// flowchart TD", text);
        Assert.Contains("///     in_0([\"input: int\"])", text);
        Assert.Contains("///     seg_foo(\"foo: string\")", text);
        Assert.Contains("///     in_0 --> seg_foo", text);
    }

    [Fact]
    public void EmitsMermaidDiagram_ForDiamondShapedPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Second;
            public class Third;
            public class Fourth;
            public class Fifth;

            public class CatalogSegment : IPipelineSegment<int, Second> { public Task<Second> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new Second()); }
            public class PricingSegment : IPipelineSegment<Second, Third> { public Task<Third> ExecuteAsync(Second v, CancellationToken ct) => Task.FromResult(new Third()); }
            public class RecommendationsSegment : IPipelineSegment<Second, Fourth> { public Task<Fourth> ExecuteAsync(Second v, CancellationToken ct) => Task.FromResult(new Fourth()); }

            public partial class DiamondPipeline(
                [Segment] CatalogSegment catalog,
                [Segment] PricingSegment pricing,
                [Segment] RecommendationsSegment recommendations
            ) : IPipeline<int, Fifth>
            {
                [Segment]
                private static Fifth Assemble(Third pricing, Fourth recommendations) => new();
            }
            """;

        var text = GetGeneratedText(source);

        Assert.Contains("///     seg_catalog[\"catalog: Second\"]", text);
        Assert.Contains("///     seg_pricing[\"pricing: Third\"]", text);
        Assert.Contains("///     seg_recommendations[\"recommendations: Fourth\"]", text);
        Assert.Contains("///     seg_Assemble(\"Assemble: Fifth\")", text);

        Assert.Contains("///     in_0 --> seg_catalog", text);
        Assert.Contains("///     seg_catalog --> seg_pricing", text);
        Assert.Contains("///     seg_catalog --> seg_recommendations", text);
        Assert.Contains("///     seg_pricing --> seg_Assemble", text);
        Assert.Contains("///     seg_recommendations --> seg_Assemble", text);
    }

    [Fact]
    public void EmitsMermaidDiagram_WithOneInputNodePerDeclaredPipelineInput()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Wrapped;

            public class FooSegment : IPipelineSegment<int, Wrapped>
            {
                public Task<Wrapped> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Wrapped());
            }

            public partial class MultiInputPipeline(
                [Segment] FooSegment foo
            ) : IPipeline<int, bool, string>
            {
                [Segment]
                private static string Combine(Wrapped foo, bool flag) => flag ? "yes" : "no";
            }
            """;

        var text = GetGeneratedText(source);

        Assert.Contains("///     in_0([\"input1: int\"])", text);
        Assert.Contains("///     in_1([\"input2: bool\"])", text);
        Assert.Contains("///     in_0 --> seg_foo", text);
        Assert.Contains("///     in_1 --> seg_Combine", text);
    }

    [Fact]
    public void EmitsMermaidDiagram_EscapingGenericAngleBracketsInLabels()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Item;

            public class ItemsSegment : IPipelineSegment<int, IReadOnlyList<Item>>
            {
                public Task<IReadOnlyList<Item>> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult<IReadOnlyList<Item>>(new List<Item>());
            }

            public partial class ItemsPipeline([Segment] ItemsSegment items) : IPipeline<int, IReadOnlyList<Item>>;
            """;

        var text = GetGeneratedText(source);

        Assert.Contains("///     seg_items(\"items: IReadOnlyList#lt;Item#gt;\")", text);
        Assert.DoesNotContain("IReadOnlyList<Item>", text.Split("public async").First());
    }

    [Fact]
    public void EmitsMaxConcurrencyNote_WhenAttributePresent()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            [MaxConcurrency(3)]
            public partial class GatedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var text = GetGeneratedText(source);

        Assert.Contains("No more than 3 of this pipeline's segments run at once (<c>[MaxConcurrency(3)]</c>).", text);
    }

    [Fact]
    public void DoesNotEmitMaxConcurrencyNote_WhenAttributeAbsent()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class UngatedPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var text = GetGeneratedText(source);

        Assert.DoesNotContain("MaxConcurrency", text);
    }

    [Fact]
    public void DocCommentIsWellFormedXml()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Item;

            public class CatalogSegment : IPipelineSegment<int, Item> { public Task<Item> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult(new Item()); }
            public class ItemsSegment : IPipelineSegment<int, IReadOnlyList<Item>> { public Task<IReadOnlyList<Item>> ExecuteAsync(int v, CancellationToken ct) => Task.FromResult<IReadOnlyList<Item>>(new List<Item>()); }

            [MaxConcurrency(2)]
            public partial class WellFormedPipeline(
                [Segment] CatalogSegment catalog,
                [Segment] ItemsSegment items
            ) : IPipeline<int, bool, string>
            {
                [Segment]
                private static string Assemble(Item catalog, IReadOnlyList<Item> items, bool flag) => "";
            }
            """;

        var text = GetGeneratedText(source);
        var xml = ExtractDocCommentXml(text);

        var exception = Record.Exception(() => XElement.Parse(xml));
        Assert.Null(exception);
    }

    [Fact]
    public void EmitsMermaidDiagram_ForEndomorphismChain()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class U;

            public class OriginSegment : IPipelineSegment<int, U>
            {
                public Task<U> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new U());
            }

            public class RefineSegment : IPipelineSegment<U, U>
            {
                public Task<U> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class ChainPipeline([Segment] OriginSegment origin, [Segment] RefineSegment refine) : IPipeline<int, U>;
            """;

        var text = GetGeneratedText(source);

        Assert.Contains("/// flowchart TD", text);
        Assert.Contains("///     in_0([\"input: int\"])", text);
        Assert.Contains("///     seg_origin[\"origin: U\"]", text);
        Assert.Contains("///     seg_refine(\"refine: U\")", text);
        Assert.Contains("///     in_0 --> seg_origin", text);
        Assert.Contains("///     seg_origin --> seg_refine", text);

        var edgeCount = text.Split('\n').Count(static line => line.Contains("-->"));
        Assert.Equal(2, edgeCount);
    }
}
