using System.Collections.Generic;
using System.Linq;

namespace RailwayManager.Fleet
{
    /// <summary>Wynik walidacji konfiguracji miejsc.</summary>
    public class ValidationResult
    {
        public List<(ValidationSeverity sev, string message)> issues = new();
        public bool HasErrors => issues.Any(i => i.sev == ValidationSeverity.Error);
    }
}
