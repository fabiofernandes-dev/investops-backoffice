using System.Data;
using Dapper;

namespace InvestOps.Infrastructure.Persistence.Dapper;

public sealed class EnumStringHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
{
    public override T Parse(object value) => Enum.Parse<T>((string)value);

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value.ToString();
    }
}
