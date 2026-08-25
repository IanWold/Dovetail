using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class EndomorphismChainTests
{
    [Fact]
    public void SingleEndomorphism_ReadingRawPipelineInput_GeneratesSuccessfully()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Money(int Cents);

            public class RoundUpSegment : IPipelineSegment<Money, Money>
            {
                public Task<Money> ExecuteAsync(Money value, CancellationToken ct) => Task.FromResult(new Money(value.Cents + 1));
            }

            public partial class RoundUpPipeline([Segment] RoundUpSegment roundUp) : IPipeline<Money, Money>;
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.GeneratedTrees);
    }

    [Fact]
    public async Task ChainOfOriginAndEndomorphism_TerminalConsumerReceivesTheRefinedValue()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct U(int Value);

            public class ASegment : IPipelineSegment<int, U>
            {
                public Task<U> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new U(value));
            }

            public class BSegment : IPipelineSegment<U, U>
            {
                public Task<U> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(new U(value.Value * 2));
            }

            public class CSegment : IPipelineSegment<U, string>
            {
                public Task<string> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
            }

            public partial class ChainedPipeline([Segment] ASegment a, [Segment] BSegment b, [Segment] CSegment c) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.ChainedPipeline")!;
        var a = Activator.CreateInstance(assembly.GetType("Sample.ASegment")!)!;
        var b = Activator.CreateInstance(assembly.GetType("Sample.BSegment")!)!;
        var c = Activator.CreateInstance(assembly.GetType("Sample.CSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, a, b, c)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [5, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("10", result);
    }

    [Fact]
    public async Task ChainWhereEndomorphismIsAlsoTheTerminal_ReturnsTheRefinedValue()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct U(int Value);

            public class ASegment : IPipelineSegment<int, U>
            {
                public Task<U> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new U(value));
            }

            public class BSegment : IPipelineSegment<U, U>
            {
                public Task<U> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(new U(value.Value + 100));
            }

            public partial class TerminalChainPipeline([Segment] ASegment a, [Segment] BSegment b) : IPipeline<int, U>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.TerminalChainPipeline")!;
        var a = Activator.CreateInstance(assembly.GetType("Sample.ASegment")!)!;
        var b = Activator.CreateInstance(assembly.GetType("Sample.BSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, a, b)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [5, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var uType = assembly.GetType("Sample.U")!;
        var value = (int)uType.GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(105, value);
    }

    [Fact]
    public async Task SingleEndomorphism_ConsumingRawPipelineInput_ExternalConsumerReceivesTheRefinedValue()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct U(int Value);

            public class BSegment : IPipelineSegment<U, U>
            {
                public Task<U> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(new U(value.Value + 1));
            }

            public class CSegment : IPipelineSegment<U, string>
            {
                public Task<string> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
            }

            public partial class RawInputChainPipeline([Segment] BSegment b, [Segment] CSegment c) : IPipeline<U, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.RawInputChainPipeline")!;
        var b = Activator.CreateInstance(assembly.GetType("Sample.BSegment")!)!;
        var c = Activator.CreateInstance(assembly.GetType("Sample.CSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, b, c)!;
        var uType = assembly.GetType("Sample.U")!;
        var input = Activator.CreateInstance(uType, 5)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [input, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("6", result);
    }

    [Fact]
    public async Task MultiInputSegment_WithOneChainParticipatingInput_ResolvesEachInputCorrectly()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct U(int Value);

            public class OriginSegment : IPipelineSegment<int, U>
            {
                public Task<U> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new U(value));
            }

            public class RefineSegment : IPipelineSegment<U, U>
            {
                public Task<U> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(new U(value.Value + 1));
            }

            public class CombineSegment : IPipelineSegment<U, string, string>
            {
                public Task<string> ExecuteAsync(U u, string g, CancellationToken ct) => Task.FromResult($"{u.Value}-{g}");
            }

            public partial class MixedChainPipeline(
                [Segment] OriginSegment origin,
                [Segment] RefineSegment refine,
                [Segment] CombineSegment combine
            ) : IPipeline<int, string, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MixedChainPipeline")!;
        var origin = Activator.CreateInstance(assembly.GetType("Sample.OriginSegment")!)!;
        var refine = Activator.CreateInstance(assembly.GetType("Sample.RefineSegment")!)!;
        var combine = Activator.CreateInstance(assembly.GetType("Sample.CombineSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, origin, refine, combine)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [5, "hi", CancellationToken.None])!;
        var result = await task;

        Assert.Equal("6-hi", result);
    }

    [Fact]
    public async Task MultiInputEndomorphicSegment_WithOneChainParticipatingInput_ResolvesEachInputCorrectly()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct U(int Value);
            public readonly record struct V(int Value);

            public class OriginSegment : IPipelineSegment<int, U>
            {
                public Task<U> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new U(value));
            }

            public class IntermediateSegment : IPipelineSegment<string, V>
            {
                public Task<V> ExecuteAsync(string value, CancellationToken ct) => Task.FromResult(new V(value.Length));
            }

            public class RefineSegment : IPipelineSegment<U, V, U>
            {
                public Task<U> ExecuteAsync(U uValue, V vValue, CancellationToken ct) => Task.FromResult(new U(uValue.Value + vValue.Value));
            }

            public class CombineSegment : IPipelineSegment<U, long>
            {
                public Task<long> ExecuteAsync(U u, CancellationToken ct) => Task.FromResult((long)u.Value);
            }

            public partial class MixedChainPipeline(
                [Segment] OriginSegment origin,
                [Segment] IntermediateSegment intermediate,
                [Segment] RefineSegment refine,
                [Segment] CombineSegment combine
            ) : IPipeline<int, string, long>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MixedChainPipeline")!;
        var origin = Activator.CreateInstance(assembly.GetType("Sample.OriginSegment")!)!;
        var intermediate = Activator.CreateInstance(assembly.GetType("Sample.IntermediateSegment")!)!;
        var refine = Activator.CreateInstance(assembly.GetType("Sample.RefineSegment")!)!;
        var combine = Activator.CreateInstance(assembly.GetType("Sample.CombineSegment")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, origin, intermediate, refine, combine)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<long>)method.Invoke(pipeline, [5, "hi", CancellationToken.None])!;
        var result = await task;

        Assert.Equal(7, result);
    }

    [Fact]
    public void ReportsDiagnostic_WhenThreeSegmentsProduceTheSameType()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<string, int>
            {
                public Task<int> ExecuteAsync(string value, CancellationToken ct) => Task.FromResult(value.Length);
            }

            public class BarSegment : IPipelineSegment<bool, int>
            {
                public Task<int> ExecuteAsync(bool value, CancellationToken ct) => Task.FromResult(value ? 1 : 0);
            }

            public class BazSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class TriplicatePipeline([Segment] FooSegment foo, [Segment] BarSegment bar, [Segment] BazSegment baz) : IPipeline<string, bool, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE020");
    }

    [Fact]
    public void ReportsDiagnostic_WhenPipelineInputCollidesWithAnUnrelatedSegmentResult()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Sku(int Value);
            public readonly record struct Money(int Cents);
            public readonly record struct Receipt(int Cents);

            public class LookupSegment : IPipelineSegment<Sku, Money>
            {
                public Task<Money> ExecuteAsync(Sku value, CancellationToken ct) => Task.FromResult(new Money(value.Value * 100));
            }

            public class SpendSegment : IPipelineSegment<Money, Receipt>
            {
                public Task<Receipt> ExecuteAsync(Money value, CancellationToken ct) => Task.FromResult(new Receipt(value.Cents));
            }

            public partial class CollisionPipeline([Segment] LookupSegment lookup, [Segment] SpendSegment spend) : IPipeline<Sku, Money, Receipt>;
            """;

        AssertSingleDiagnostic(source, "DOVE018");
    }

    [Fact]
    public void ReportsDiagnostic_WhenAValidChainIsUnreachableFromTheTerminal()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class OriginSegment : IPipelineSegment<int, string>
            {
                public Task<string> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value.ToString());
            }

            public class RefineSegment : IPipelineSegment<string, string>
            {
                public Task<string> ExecuteAsync(string value, CancellationToken ct) => Task.FromResult(value.Trim());
            }

            public class TerminalSegment : IPipelineSegment<int, bool>
            {
                public Task<bool> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value > 0);
            }

            public partial class UnreachableChainPipeline([Segment] OriginSegment origin, [Segment] RefineSegment refine, [Segment] TerminalSegment terminal) : IPipeline<int, bool>;
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.GeneratedTrees);
        Assert.Equal(2, result.Diagnostics.Length);
        Assert.All(result.Diagnostics, static d => Assert.Equal("DOVE008", d.Id));
    }

    [Fact]
    public void ReportsDiagnostic_WhenALoneEndomorphismsInputIsUnresolved()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class RefineSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value + 1);
            }

            public partial class UnresolvedEndomorphismPipeline([Segment] RefineSegment refine) : IPipeline<string, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE006");
    }

    [Fact]
    public void ReportsDiagnostic_WhenPipelineInputCollidesWithAnOriginAndEndomorphismChain()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct U(int Value);

            public class OriginSegment : IPipelineSegment<int, U>
            {
                public Task<U> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new U(value));
            }

            public class RefineSegment : IPipelineSegment<U, U>
            {
                public Task<U> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(new U(value.Value + 1));
            }

            public partial class DoublyOriginedPipeline(
                [Segment] OriginSegment origin,
                [Segment] RefineSegment refine
            ) : IPipeline<int, U, U>;
            """;

        AssertSingleDiagnostic(source, "DOVE018");
    }

    [Fact]
    public void ReportsDiagnostic_WhenAValidChainCombinesWithAnUnrelatedDependencyToCloseACycle()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct U(int Value);
            public readonly record struct W(int Value);

            public class ASegment : IPipelineSegment<int, W, U>
            {
                public Task<U> ExecuteAsync(int t, W w, CancellationToken ct) => Task.FromResult(new U(t + w.Value));
            }

            public class BSegment : IPipelineSegment<U, U>
            {
                public Task<U> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(value);
            }

            public class DSegment : IPipelineSegment<U, W>
            {
                public Task<W> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(new W(value.Value));
            }

            public partial class CyclicChainPipeline([Segment] ASegment a, [Segment] BSegment b, [Segment] DSegment d) : IPipeline<int, U>;
            """;

        AssertSingleDiagnostic(source, "DOVE007");
    }
}
