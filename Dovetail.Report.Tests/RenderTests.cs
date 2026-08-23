using System.Collections.Immutable;

namespace Dovetail.Report.Tests;

public class RenderTests
{
    private static TypeDeclarationModel MakeType(string @namespace, string name, string typeParameterList = "", ImmutableArray<ContainingTypeModel> containingTypes = default) =>
        new(@namespace, name, IsPartial: true, containingTypes.IsDefault ? [] : containingTypes, typeParameterList, "class");

    [Fact]
    public void GetFullyQualifiedName_CombinesNamespaceAndName()
    {
        var type = MakeType("Sample.Business", "CartSummaryPipeline");

        Assert.Equal("Sample.Business.CartSummaryPipeline", Render.GetFullyQualifiedName(type));
    }

    [Fact]
    public void GetFullyQualifiedName_IncludesContainingTypeChain()
    {
        var containingTypes = ImmutableArray.Create(new ContainingTypeModel("Outer", "class", IsPartial: true, IsGeneric: false));
        var type = MakeType("Sample", "InnerPipeline", containingTypes: containingTypes);

        Assert.Equal("Sample.Outer.InnerPipeline", Render.GetFullyQualifiedName(type));
    }

    [Fact]
    public void GetPageFileName_SanitizesCharactersUnsafeForFileNames()
    {
        var type = MakeType("Sample", "GenericPipeline", typeParameterList: "<T>");
        var fileName = Render.GetPageFileName(type);

        Assert.DoesNotContain('<', fileName);
        Assert.DoesNotContain('>', fileName);
        Assert.EndsWith(".html", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPageFileName_ForDifferentNamespacesWithSameShortTypeName_ProducesDifferentFileNames()
    {
        var first = MakeType("Sample.Orders", "SummaryPipeline");
        var second = MakeType("Sample.Carts", "SummaryPipeline");
        var firstFileName = Render.GetPageFileName(first);
        var secondFileName = Render.GetPageFileName(second);

        Assert.NotEqual(firstFileName, secondFileName);
    }

    [Fact]
    public void SortingByFullyQualifiedName_OrdersPipelinesAlphabetically()
    {
        var types = new[]
        {
            MakeType("Sample", "ZebraPipeline"),
            MakeType("Sample", "AardvarkPipeline"),
            MakeType("Sample", "MidPipeline"),
        };

        var sorted = types
            .OrderBy(Render.GetFullyQualifiedName, StringComparer.Ordinal)
            .Select(Render.GetFullyQualifiedName)
            .ToArray();

        Assert.Equal(["Sample.AardvarkPipeline", "Sample.MidPipeline", "Sample.ZebraPipeline"], sorted);
    }
}
