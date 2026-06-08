using Dapper;
using InvestOps.Domain.Enums;

namespace InvestOps.Infrastructure.Persistence.Dapper;

public static class DapperConfig
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        SqlMapper.AddTypeHandler(new EnumStringHandler<InvestorProfile>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<CustomerStatus>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<AccountStatus>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<ProductType>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<OperationType>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<OperationStatus>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<RejectionReason>());
        SqlMapper.AddTypeHandler(new EnumStringHandler<OutboxStatus>());

        _registered = true;
    }
}
