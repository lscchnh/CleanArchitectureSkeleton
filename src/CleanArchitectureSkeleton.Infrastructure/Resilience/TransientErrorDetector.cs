using Npgsql;

namespace CleanArchitectureSkeleton.Infrastructure.Resilience;

/// <summary>
/// Décide si une exception est TRANSITOIRE, c'est-à-dire susceptible de disparaître si on réessaie.
/// Réessayer une erreur permanente (violation de contrainte, bug SQL...) ne ferait que gaspiller des ressources.
/// </summary>
public static class TransientErrorDetector
{
    // Codes d'erreur SQLSTATE PostgreSQL : https://www.postgresql.org/docs/current/errcodes-appendix.html
    private const string SerializationFailure = "40001"; // conflit de sérialisation (isolation SERIALIZABLE/REPEATABLE READ)
    private const string DeadlockDetected = "40P01";     // deadlock détecté entre deux transactions
    private const string TooManyConnections = "53300";   // pool/serveur saturé, momentanément indisponible
    private const string ConnectionException = "08006";  // connexion perdue
    private const string ConnectionDoesNotExist = "08003";
    private const string ConnectionFailure = "08001";
    private const string SqlClientUnableToEstablishConnection = "08004";

    public static bool IsTransient(Exception? exception)
    {
        // EF Core encapsule souvent l'erreur d'origine (DbUpdateException → PostgresException) : on remonte la chaîne.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: SerializationFailure or DeadlockDetected or TooManyConnections
                or ConnectionException or ConnectionDoesNotExist or ConnectionFailure or SqlClientUnableToEstablishConnection })
            {
                return true;
            }

            // Erreurs réseau/socket brutes (ex: conteneur Docker pas encore prêt) remontées par Npgsql.
            if (current is NpgsqlException { InnerException: System.Net.Sockets.SocketException })
            {
                return true;
            }
        }

        return false;
    }
}
