using System.Diagnostics;
using System.Reflection;
using static Dovetail.Tests.TestHelpers;

namespace Dovetail.Tests;

public class TracingTests
{
    [Fact]
    public void EmitsTracing_WhenActivitySourceIsAvailable()
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

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var sources = result.Results.Single().GeneratedSources;
        Assert.Equal(2, sources.Length);

        var activitySource = Assert.Single(sources, static s => s.HintName == "DovetailActivitySource.g.cs").SourceText.ToString();
        Assert.Contains("internal static class DovetailActivitySource", activitySource);
        Assert.Contains("new global::System.Diagnostics.ActivitySource(\"Dovetail\")", activitySource);

        var pipelineSource = Assert.Single(sources, static s => s.HintName == "FooPipeline.g.cs").SourceText.ToString();
        Assert.Contains("using var activity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"FooPipeline.ExecuteAsync\");", pipelineSource);
        Assert.Contains("activity?.SetTag(\"dovetail.pipeline\", \"Sample.FooPipeline\");", pipelineSource);
        Assert.Contains("using var segmentActivity = global::Dovetail.DovetailActivitySource.Instance.StartActivity(\"FooPipeline.foo\");", pipelineSource);
        Assert.Contains("segmentActivity?.SetTag(\"dovetail.segment\", \"foo\");", pipelineSource);
        Assert.Contains("segmentActivity?.SetTag(\"dovetail.segment.type\", \"Sample.FooSegment\");", pipelineSource);
        Assert.Contains("segmentActivity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, ex.Message);", pipelineSource);
    }

    [Fact]
    public void DoesNotEmitTracing_WhenActivitySourceIsUnavailable()
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

        var result = RunGenerator(source, includeActivitySource: false);

        Assert.Empty(result.Diagnostics);

        var pipelineSource = Assert.Single(result.Results.Single().GeneratedSources);

        Assert.Equal("FooPipeline.g.cs", pipelineSource.HintName);
        Assert.DoesNotContain("StartActivity", pipelineSource.SourceText.ToString());
    }

    [Fact]
    public async Task GeneratedPipeline_RecordsActivitiesForPipelineAndEachSegment()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Dovetail;

            namespace Sample;

            public readonly record struct Doubled(int Value);

            public class DoubleSegment : IPipelineSegment<int, Doubled>
            {
                public Task<Doubled> ExecuteAsync(int value, CancellationToken ct) => Task.FromResult(new Doubled(value * 2));
            }

            public class ToStringSegment : IPipelineSegment<Doubled, string>
            {
                public Task<string> ExecuteAsync(Doubled value, CancellationToken ct) => Task.FromResult(value.Value.ToString());
            }

            public partial class NumberPipeline(
                [Segment] DoubleSegment doubler,
                [Segment] ToStringSegment stringifier
            ) : IPipeline<int, string>;
            """;

        var assembly = CompileAndLoad(source, new PipelineSourceGenerator());

        var dovetailActivitySource = (ActivitySource)assembly.GetType("Dovetail.DovetailActivitySource")!
            .GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var startedActivities = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => ReferenceEquals(activitySource, dovetailActivitySource),
            Sample = (ref _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => startedActivities.Add(activity),
        };

        ActivitySource.AddActivityListener(listener);

        try
        {
            var pipelineType = assembly.GetType("Sample.NumberPipeline")!;
            var doubler = Activator.CreateInstance(assembly.GetType("Sample.DoubleSegment")!)!;
            var stringifier = Activator.CreateInstance(assembly.GetType("Sample.ToStringSegment")!)!;
            var pipeline = Activator.CreateInstance(pipelineType, doubler, stringifier)!;

            var method = pipelineType.GetMethod("ExecuteAsync")!;
            var task = (Task<string>)method.Invoke(pipeline, [21, CancellationToken.None])!;
            var result = await task;

            Assert.Equal("42", result);
        }
        finally
        {
            listener.Dispose();
        }

        var names = startedActivities.Select(a => a.OperationName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["NumberPipeline.ExecuteAsync", "NumberPipeline.doubler", "NumberPipeline.stringifier"], names);

        var pipelineActivity = Assert.Single(startedActivities, a => a.OperationName == "NumberPipeline.ExecuteAsync");
        Assert.Equal("Sample.NumberPipeline", pipelineActivity.GetTagItem("dovetail.pipeline"));

        var doublerActivity = Assert.Single(startedActivities, a => a.OperationName == "NumberPipeline.doubler");
        Assert.Equal("doubler", doublerActivity.GetTagItem("dovetail.segment"));
        Assert.Equal("Sample.DoubleSegment", doublerActivity.GetTagItem("dovetail.segment.type"));
    }
}
