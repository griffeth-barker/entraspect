using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Entraspect.App.Services;

namespace Entraspect.App.Tests;

/// <summary>
/// Records what view models ask of the window. Never opens a browser, touches the real clipboard or changes the theme.
/// </summary>
internal sealed class FakeUserInteraction : IUserInteraction
{
    public string? Password { get; set; }

    public int Calls { get; private set; }

    public bool ConfirmResult { get; set; } = true;

    public int Confirmations { get; private set; }

    public List<string> CopiedText { get; } = [];

    public List<string> OpenedUrls { get; } = [];

    public Task<string?> RequestPasswordAsync(string profileName)
    {
        Calls++;
        return Task.FromResult(Password);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Confirmations++;
        return Task.FromResult(ConfirmResult);
    }

    public Task CopyTextAsync(string text)
    {
        CopiedText.Add(text);
        return Task.CompletedTask;
    }

    public Task OpenUrlAsync(string url)
    {
        OpenedUrls.Add(url);
        return Task.CompletedTask;
    }

    /// <summary>What the save dialog returns; null acts as Cancel.</summary>
    public ExportFile? ExportFile { get; set; }

    public List<string> SuggestedFileNames { get; } = [];

    public Task<ExportFile?> PickExportFileAsync(string suggestedFileName)
    {
        SuggestedFileNames.Add(suggestedFileName);
        return Task.FromResult(ExportFile);
    }

    /// <summary>What opening a file returns; false acts as "no app could open it".</summary>
    public bool OpenFileResult { get; set; } = true;

    /// <summary>Every file opened, with whether it existed at that moment. Nothing is actually opened.</summary>
    public List<(string Path, bool Existed)> OpenedFiles { get; } = [];

    public Task<bool> OpenFileAsync(string path)
    {
        OpenedFiles.Add((path, File.Exists(path)));
        return Task.FromResult(OpenFileResult);
    }

    /// <summary>Every theme applied, in order.</summary>
    public List<AppTheme> Themes { get; } = [];

    public void SetTheme(AppTheme theme) => Themes.Add(theme);
}
