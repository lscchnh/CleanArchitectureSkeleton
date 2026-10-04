using Microsoft.Data.Sqlite;

namespace CleanArchitectureSkeleton.Infrastructure.Resilience;

/// <summary>
/// Décide si une exception est TRANSITOIRE, c'est-à-dire susceptible de disparaître si on réessaie.
/// Réessayer une erreur permanente (violation de contrainte, bug SQL...) ne ferait que gaspiller des ressources.
/// </summary>
public static class TransientErrorDetector
{
    // Codes primaires SQLite : https://www.sqlite.org/rescode.html
    private const int SqliteBusy = 5;      // un autre writer détient le verrou
    private const int SqliteLocked = 6;    // conflit de verrou dans la même connexion partagée
    private const int SqliteIoError = 10;  // erreur disque/IO
    private const int SqliteCantOpen = 14; // fichier momentanément inaccessible

    public static bool IsTransient(Exception? exception)
    {
        // EF Core encapsule souvent l'erreur d'origine (DbUpdateException → SqliteException) : on remonte la chaîne.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException { SqliteErrorCode: SqliteBusy or SqliteLocked or SqliteIoError or SqliteCantOpen })
            {
                return true;
            }
        }

        return false;
    }
}
