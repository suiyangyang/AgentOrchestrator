using System.IO;
using Avalonia.Media.Imaging;

namespace AgentOrchestrator.App.Models.Chat;

public sealed class ChatAttachment
{
    public ChatAttachment(string displayName, string path, bool isImage)
    {
        DisplayName = displayName;
        Path = path;
        IsImage = isImage;
        Thumbnail = LoadThumbnail(path, isImage);
    }

    public string DisplayName { get; }

    public string Path { get; }

    public bool IsImage { get; }

    public Bitmap? Thumbnail { get; }

    private static Bitmap? LoadThumbnail(string path, bool isImage)
    {
        if (!isImage || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}