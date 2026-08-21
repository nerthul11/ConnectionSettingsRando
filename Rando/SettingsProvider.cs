using System;

namespace ConnectionSettingsRando
{
    public interface ISettingsProvider
    {
        string Name { get; }
        RandomizationStats Randomize(Random rng);
    }

    internal class AutomatedSettingsProvider<T>(
        string name,
        Func<T> getter,
        Action<T> apply) : ISettingsProvider
        where T : new()
    {
        public string Name { get; } = name;
        public RandomizationStats Randomize(Random rng)
        {
            SettingsRandomizer randomizer = new();
            var (settings, stats) = randomizer.Randomize(getter(), rng, Name);
            apply(settings);
            return stats;
        }
    }

    internal class CustomSettingsProvider(
        string name,
        Func<Random, RandomizationStats> randomize) : ISettingsProvider
    {
        public string Name { get; } = name;
        public RandomizationStats Randomize(Random rng) => randomize(rng);
    }
}