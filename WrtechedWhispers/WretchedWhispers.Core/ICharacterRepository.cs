namespace WretchedWhispers.Core;

public interface ICharacterRepository
{
    Task<Character.Character?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Character.Character c, CancellationToken ct = default);
}