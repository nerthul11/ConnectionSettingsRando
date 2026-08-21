using System;

namespace ConnectionSettingsRando
{
    public static class CSR
    {
        private static readonly RandomizationPipeline pipeline = new();
        public static void Register<T>(
            string name,
            Func<T> getter,
            Action<T> overrideSettings)
            where T : new()
        {
            ConnectionsRegistry.Register(new AutomatedSettingsProvider<T>(name, getter, overrideSettings));
        }
        public static void Register(
            string name,
            Func<Random, RandomizationStats> randomize)
        {
            ConnectionsRegistry.Register(new CustomSettingsProvider(name, randomize));
        }
        public static void RandomizeAll(Random rng)
        {
            pipeline.RandomizeAll(rng);
        }
    }
    
}