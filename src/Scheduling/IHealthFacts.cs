namespace Agyo.Scheduling;

public interface IHealthFacts
{
    IReadOnlyDictionary<string, string> GetFacts();
}
