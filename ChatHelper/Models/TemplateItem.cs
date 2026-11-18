namespace ChatHelper.Models;

public class TemplateItem
{
    public string Keyword { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "일반";

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var terms = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var target = $"{Keyword} {Title} {Category} {Content}".ToLower();

        return terms.All(t => target.Contains(t));
    }
}
