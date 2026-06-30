using Microsoft.Data.SqlClient;

namespace KingCom.Infrastructure.Data;

public sealed class SqlConnectionFactory(string connectionString)
{
    public SqlConnection Create()
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:DefaultConnection.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!builder.ContainsKey("Application Intent"))
        {
            builder.ApplicationIntent = ApplicationIntent.ReadOnly;
        }

        return new SqlConnection(builder.ConnectionString);
    }
}

public sealed class AuthSqlConnectionFactory(string connectionString)
{
    public SqlConnection Create()
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Chưa cấu hình ConnectionStrings:AuthConnection.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        return new SqlConnection(builder.ConnectionString);
    }
}
