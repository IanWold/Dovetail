using Dovetail.Report;
using Microsoft.Build.Locator;

if (!MSBuildLocator.IsRegistered)
{
    MSBuildLocator.RegisterDefaults();
}

return await ReportGenerator.RunAsync(args, Console.Out, Console.Error);
