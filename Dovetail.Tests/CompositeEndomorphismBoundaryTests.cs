using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class CompositeEndomorphismBoundaryTests
{
    [Fact]
    public async Task ThreeSegmentLoop_BackToThePipelinesOwnBoundaryType_ResolvesInDeclaredOrder()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct C(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value + 1));
            }

            public class Seg2 : IPipelineSegment<B, C>
            {
                public Task<C> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new C(value.Value * 2));
            }

            public class Seg3 : IPipelineSegment<C, A>
            {
                public Task<A> ExecuteAsync(C value, CancellationToken ct) => Task.FromResult(new A(value.Value - 3));
            }

            public partial class LoopPipeline([Segment] Seg1 seg1, [Segment] Seg2 seg2, [Segment] Seg3 seg3) : IPipeline<A, A>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.LoopPipeline")!;
        var seg1 = Activator.CreateInstance(assembly.GetType("Sample.Seg1")!)!;
        var seg2 = Activator.CreateInstance(assembly.GetType("Sample.Seg2")!)!;
        var seg3 = Activator.CreateInstance(assembly.GetType("Sample.Seg3")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, seg1, seg2, seg3)!;
        var aType = assembly.GetType("Sample.A")!;
        var input = Activator.CreateInstance(aType, 5)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)aType.GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(9, value);
    }

    [Fact]
    public async Task TwoSegmentLoop_BackToThePipelinesOwnBoundaryType_ResolvesInDeclaredOrder()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value + 1));
            }

            public class Seg2 : IPipelineSegment<B, A>
            {
                public Task<A> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new A(value.Value * 2));
            }

            public partial class LoopPipeline([Segment] Seg1 seg1, [Segment] Seg2 seg2) : IPipeline<A, A>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.LoopPipeline")!;
        var seg1 = Activator.CreateInstance(assembly.GetType("Sample.Seg1")!)!;
        var seg2 = Activator.CreateInstance(assembly.GetType("Sample.Seg2")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, seg1, seg2)!;
        var aType = assembly.GetType("Sample.A")!;
        var input = Activator.CreateInstance(aType, 5)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)aType.GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(12, value);
    }

    [Fact]
    public void MultiInputPipeline_WhereOnlyOneDeclaredInputIsTheBoundaryType_GeneratesSuccessfully()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct C(int Value);
            public readonly record struct X(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value));
            }

            public class Seg2 : IPipelineSegment<B, C>
            {
                public Task<C> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new C(value.Value));
            }

            public class Seg3 : IPipelineSegment<C, A>
            {
                public Task<A> ExecuteAsync(C value, CancellationToken ct) => Task.FromResult(new A(value.Value));
            }

            public partial class LoopPipeline(
                [Segment] Seg1 seg1,
                [Segment] Seg2 seg2,
                [Segment] Seg3 seg3
            ) : IPipeline<A, X, A>;
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.GeneratedTrees);
    }

    [Fact]
    public async Task FanOutOffAnInteriorChainType_ResolvesAlongsideTheBoundaryLoop()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct C(int Value);
            public readonly record struct D(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value + 1));
            }

            public class Seg2 : IPipelineSegment<B, C>
            {
                public Task<C> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new C(value.Value * 2));
            }

            public class SegSide : IPipelineSegment<B, D>
            {
                public Task<D> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new D(value.Value + 100));
            }

            public class Seg3 : IPipelineSegment<C, D, A>
            {
                public Task<A> ExecuteAsync(C c, D d, CancellationToken ct) => Task.FromResult(new A(c.Value + d.Value));
            }

            public partial class LoopPipeline(
                [Segment] Seg1 seg1,
                [Segment] Seg2 seg2,
                [Segment] SegSide segSide,
                [Segment] Seg3 seg3
            ) : IPipeline<A, A>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.LoopPipeline")!;
        var seg1 = Activator.CreateInstance(assembly.GetType("Sample.Seg1")!)!;
        var seg2 = Activator.CreateInstance(assembly.GetType("Sample.Seg2")!)!;
        var segSide = Activator.CreateInstance(assembly.GetType("Sample.SegSide")!)!;
        var seg3 = Activator.CreateInstance(assembly.GetType("Sample.Seg3")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, seg1, seg2, segSide, seg3)!;
        var aType = assembly.GetType("Sample.A")!;
        var input = Activator.CreateInstance(aType, 5)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)aType.GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(118, value);
    }

    [Fact]
    public async Task InteriorSegment_WithAnExtraInputFromOutsideTheChain_ResolvesCorrectly()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct C(int Value);
            public readonly record struct G(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value));
            }

            public class Seg2 : IPipelineSegment<B, G, C>
            {
                public Task<C> ExecuteAsync(B b, G g, CancellationToken ct) => Task.FromResult(new C(b.Value + g.Value));
            }

            public class Seg3 : IPipelineSegment<C, A>
            {
                public Task<A> ExecuteAsync(C value, CancellationToken ct) => Task.FromResult(new A(value.Value));
            }

            public partial class LoopPipeline(
                [Segment] Seg1 seg1,
                [Segment] Seg2 seg2,
                [Segment] Seg3 seg3
            ) : IPipeline<A, G, A>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.LoopPipeline")!;
        var seg1 = Activator.CreateInstance(assembly.GetType("Sample.Seg1")!)!;
        var seg2 = Activator.CreateInstance(assembly.GetType("Sample.Seg2")!)!;
        var seg3 = Activator.CreateInstance(assembly.GetType("Sample.Seg3")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, seg1, seg2, seg3)!;
        var aType = assembly.GetType("Sample.A")!;
        var gType = assembly.GetType("Sample.G")!;
        var a = Activator.CreateInstance(aType, 5)!;
        var g = Activator.CreateInstance(gType, 100)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [a, g, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)aType.GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(105, value);
    }

    [Fact]
    public async Task MiddleSegment_AlsoConsumingTheRawBoundaryInput_ReceivesBothValues()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct C(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value + 1));
            }

            public class Seg2 : IPipelineSegment<B, A, C>
            {
                public Task<C> ExecuteAsync(B b, A original, CancellationToken ct) => Task.FromResult(new C(b.Value * 100 + original.Value));
            }

            public class Seg3 : IPipelineSegment<C, A>
            {
                public Task<A> ExecuteAsync(C value, CancellationToken ct) => Task.FromResult(new A(value.Value));
            }

            public partial class LoopPipeline([Segment] Seg1 seg1, [Segment] Seg2 seg2, [Segment] Seg3 seg3) : IPipeline<A, A>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.LoopPipeline")!;
        var seg1 = Activator.CreateInstance(assembly.GetType("Sample.Seg1")!)!;
        var seg2 = Activator.CreateInstance(assembly.GetType("Sample.Seg2")!)!;
        var seg3 = Activator.CreateInstance(assembly.GetType("Sample.Seg3")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, seg1, seg2, seg3)!;
        var aType = assembly.GetType("Sample.A")!;
        var input = Activator.CreateInstance(aType, 5)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)aType.GetProperty("Value")!.GetValue(result)!;

        Assert.Equal(605, value);
    }

    [Fact]
    public async Task MultiInputChainMember_DrawingAnExtraInputFromThePipeline_ResolvesWithoutRedundantDeclaration()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct X(int Value);
            public readonly record struct Y(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value + 1));
            }

            public class SegQ : IPipelineSegment<Y, B, X>
            {
                public Task<X> ExecuteAsync(Y y, B b, CancellationToken ct) => Task.FromResult(new X(y.Value + b.Value));
            }

            public class Seg3 : IPipelineSegment<X, A>
            {
                public Task<A> ExecuteAsync(X value, CancellationToken ct) => Task.FromResult(new A(value.Value * 2));
            }

            public partial class LoopPipeline(
                [Segment] Seg1 seg1,
                [Segment] SegQ segQ,
                [Segment] Seg3 seg3
            ) : IPipeline<A, Y, A>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.LoopPipeline")!;
        var seg1 = Activator.CreateInstance(assembly.GetType("Sample.Seg1")!)!;
        var segQ = Activator.CreateInstance(assembly.GetType("Sample.SegQ")!)!;
        var seg3 = Activator.CreateInstance(assembly.GetType("Sample.Seg3")!)!;
        var pipeline = Activator.CreateInstance(pipelineType, seg1, segQ, seg3)!;
        var aType = assembly.GetType("Sample.A")!;
        var yType = assembly.GetType("Sample.Y")!;
        var a = Activator.CreateInstance(aType, 5)!;
        var y = Activator.CreateInstance(yType, 100)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [a, y, CancellationToken.None])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (int)aType.GetProperty("Value")!.GetValue(result)!;

        // B = 6; X = 100 + 6 = 106; A = 106 * 2 = 212
        Assert.Equal(212, value);
    }

    [Fact]
    public void RegressionGuard_GenuinelyAmbiguousNonCyclicCollision_StillReportsDove018()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct Foo(int Value);
            public readonly record struct Bar(int Value);

            public class SegX : IPipelineSegment<Foo, A>
            {
                public Task<A> ExecuteAsync(Foo value, CancellationToken ct) => Task.FromResult(new A(value.Value));
            }

            public class SegY : IPipelineSegment<A, Bar>
            {
                public Task<Bar> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new Bar(value.Value));
            }

            public partial class AmbiguousPipeline([Segment] SegX segX, [Segment] SegY segY) : IPipeline<A, Foo, Bar>;
            """;

        AssertSingleDiagnostic(source, "DOVE018");
    }

    [Fact]
    public void RealCycle_ElsewhereInTheGraph_StillReportsDove007()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct C(int Value);
            public readonly record struct D(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value));
            }

            public class Seg2 : IPipelineSegment<B, D, C>
            {
                public Task<C> ExecuteAsync(B b, D d, CancellationToken ct) => Task.FromResult(new C(b.Value + d.Value));
            }

            public class SegD : IPipelineSegment<C, D>
            {
                public Task<D> ExecuteAsync(C value, CancellationToken ct) => Task.FromResult(new D(value.Value));
            }

            public class Seg3 : IPipelineSegment<C, A>
            {
                public Task<A> ExecuteAsync(C value, CancellationToken ct) => Task.FromResult(new A(value.Value));
            }

            public partial class CyclicPipeline(
                [Segment] Seg1 seg1,
                [Segment] Seg2 seg2,
                [Segment] SegD segD,
                [Segment] Seg3 seg3
            ) : IPipeline<A, A>;
            """;

        AssertSingleDiagnostic(source, "DOVE007");
    }

    [Fact]
    public void TwoLiteralEndomorphisms_CompetingForTheBoundaryType_StillReportsDove020()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);

            public class SegE1 : IPipelineSegment<A, A>
            {
                public Task<A> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(value);
            }

            public class SegE2 : IPipelineSegment<A, A>
            {
                public Task<A> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class CompetingPipeline([Segment] SegE1 segE1, [Segment] SegE2 segE2) : IPipeline<A, A>;
            """;

        AssertSingleDiagnostic(source, "DOVE020");
    }

    [Fact]
    public void InterdependentCollisions_ReportDove021ForTheMaskedOneAndDove018ForTheGenuineOne()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct X(int Value);
            public readonly record struct Y(int Value);

            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value));
            }

            public class SegQ : IPipelineSegment<Y, B, X>
            {
                public Task<X> ExecuteAsync(Y y, B b, CancellationToken ct) => Task.FromResult(new X(y.Value + b.Value));
            }

            public class Seg3 : IPipelineSegment<X, A>
            {
                public Task<A> ExecuteAsync(X value, CancellationToken ct) => Task.FromResult(new A(value.Value));
            }

            public partial class InterdependentPipeline(
                [Segment] Seg1 seg1,
                [Segment] SegQ segQ,
                [Segment] Seg3 seg3
            ) : IPipeline<A, X, Y, A>;
            """;

        var result = RunGenerator(source);

        Assert.Equal(2, result.Diagnostics.Length);

        var interdependent = Assert.Single(result.Diagnostics, static d => d.Id == "DOVE021");
        Assert.Contains("seg1", interdependent.GetMessage());
        Assert.Contains("seg3", interdependent.GetMessage());
        Assert.Single(interdependent.AdditionalLocations);

        var genuine = Assert.Single(result.Diagnostics, static d => d.Id == "DOVE018");
        Assert.Contains("seg3", genuine.GetMessage());
        Assert.Contains("segQ", genuine.GetMessage());
    }

    [Fact]
    public void CollisionWhoseCandidateIsNotItselfPending_DoesNotOverFireDove021()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct A(int Value);
            public readonly record struct B(int Value);
            public readonly record struct C(int Value);
            public readonly record struct U(int Value);
            public readonly record struct V(int Value);

            // Seg1's own A input is a pending boundary collision (candidate: Seg3).
            public class Seg1 : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B(value.Value));
            }

            public class Seg2 : IPipelineSegment<B, C>
            {
                public Task<C> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new C(value.Value));
            }

            // Depends on Seg1 (not itself pending), several hops from Seg1's own collision.
            public class SegDelta : IPipelineSegment<B, U>
            {
                public Task<U> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new U(value.Value));
            }

            // SegDelta is not itself a pending consumer, so this collision should resolve via the
            // ordinary reachability check (and correctly fail as genuinely ambiguous), not DOVE021.
            public class SegGamma : IPipelineSegment<U, V>
            {
                public Task<V> ExecuteAsync(U value, CancellationToken ct) => Task.FromResult(new V(value.Value));
            }

            public class Seg3 : IPipelineSegment<C, V, A>
            {
                public Task<A> ExecuteAsync(C c, V v, CancellationToken ct) => Task.FromResult(new A(c.Value + v.Value));
            }

            public partial class DeepButNotDirectPendingPipeline(
                [Segment] Seg1 seg1,
                [Segment] Seg2 seg2,
                [Segment] SegDelta segDelta,
                [Segment] SegGamma segGamma,
                [Segment] Seg3 seg3
            ) : IPipeline<A, U, A>;
            """;

        var diagnostic = Assert.Single(RunGenerator(source).Diagnostics);

        Assert.Equal("DOVE018", diagnostic.Id);
        Assert.Contains("segGamma", diagnostic.GetMessage());
    }
}
