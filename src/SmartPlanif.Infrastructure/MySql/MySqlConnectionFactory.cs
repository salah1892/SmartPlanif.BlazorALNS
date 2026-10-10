using System.Data.Common;
using MySqlConnector;

namespace SmartPlanif.Infrastructure.MySql;

/// <summary>Fabrique de connexions ouvertes vers la base Transport_Ai.</summary>
public interface IDbConnectionFactory
{
    /// <summary>Ouvre une nouvelle connexion (à libérer par l'appelant).</summary>
    Task<DbConnection> OpenAsync(CancellationToken ct);
}

/// <summary>Implémentation MySQL de <see cref="IDbConnectionFactory"/>.</summary>
public sealed class MySqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    /// <inheritdoc />
    public async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
