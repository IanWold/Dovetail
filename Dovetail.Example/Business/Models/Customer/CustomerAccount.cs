namespace Dovetail.Example.Business;

public record CustomerAccount(
    UserId UserId,
    string FullName,
    string Email,
    string MembershipTier
);
