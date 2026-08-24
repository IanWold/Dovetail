# Contributing to Dovetail

Dovetail is pre-1.0 and its API can still change. **Please open an issue or start a discussion before working on a PR**, especially for anything beyond a small, obvious fix. It's a lot easier to agree on the shape of a change before it's written than to rework a finished PR.

Good things to open an issue for:

* A bug in the generator (a pipeline that should compile and doesn't, or vice versa; a diagnostic that fires incorrectly; generated code that doesn't behave as documented).
* Graph shapes that are currently unsupported but have a use case to be added to Dovetail.
* A feature or API change you'd like to make.
* Anything in [the README](README.md) that's unclear or out of date.

## Development setup

You'll need the .NET 10 SDK or later, then everything should be straightforward.

```bash
dotnet build
dotnet test
```

The solution is [Dovetail.slnx](Dovetail.slnx), with the following projects:

* **Dovetail:** the public API (`IPipeline`, `IPipelineSegment`, `SegmentAttribute`) and both source generators.
* **Dovetail.Tests:** xUnit tests, including generator tests that compile sample source through `CSharpGeneratorDriver` and, for the more involved cases, actually emit and load the resulting assembly to run the generated code and assert on real behavior.
* **Dovetail.Report:** the HTML-report-generating dotnet tool. Reuses the graph-generating logic in **Dovetail**.
* **Dovetail.Report.Tests:** xUnit tests set up similar to **Dovetail.Tests**, only tests the extra functionality in **Dovetail.Report"

If you're changing the generator, a test that drives it against a small sample pipeline (see `PipelineSourceGeneratorTests.cs`) is the fastest way to see what it actually emits.

## Making a change

1. If there is an open issue you would like to implement, please start a conversation on that issue stating you would like to take it on.
2. Fork the repo and branch from `main`.
3. Make your change, with tests and documentation as necessary. Communicate any clarifying questions or blocks in the related issue.
4. `dotnet build` and `dotnet test` should both be clean, without warnings.
5. Open a PR against `main`. CI (`.github/workflows/build.yml`) runs build and test on every PR.

By submitting a PR, you agree your contribution is licensed under this project's [MIT license](LICENSE).
