namespace ASC.ZoomService.Helpers
{
    public static class ZoomConfigurationHelper
    {
        public static string ConfigurationSectionToString(IConfigurationSection section)
        {
            var items = section.GetChildren();
            return ConfigurationChildrensToString(items);
        }

        private static string ConfigurationChildrensToString(IEnumerable<IConfigurationSection> sections, int indent = 0)
        {
            var sb = new StringBuilder();
            var prefix = indent > 0 ? new string('\t', indent) : "";
            foreach (var section in sections)
            {
                if (!section.GetChildren().Any())
                {
                    sb.AppendLine($"{prefix}{section.Key} = {section.Value}");
                }
                else
                {
                    sb.AppendLine($"{prefix}{section.Key}");
                    sb.Append(ConfigurationChildrensToString(section.GetChildren(), indent + 1));
                }
            }
            return sb.ToString();
        }
    }
}
