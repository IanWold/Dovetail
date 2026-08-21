using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void ReportsDiagnostic_WhenContainingTypeIsNotPartial()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public class NotPartialPipeline([Segment] FooSegment foo) : IPipeline<int, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE001");
    }

    [Fact]
    public void ReportsDiagnostic_WhenContainingTypeDoesNotImplementIPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class NotAPipeline([Segment] FooSegment foo);
            """;

        AssertSingleDiagnostic(source, "DOVE002");
    }

    [Fact]
    public void ReportsDiagnostic_WhenSegmentTypeDoesNotImplementIPipelineSegment()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class NotASegment;

            public partial class BadPipeline([Segment] NotASegment notSegment) : IPipeline<NotASegment, NotASegment>;
            """;

        AssertSingleDiagnostic(source, "DOVE003");
    }

    [Fact]
    public void ReportsDiagnostic_WhenNoSegmentProducesThePipelineResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class MissingTerminalPipeline([Segment] FooSegment foo) : IPipeline<int, string>;
            """;

        AssertSingleDiagnostic(source, "DOVE004");
    }

    [Fact]
    public void ReportsDiagnostic_WhenTwoSegmentsProduceTheSameType()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public class BarSegment : IPipelineSegment<int, int>
            {
                public Task<int> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(value);
            }

            public partial class DuplicatePipeline([Segment] FooSegment foo, [Segment] BarSegment bar) : IPipeline<int, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE005");
    }

    [Fact]
    public void ReportsDiagnostic_WhenASegmentInputIsUnresolved()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment : IPipelineSegment<string, int>
            {
                public Task<int> ExecuteAsync(string value, CancellationToken ct) => Task.FromResult(value.Length);
            }

            public partial class UnresolvedPipeline([Segment] FooSegment foo) : IPipeline<int, int>;
            """;

        AssertSingleDiagnostic(source, "DOVE006");
    }

    [Fact]
    public void ReportsDiagnostic_WhenSegmentsFormACycle()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class A;
            public class B;

            public class SegA : IPipelineSegment<B, A>
            {
                public Task<A> ExecuteAsync(B value, CancellationToken ct) => Task.FromResult(new A());
            }

            public class SegB : IPipelineSegment<A, B>
            {
                public Task<B> ExecuteAsync(A value, CancellationToken ct) => Task.FromResult(new B());
            }

            public partial class CyclePipeline([Segment] SegA segA, [Segment] SegB segB) : IPipeline<A>;
            """;

        AssertSingleDiagnostic(source, "DOVE007");
    }

    [Fact]
    public void ReportsDiagnostic_WhenASegmentIsUnreachableFromTheResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Foo;
            public class Bar;

            public class FooSegment : IPipelineSegment<int, Foo>
            {
                public Task<Foo> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Foo());
            }

            public class OrphanSegment : IPipelineSegment<int, Bar>
            {
                public Task<Bar> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Bar());
            }

            public partial class OrphanPipeline([Segment] FooSegment foo, [Segment] OrphanSegment orphan) : IPipeline<int, Foo>;
            """;

        AssertSingleDiagnostic(source, "DOVE008");
    }

    [Fact]
    public void ReportsDiagnostic_WhenThePipelineDeclaresTheSameInputTypeMoreThanOnce()
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

            public partial class DuplicateInputPipeline([Segment] FooSegment foo) : IPipeline<int, int, string>;
            """;

        AssertSingleDiagnostic(source, "DOVE009");
    }
}
