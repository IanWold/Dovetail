using static Dovetail.Report.Tests.TestHelpers;

namespace Dovetail.Report.Tests;

public class DiscoveryTests
{
    [Fact]
    public void FindSegmentMembers_ForConstructorParameterSegment_FindsIt()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public partial class SimplePipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var compilation = CreateCompilation(source);
        var candidateType = compilation.GetTypeByMetadataName("Sample.SimplePipeline")!;

        var members = PipelineSourceGenerator.FindSegmentMembers(candidateType);
        var member = Assert.Single(members);

        Assert.Equal("foo", member.ParameterName);
        Assert.False(member.IsStaticSegmentMethod);
    }

    [Fact]
    public void FindSegmentMembers_ForStaticMethodSegment_FindsIt()
    {
        const string source = """
            using Dovetail;

            namespace Sample;

            public partial class StaticSegmentPipeline() : IPipeline<int, string>
            {
                [Segment]
                private static string Stringify(int value) => value.ToString();
            }
            """;

        var compilation = CreateCompilation(source);
        var candidateType = compilation.GetTypeByMetadataName("Sample.StaticSegmentPipeline")!;

        var members = PipelineSourceGenerator.FindSegmentMembers(candidateType);
        var member = Assert.Single(members);

        Assert.Equal("Stringify", member.ParameterName);
        Assert.True(member.IsStaticSegmentMethod);
    }

    [Fact]
    public void FindSegmentMembers_ForTypeWithNoSegmentAttributes_ReturnsEmpty()
    {
        const string source = """
            namespace Sample;

            public class NotAPipeline
            {
                public int Value { get; set; }
            }
            """;

        var compilation = CreateCompilation(source);
        var candidateType = compilation.GetTypeByMetadataName("Sample.NotAPipeline")!;

        Assert.Empty(PipelineSourceGenerator.FindSegmentMembers(candidateType));
    }

    [Fact]
    public void TryBuildGraph_ForFanOutFanInPipeline_BuildsExpectedGraphWithDependenciesAndTerminal()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Doubled(int Value);
            public readonly record struct Tripled(long Value);

            public class DoubleSegment : IPipelineSegment<int, Doubled>
            {
                public Task<Doubled> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Doubled(value * 2));
            }

            public class TripleSegment : IPipelineSegment<int, Tripled>
            {
                public Task<Tripled> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Tripled(value * 3L));
            }

            public partial class FanPipeline(
                [Segment] DoubleSegment doubled,
                [Segment] TripleSegment tripled
            ) : IPipeline<int, string>
            {
                [Segment]
                private static string Assemble(Doubled doubled, Tripled tripled) => $"{doubled.Value}-{tripled.Value}";
            }
            """;

        var compilation = CreateCompilation(source);
        var candidateType = compilation.GetTypeByMetadataName("Sample.FanPipeline")!;
        var members = PipelineSourceGenerator.FindSegmentMembers(candidateType);

        Assert.Equal(3, members.Length);

        var built = PipelineSourceGenerator.TryBuildGraph(members[0].ContainingType, members, static _ => { }, out var graph);

        Assert.True(built);

        var model = graph!.Value;

        Assert.Equal("FanPipeline", model.ContainingType.Name);
        Assert.Equal(3, model.Segments.Length);
        Assert.Equal("Assemble", model.TerminalParameterName);
        Assert.Single(model.PipelineInputTypeNames);
        Assert.Empty(model.Dependencies["doubled"].Where(static b => b.SegmentParameterName is not null));
        Assert.Empty(model.Dependencies["tripled"].Where(static b => b.SegmentParameterName is not null));
        Assert.Equal(2, model.Dependencies["Assemble"].Length);
    }

    [Fact]
    public void TryBuildGraph_ForPipelineWithMaxConcurrency_CarriesItThroughToTheModel()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            [MaxConcurrency(2)]
            public partial class ThrottledPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var compilation = CreateCompilation(source);
        var candidateType = compilation.GetTypeByMetadataName("Sample.ThrottledPipeline")!;
        var members = PipelineSourceGenerator.FindSegmentMembers(candidateType);

        PipelineSourceGenerator.TryBuildGraph(members[0].ContainingType, members, static _ => { }, out var graph);

        Assert.Equal(2, graph!.Value.MaxConcurrency);
    }

    [Fact]
    public void TryBuildGraph_ForInvalidMaxConcurrency_ReportsDiagnosticAndFails()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            [MaxConcurrency(0)]
            public partial class InvalidPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        var compilation = CreateCompilation(source);
        var candidateType = compilation.GetTypeByMetadataName("Sample.InvalidPipeline")!;
        var members = PipelineSourceGenerator.FindSegmentMembers(candidateType);

        var reported = new List<Microsoft.CodeAnalysis.Diagnostic>();
        var built = PipelineSourceGenerator.TryBuildGraph(members[0].ContainingType, members, reported.Add, out var graph);

        Assert.False(built);
        Assert.Null(graph);
        Assert.Contains(reported, static d => d.Id == "DOVE019");
    }
}
