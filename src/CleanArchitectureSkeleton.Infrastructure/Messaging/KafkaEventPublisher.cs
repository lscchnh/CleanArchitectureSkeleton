using Confluent.Kafka;

namespace CleanArchitectureSkeleton.Infrastructure.Messaging;

/// <summary>
/// Implémentation Confluent.Kafka de <see cref="IEventPublisher"/>.
/// Un seul topic <c>orders-events</c>, réduit à UNE SEULE partition (voir <see cref="DependencyInjection"/> :
/// le topic est créé avec <c>numPartitions: 1</c>) : tous les messages d'un même topic à partition unique
/// sont livrés et lus strictement dans l'ordre d'écriture (FIFO), ce qui est la condition nécessaire pour
/// que <see cref="OutboxConsumer"/> puisse garantir l'ordre de traitement sans logique supplémentaire.
/// La clé Kafka transporte le type d'événement (ex: "OrderCreatedDomainEvent"), l'en-tête "MessageId"
/// transporte l'Id du message Outbox d'origine (relu par le consumer pour l'idempotence).
/// </summary>
internal sealed class KafkaEventPublisher(IProducer<string, string> producer) : IEventPublisher, IAsyncDisposable
{
    private const string TopicName = "orders-events";

    public async Task PublishAsync(Guid messageId, string routingKey, string payload, CancellationToken cancellationToken)
    {
        var message = new Message<string, string>
        {
            // Clé = type d'événement : des messages de même clé sont toujours envoyés à la même partition
            // par Kafka (hash de la clé), ce qui serait utile si on ajoutait un jour plusieurs partitions
            // tout en gardant l'ordre PAR TYPE d'événement.
            Key = routingKey,
            Value = payload,
            Headers = [new Header("MessageId", System.Text.Encoding.UTF8.GetBytes(messageId.ToString()))],
        };

        await producer.ProduceAsync(TopicName, message, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        // Flush synchrone : s'assure que les messages en buffer sont bien envoyés avant l'arrêt du process.
        producer.Flush(TimeSpan.FromSeconds(10));
        producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
