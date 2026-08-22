using System.Collections.Immutable;
using System.Linq;

namespace Dovetail;

internal readonly record struct TypeDeclarationModel(
    string Namespace,
    string Name,
    bool IsPartial,
    ImmutableArray<ContainingTypeModel> ContainingTypes,
    string TypeParameterList,
    string Keyword
)
{
    public bool Equals(TypeDeclarationModel other) =>
        Namespace == other.Namespace
        && Name == other.Name
        && IsPartial == other.IsPartial
        && ContainingTypes.SequenceEqual(other.ContainingTypes)
        && TypeParameterList == other.TypeParameterList
        && Keyword == other.Keyword;

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Namespace.GetHashCode();
            hash = hash * 31 + Name.GetHashCode();
            hash = hash * 31 + IsPartial.GetHashCode();

            foreach (var containingType in ContainingTypes)
            {
                hash = hash * 31 + containingType.GetHashCode();
            }

            hash = hash * 31 + TypeParameterList.GetHashCode();
            hash = hash * 31 + Keyword.GetHashCode();
            
            return hash;
        }
    }
}
