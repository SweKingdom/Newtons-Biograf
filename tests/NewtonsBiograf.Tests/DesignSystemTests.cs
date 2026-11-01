using System.Text.RegularExpressions;

namespace NewtonsBiograf.Tests;

public class DesignSystemTests
{
    private static readonly string ClientDir = FindClientDir();

    private static string FindClientDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewtonsBiograf.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Solution root not found."),
            "src", "NewtonsBiograf.Client");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([ClientDir, .. parts]));

    [Fact]
    public void Ac1_AppCss_defines_design_tokens_on_root()
    {
        var css = Read("wwwroot", "css", "app.css");
        var root = Regex.Match(css, @":root\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline).Groups["body"].Value;

        string[] tokens =
        [
            "--color-bg", "--color-surface", "--color-primary", "--color-accent", "--color-text", "--color-muted",
            "--color-danger", "--color-success", "--font-body", "--font-heading",
            "--space-1", "--space-2", "--space-3", "--space-4", "--space-5", "--space-6", "--radius", "--shadow"
        ];
        foreach (var token in tokens)
            Assert.Contains(token + ":", root);
        Assert.DoesNotContain("http", css, StringComparison.OrdinalIgnoreCase); // no external fonts or assets
    }

    [Fact]
    public void Ac1_MainLayout_has_header_top_nav_footer_and_collapsing_toggle_without_sidebar()
    {
        var layout = Read("Layout", "MainLayout.razor");

        Assert.Contains("<header", layout);
        Assert.Contains("<nav", layout);
        Assert.Contains("<main", layout);
        Assert.Contains("@Body", layout);
        Assert.Contains("<footer", layout);
        Assert.Contains("aria-expanded", layout);
        Assert.DoesNotContain("sidebar", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-width: 640px", Read("Layout", "MainLayout.razor.css"));
    }

    [Fact]
    public void Ac1_IndexHtml_links_own_css_and_no_bootstrap()
    {
        var html = Read("wwwroot", "index.html");

        Assert.Contains("css/app.css", html);
        Assert.Contains("NewtonsBiograf.Client.styles.css", html);
        Assert.DoesNotContain("bootstrap", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("AppButton", "Variant", "OnClick", "Disabled", "Type")]
    [InlineData("FormField", "Label", "Id", "Error", "role=\"alert\"")]
    [InlineData("AlertBox", "Kind", "ChildContent", "role=", "Error")]
    [InlineData("Spinner", "role=\"status\"", "aria-label=\"Loading\"")]
    [InlineData("Card", "Title", "ChildContent")]
    public void Ac2_Shared_components_exist_with_their_parameters(string component, params string[] expected)
    {
        var path = Path.Combine(ClientDir, "Components", component + ".razor");
        Assert.True(File.Exists(path), $"{component}.razor is missing");

        var source = File.ReadAllText(path);
        foreach (var fragment in expected)
            Assert.Contains(fragment, source);
    }

    [Fact]
    public void Ac2_FormField_binds_label_to_input_with_for()
    {
        Assert.Contains("<label for=\"@Id\">", Read("Components", "FormField.razor"));
    }

    [Fact]
    public void Ac3_Home_and_template_leftovers_are_gone()
    {
        var home = Read("Pages", "Home.razor");

        Assert.DoesNotContain("Hello, world", home, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(ClientDir, "Layout", "NavMenu.razor")));
        Assert.False(Directory.Exists(Path.Combine(ClientDir, "wwwroot", "css", "bootstrap")));
    }
}
