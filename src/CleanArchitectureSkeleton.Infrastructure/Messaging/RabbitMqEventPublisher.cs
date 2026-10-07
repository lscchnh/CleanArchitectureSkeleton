using RabbitMQ.Client;

namespace CleanArchitectureSkeleton.Infrastructure.Messaging;

/// <summary>
/// Implémentation RabbitMQ.Client (v7, API async) de <see cref="IEventPublisher"/>.
/// Un seul exchange "topic" nommé <c>orders-events</c> : la routing key est le nom du type d'événement
/// (ex: "OrderCreatedDomainEvent"), ce qui permet à un futur consommateur de s'abonner à un sous-ensemble
/// via un pattern (ex: "Order*" ou "OrderShipped*") sans changer le code de publication.
/// La connexion est ouverte paresseusement et réutilisée (RabbitMQ.Client gère le multiplexage des canaux).
/// </summary>
internal sealed class RabbitMqEventPublisher(IConnection connection) : IEventPublisher, IAsyncDisposable
{
    private const string ExchangeName = "orders-events";

    public async Task PublishAsync(Guid messageId, string routingKey, string payload, CancellationToken cancellationToken)
    {
        // Un canal par publication : pas thread-safe à partager entre appels concurrents en v7.
        // C'est volontairement simple ici (projet pédagogique) ; un pool de canaux serait la version "production".
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

        var body = System.Text.Encoding.UTF8.GetBytes(payload);
        // MessageId = Id du message Outbox d'origine : c'est ce que l'OutboxConsumer relira pour
        // dédoublonner (voir ProcessedMessage). Persistent = true : le message survit à un redémarrage de RabbitMQ.
        var properties = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Persistent = true,
        };

        await channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();
}
