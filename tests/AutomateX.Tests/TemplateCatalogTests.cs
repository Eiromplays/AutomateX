using AutomateX.Modules.Templates;
using Xunit;

namespace AutomateX.Tests;

public sealed class TemplateCatalogTests
{
    [Fact]
    public void Parses_inline_template_docs()
    {
        const string json = """
            {"templates":[
              {"name":"RSS alert","description":"d","category":"Alerts","doc":{"automatex":1,"name":"x","steps":[]}}
            ]}
            """;

        var entry = Assert.Single(TemplateCatalog.Parse(json));

        Assert.Equal("RSS alert", entry.Name);
        Assert.Equal("Alerts", entry.Category);
        Assert.NotNull(entry.Doc["automatex"]);
    }

    [Fact]
    public void Rejects_entry_missing_doc()
    {
        Assert.Throws<InvalidOperationException>(() => TemplateCatalog.Parse("""{"templates":[{"name":"x"}]}"""));
    }

    [Fact]
    public void Rejects_non_json()
    {
        Assert.Throws<InvalidOperationException>(() => TemplateCatalog.Parse("not json"));
    }
}
