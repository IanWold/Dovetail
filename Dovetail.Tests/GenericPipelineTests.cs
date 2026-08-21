using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class GenericPipelineTests
{
    [Fact]
    public async Task GeneratedPipeline_ForClosedGenericSegmentType_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Wrapper<T> : IPipelineSegment<int, T>
            {
                private readonly T _value;
                public Wrapper(T value) => _value = value;
                public Task<T> ExecuteAsync(int input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class MyPipeline([Segment] Wrapper<string> wrapper) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MyPipeline")!;
        var wrapperType = assembly.GetType("Sample.Wrapper`1")!.MakeGenericType(typeof(string));
        var wrapper = Activator.CreateInstance(wrapperType, "hello")!;
        var pipeline = Activator.CreateInstance(pipelineType, wrapper)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task<string>)method.Invoke(pipeline, [1, CancellationToken.None])!;
        var result = await task;

        Assert.Equal("hello", result);
    }

    [Fact]
    public void EmitsFanOutFanIn_ForGenericPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }
            public class Result { public int Value { get; init; } }

            public class FirstSegment<T> : IPipelineSegment<Input, T> where T : new()
            {
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(new T());
            }

            public class SecondSegment<T> : IPipelineSegment<T, Result>
            {
                public Task<Result> ExecuteAsync(T value, CancellationToken ct) => Task.FromResult(new Result());
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first,
                [Segment] SecondSegment<T> second
            ) : IPipeline<Input, Result> where T : new();
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "MyPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("partial class MyPipeline<T>", text);
        Assert.Contains("async global::System.Threading.Tasks.Task<T> FirstAsync()", text);
        Assert.Contains("await second.ExecuteAsync(await firstTask.ConfigureAwait(false), linkedToken).ConfigureAwait(false);", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForGenericPipelineWithGenericSegments_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }

            public readonly record struct Boxed<T>(T Value);

            public class FirstSegment<T> : IPipelineSegment<Input, T>
            {
                private readonly T _value;
                public FirstSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(_value);
            }

            public class SecondSegment<T> : IPipelineSegment<T, Boxed<T>>
            {
                public Task<Boxed<T>> ExecuteAsync(T value, CancellationToken ct) => Task.FromResult(new Boxed<T>(value));
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first,
                [Segment] SecondSegment<T> second
            ) : IPipeline<Input, Boxed<T>>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MyPipeline`1")!.MakeGenericType(typeof(string));
        var firstType = assembly.GetType("Sample.FirstSegment`1")!.MakeGenericType(typeof(string));
        var secondType = assembly.GetType("Sample.SecondSegment`1")!.MakeGenericType(typeof(string));

        var first = Activator.CreateInstance(firstType, "hello")!;
        var second = Activator.CreateInstance(secondType)!;
        var pipeline = Activator.CreateInstance(pipelineType, first, second)!;
        var input = Activator.CreateInstance(assembly.GetType("Sample.Input")!)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;

        await task;

        var boxed = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (string)boxed.GetType().GetProperty("Value")!.GetValue(boxed)!;

        Assert.Equal("hello", value);
    }

    [Fact]
    public async Task GeneratedPipeline_ForNestedGenericPipeline_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class FooSegment<T> : IPipelineSegment<int, T>
            {
                private readonly T _value;
                public FooSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(int input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class Outer
            {
                public partial class NestedPipeline<T>([Segment] FooSegment<T> foo) : IPipeline<int, T>;
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var outerType = assembly.GetType("Sample.Outer")!;
        var pipelineType = outerType.GetNestedType("NestedPipeline`1")!.MakeGenericType(typeof(string));
        var fooType = assembly.GetType("Sample.FooSegment`1")!.MakeGenericType(typeof(string));

        var foo = Activator.CreateInstance(fooType, "nested-and-generic")!;
        var pipeline = Activator.CreateInstance(pipelineType, foo)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [1, CancellationToken.None])!;

        await task;

        var result = (string)task.GetType().GetProperty("Result")!.GetValue(task)!;

        Assert.Equal("nested-and-generic", result);
    }

    [Fact]
    public void EmitsFanOutFanIn_ForStaticMethodSegmentOnGenericPipeline()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }
            public readonly record struct Boxed<T>(T Value);

            public class FirstSegment<T> : IPipelineSegment<Input, T>
            {
                private readonly T _value;
                public FirstSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first
            ) : IPipeline<Input, Boxed<T>>
            {
                [Segment]
                private static Boxed<T> Wrap(T value) => new(value);
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var generated = Assert.Single(result.Results.Single().GeneratedSources, static s => s.HintName == "MyPipeline.g.cs");
        var text = generated.SourceText.ToString();

        Assert.Contains("partial class MyPipeline<T>", text);
        Assert.Contains("async global::System.Threading.Tasks.Task<global::Sample.Boxed<T>> WrapAsync()", text);
        Assert.Contains("return Wrap(await firstTask.ConfigureAwait(false));", text);
    }

    [Fact]
    public async Task GeneratedPipeline_ForStaticMethodSegmentOnGenericPipeline_ProducesCorrectResult()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public class Input { public int Value { get; init; } }
            public readonly record struct Boxed<T>(T Value);

            public class FirstSegment<T> : IPipelineSegment<Input, T>
            {
                private readonly T _value;
                public FirstSegment(T value) => _value = value;
                public Task<T> ExecuteAsync(Input input, CancellationToken ct) => Task.FromResult(_value);
            }

            public partial class MyPipeline<T>(
                [Segment] FirstSegment<T> first
            ) : IPipeline<Input, Boxed<T>>
            {
                [Segment]
                private static Boxed<T> Wrap(T value) => new(value);
            }
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());
        var pipelineType = assembly.GetType("Sample.MyPipeline`1")!.MakeGenericType(typeof(string));
        var firstType = assembly.GetType("Sample.FirstSegment`1")!.MakeGenericType(typeof(string));

        var first = Activator.CreateInstance(firstType, "hello")!;
        var pipeline = Activator.CreateInstance(pipelineType, first)!;
        var input = Activator.CreateInstance(assembly.GetType("Sample.Input")!)!;

        var method = pipelineType.GetMethod("ExecuteAsync")!;
        var task = (Task)method.Invoke(pipeline, [input, CancellationToken.None])!;

        await task;

        var boxed = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var value = (string)boxed.GetType().GetProperty("Value")!.GetValue(boxed)!;

        Assert.Equal("hello", value);
    }
}
