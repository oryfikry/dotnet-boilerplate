using System.Data;
using System.Globalization;
using Dapper;

namespace Api.Infrastructure.Data;

/// <summary>
/// Dapper type handlers for SQLite. Microsoft.Data.Sqlite stores every
/// strongly-typed CLR value as <c>TEXT</c> or <c>INTEGER</c>; Dapper's
/// default mapper cannot coerce <c>string</c> back to <see cref="Guid"/>,
/// <see cref="DateTime"/>, or <see cref="decimal"/> when materializing
/// records via positional constructors.
///
/// Registered via <see cref="SqlMapper.AddTypeHandler{T}(SqlMapper.TypeHandler{T})"/>
/// at startup whenever <see cref="DatabaseProvider.Sqlite"/> is selected
/// (ADR-0001).
///
/// These handlers are global to the Dapper static cache, which is fine
/// because they round-trip values that are already stored as ISO-8601 /
/// invariant strings — the same shape any other provider would serialize
/// to TEXT if asked.
/// </summary>
internal static class SqliteDapperTypeHandlers
{
    private static int _registered;

    public static void RegisterOnce()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new NullableGuidHandler());
        SqlMapper.AddTypeHandler(new DateTimeHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeHandler());
        SqlMapper.AddTypeHandler(new DecimalHandler());
        SqlMapper.AddTypeHandler(new NullableDecimalHandler());
    }

    private sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) => value switch
        {
            Guid g => g,
            string s => Guid.Parse(s),
            byte[] b when b.Length == 16 => new Guid(b),
            _ => throw new DataException($"Cannot convert {value?.GetType()} to Guid.")
        };

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("D");
        }
    }

    private sealed class NullableGuidHandler : SqlMapper.TypeHandler<Guid?>
    {
        public override Guid? Parse(object value) => value switch
        {
            null or DBNull => null,
            Guid g => g,
            string s when string.IsNullOrEmpty(s) => null,
            string s => Guid.Parse(s),
            byte[] b when b.Length == 16 => new Guid(b),
            _ => throw new DataException($"Cannot convert {value.GetType()} to Guid?.")
        };

        public override void SetValue(IDbDataParameter parameter, Guid? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value?.ToString("D") ?? (object)DBNull.Value;
        }
    }

    private sealed class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override DateTime Parse(object value) => value switch
        {
            DateTime dt => dt,
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            _ => throw new DataException($"Cannot convert {value?.GetType()} to DateTime.")
        };

        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("o", CultureInfo.InvariantCulture);
        }
    }

    private sealed class NullableDateTimeHandler : SqlMapper.TypeHandler<DateTime?>
    {
        public override DateTime? Parse(object value) => value switch
        {
            null or DBNull => null,
            DateTime dt => dt,
            string s when string.IsNullOrEmpty(s) => null,
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            _ => throw new DataException($"Cannot convert {value.GetType()} to DateTime?.")
        };

        public override void SetValue(IDbDataParameter parameter, DateTime? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value?.ToString("o", CultureInfo.InvariantCulture) ?? (object)DBNull.Value;
        }
    }

    private sealed class DecimalHandler : SqlMapper.TypeHandler<decimal>
    {
        public override decimal Parse(object value) => value switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            long l => l,
            int i => i,
            string s => decimal.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert {value?.GetType()} to decimal.")
        };

        public override void SetValue(IDbDataParameter parameter, decimal value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private sealed class NullableDecimalHandler : SqlMapper.TypeHandler<decimal?>
    {
        public override decimal? Parse(object value) => value switch
        {
            null or DBNull => null,
            decimal d => d,
            double dbl => (decimal)dbl,
            long l => l,
            int i => i,
            string s when string.IsNullOrEmpty(s) => null,
            string s => decimal.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert {value.GetType()} to decimal?.")
        };

        public override void SetValue(IDbDataParameter parameter, decimal? value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value?.ToString(CultureInfo.InvariantCulture) ?? (object)DBNull.Value;
        }
    }
}
