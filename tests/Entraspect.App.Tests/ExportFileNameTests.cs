using Entraspect.App.Services;
using Entraspect.Core;

namespace Entraspect.App.Tests;

public sealed class ExportFileNameTests
{
    [Theory]
    [InlineData("Contoso", ExportFormat.Csv, "entraspect-Contoso-20260115-1200.csv")]
    [InlineData("Contoso", ExportFormat.Json, "entraspect-Contoso-20260115-1200.json")]
    [InlineData("Contoso prod", ExportFormat.Csv, "entraspect-Contoso prod-20260115-1200.csv")]
    [InlineData("a<b>c:d\"e/f\\g|h?i*j", ExportFormat.Csv, "entraspect-abcdefghij-20260115-1200.csv")]
    [InlineData("tab\tnew\nline", ExportFormat.Csv, "entraspect-tabnewline-20260115-1200.csv")]
    [InlineData("Zoë's tenant", ExportFormat.Csv, "entraspect-Zoë's tenant-20260115-1200.csv")]
    [InlineData("ends with dots. ", ExportFormat.Csv, "entraspect-ends with dots-20260115-1200.csv")]
    public void Removes_characters_not_valid_in_file_names(string profile, ExportFormat format, string expected)
    {
        Assert.Equal(expected, ExportFileName.For(profile, "20260115-1200", format));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/:*?")]
    public void Profile_with_nothing_left_is_left_out(string profile)
    {
        Assert.Equal("entraspect-20260115-1200.csv", ExportFileName.For(profile, "20260115-1200", ExportFormat.Csv));
    }

    [Theory]
    [InlineData("scan.json", ExportFormat.Csv, ExportFormat.Json)]
    [InlineData("scan.JSON", ExportFormat.Csv, ExportFormat.Json)]
    [InlineData("scan.csv", ExportFormat.Json, ExportFormat.Csv)]
    [InlineData("scan", ExportFormat.Json, ExportFormat.Json)]
    [InlineData("scan.txt", ExportFormat.Csv, ExportFormat.Csv)]
    public void Format_follows_the_extension_then_the_chosen_type(string name, ExportFormat chosen, ExportFormat expected)
    {
        Assert.Equal(expected, ExportFileName.FormatFor(name, chosen));
    }
}
