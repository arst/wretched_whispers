namespace WretchedWhispers.Api.Models;

public record ChatMessageDto(string Role, string? Content, string? AuthorName);
