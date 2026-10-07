namespace CleanArchitectureSkeleton.Application.ProcessedMessages;

/// <summary>Projection en lecture seule d'un <c>ProcessedMessage</c>, destinée à l'affichage (encart Blazor).</summary>
public sealed record ProcessedMessageDto(Guid Id, string Type, string Content, DateTimeOffset ProcessedOnUtc);
