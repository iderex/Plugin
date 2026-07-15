using System.Collections.Generic;

namespace Emby.Plugins.Moonfin.Models
{
    public sealed class MoonfinThemeValidationResult
    {
        public MoonfinThemeValidationResult(bool isValid, IReadOnlyList<string> errors, string themeId, string displayName)
        {
            IsValid = isValid;
            Errors = errors;
            ThemeId = themeId;
            DisplayName = displayName;
        }

        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }
        public string ThemeId { get; }
        public string DisplayName { get; }
    }
}
