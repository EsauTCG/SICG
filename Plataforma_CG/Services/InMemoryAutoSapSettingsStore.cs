using Plataforma_CG.Models;

namespace Plataforma_CG.Services
{
    public class InMemoryAutoSapSettingsStore : IAutoSapSettingsStore
    {
        private static readonly object _lock = new();

        // settings por planta
        private static readonly Dictionary<string, AutoSapSettings> _bySource =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["P1"] = new AutoSapSettings { Enabled = false, Source = "P1", IntervalMs = 5000 },
                ["TIF"] = new AutoSapSettings { Enabled = false, Source = "TIF", IntervalMs = 5000 },
            };

        public AutoSapSettings Get(string source)
        {
            source = Normalize(source);
            lock (_lock)
            {
                var s = _bySource[source];
                return new AutoSapSettings { Enabled = s.Enabled, Source = s.Source, IntervalMs = s.IntervalMs };
            }
        }

        public List<AutoSapSettings> GetAll()
        {
            lock (_lock)
            {
                return _bySource.Values
                    .Select(s => new AutoSapSettings { Enabled = s.Enabled, Source = s.Source, IntervalMs = s.IntervalMs })
                    .ToList();
            }
        }

        public void Set(AutoSapSettings s)
        {
            var source = Normalize(s.Source);
            lock (_lock)
            {
                _bySource[source] = new AutoSapSettings
                {
                    Enabled = s.Enabled,
                    Source = source,
                    IntervalMs = s.IntervalMs <= 0 ? 5000 : s.IntervalMs
                };
            }
        }

        private static string Normalize(string source)
        {
            var x = (source ?? "P1").Trim().ToUpper();
            return x == "TIF" ? "TIF" : "P1";
        }
    }
}
