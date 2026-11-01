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

    [Fact]
    public void US123_Ac2_Staff_page_is_admin_only_and_offers_all_three_permissions_for_create_and_edit()
    {
        var page = Read("Pages", "Admin", "Staff.razor");

        Assert.Contains("@page \"/admin/staff\"", page);
        Assert.Contains("[Authorize(Roles = Roles.Admin)]", page);
        Assert.Equal(2, Regex.Matches(page, @"in Permissions\.All").Count);
        Assert.Contains("GetAsync(\"api/admin/staff\")", page);
        Assert.Contains("PostAsJsonAsync(\"api/admin/staff\"", page);
        Assert.Contains("PutAsJsonAsync(", page);
    }

    [Fact]
    public void US123_Ac3_Router_guards_pages_and_menu_links_depend_on_the_role()
    {
        var app = Read("App.razor");
        var layout = Read("Layout", "MainLayout.razor");

        Assert.Contains("<AuthorizeRouteView", app);
        Assert.Contains("<AccessRedirect", app);
        Assert.Contains("<AuthorizeView Roles=\"@Roles.Admin\">", layout);
        Assert.Contains("Log out", layout);
        Assert.True(File.Exists(Path.Combine(ClientDir, "Pages", "NoAccess.razor")));
        Assert.Contains("@page \"/no-access\"", Read("Pages", "NoAccess.razor"));
    }

    [Fact]
    public void US125_Ac1_Movies_page_is_guarded_by_ManageMovies_lists_movies_and_the_menu_entry_uses_the_policy()
    {
        var page = Read("Pages", "Staff", "Movies.razor");

        Assert.Contains("@page \"/staff/movies\"", page);
        Assert.Contains("[Authorize(Policy = Permissions.ManageMovies)]", page);
        Assert.Contains("<MovieForm", page);
        Assert.Contains("No movies yet. Add the first one.", page);
        Assert.Contains("ToDisplayName()", page);
        Assert.Contains("role=\"alertdialog\"", page);
        Assert.DoesNotContain("window.confirm", page);

        var layout = Read("Layout", "MainLayout.razor");
        var entry = layout.IndexOf("href=\"staff/movies\"", StringComparison.Ordinal);
        Assert.True(entry > layout.LastIndexOf("<AuthorizeView Policy=\"@Permissions.ManageMovies\">", entry, StringComparison.Ordinal) &&
                    layout.LastIndexOf("<AuthorizeView Policy=\"@Permissions.ManageMovies\">", entry, StringComparison.Ordinal) >= 0);

        var program = Read("Program.cs");
        Assert.Contains("foreach (var permission in Permissions.All)", program);
        Assert.Contains("AddScoped<MovieApiClient>", program);
    }

    [Fact]
    public void US125_Ac1_Movie_form_has_the_five_fields_with_full_dropdowns_and_html_limits()
    {
        var form = Read("Pages", "Staff", "MovieForm.razor");

        Assert.Contains("Enum.GetValues<Genre>()", form);
        Assert.Contains("Enum.GetValues<AgeRating>()", form);
        Assert.Equal(2, Regex.Matches(form, "<option value=\"\">Select…</option>").Count);
        Assert.Contains("id=\"movie-title\" type=\"text\" maxlength=\"200\"", form);
        Assert.Contains("type=\"number\" min=\"1\" max=\"400\" step=\"1\"", form);
        Assert.Contains("<textarea id=\"movie-description\" rows=\"5\" maxlength=\"2000\"", form);
        foreach (var field in new[] { "Title", "Genre", "AgeRating", "LengthMinutes", "Description" })
            Assert.Contains($"Errors?.Field(\"{field}\")", form);
    }

    [Fact]
    public void US128_Ac1_Auditorium_list_shows_the_columns_and_the_form_has_the_three_fields_with_field_errors()
    {
        var list = Read("Pages", "Admin", "Auditoriums.razor");
        var form = Read("Pages", "Admin", "AuditoriumForm.razor");

        Assert.Contains("@page \"/admin/auditoriums\"", list);
        Assert.Contains("<th>Name</th><th>Rows</th><th>Seats per row</th><th>Capacity</th>", list);
        Assert.Contains("@page \"/admin/auditoriums/new\"", form);
        Assert.Contains("@page \"/admin/auditoriums/{Id:int}\"", form);
        foreach (var field in new[] { "Name", "Rows", "SeatsPerRow" })
            Assert.Contains($"formProblem?.Field(\"{field}\")", form);
        Assert.Contains("Nav.NavigateTo(\"admin/auditoriums\")", form);
        Assert.Contains("AddScoped<AuditoriumApiClient>", Read("Program.cs"));
    }

    [Fact]
    public void US128_Ac2_Seat_grid_has_labelled_focusable_row_buttons_and_the_form_reverts_a_failed_toggle()
    {
        var grid = Read("Components", "SeatGrid.razor");
        var form = Read("Pages", "Admin", "AuditoriumForm.razor");

        Assert.Contains("<button type=\"button\" class=\"seat-row-label @typeClass\" aria-label=\"Row @r @typeName\"", grid);
        Assert.Contains("\"VIP\" : \"Standard\"", grid);
        Assert.Contains("<small>@typeName</small>", grid);
        Assert.Contains("IReadOnlySet<int> VipRows", grid);
        Assert.Contains("<SeatGrid", form);
        Assert.Contains("SetRowTypeAsync(id, row,", form);
        Assert.Contains("SetVip(row, !makeVip);", form);
        Assert.Contains("rowProblem", form);
    }

    [Fact]
    public void US128_Ac2_Seat_colours_are_css_variables_and_wide_grids_scroll_sideways()
    {
        var css = Read("wwwroot", "css", "app.css");

        Assert.Contains("--seat-standard:", css);
        Assert.Contains("--seat-vip:", css);
        Assert.Contains("background: var(--seat-vip)", css);
        Assert.Matches(@"\.seat-grid-scroll\s*\{\s*overflow-x: auto;", css);
    }

    [Fact]
    public void US128_Ac3_Form_shows_the_read_only_prices_from_the_pricing_endpoint()
    {
        var form = Read("Pages", "Admin", "AuditoriumForm.razor");

        Assert.Contains("GetPricesAsync()", form);
        Assert.Contains("prices.StandardSeat", form);
        Assert.Contains("prices.VipSeat", form);
        Assert.Contains("\"api/pricing\"", Read("Services", "AuditoriumApiClient.cs"));
    }

    [Fact]
    public void US128_Ac5_Pages_are_admin_only_and_the_nav_entry_sits_in_the_admin_block()
    {
        Assert.Contains("[Authorize(Roles = Roles.Admin)]", Read("Pages", "Admin", "Auditoriums.razor"));
        Assert.Contains("[Authorize(Roles = Roles.Admin)]", Read("Pages", "Admin", "AuditoriumForm.razor"));

        var layout = Read("Layout", "MainLayout.razor");
        var entry = layout.IndexOf("href=\"admin/auditoriums\"", StringComparison.Ordinal);
        var block = layout.LastIndexOf("<AuthorizeView Roles=\"@Roles.Admin\">", entry, StringComparison.Ordinal);
        Assert.True(entry > block && block >= 0);
        Assert.True(layout.IndexOf("</AuthorizeView>", block, StringComparison.Ordinal) > entry);
    }

    [Fact]
    public void US128_Ac6_Pages_show_a_spinner_an_empty_state_and_an_error_alert()
    {
        var list = Read("Pages", "Admin", "Auditoriums.razor");
        var form = Read("Pages", "Admin", "AuditoriumForm.razor");

        Assert.Contains("<Spinner />", list);
        Assert.Contains("No auditoriums yet. Add the first one.", list);
        Assert.Contains("AlertBox.AlertKind.Error", list);
        Assert.Contains("<Spinner />", form);
        Assert.Contains("loadProblem", form);
    }
}
