using Microsoft.Extensions.Options;
using Calendar.Data;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public sealed class TaskAttachmentStorageOptions
{
    public const string SectionName = "TaskAttachments";
    public string RootPath { get; set; } = "App_Data/TaskAttachments";
}

public interface ITaskAttachmentStorage
{
    Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed class LocalTaskAttachmentStorage : ITaskAttachmentStorage
{
    private readonly string rootPath;

    public LocalTaskAttachmentStorage(
        IOptions<TaskAttachmentStorageOptions> options,
        IWebHostEnvironment environment)
    {
        var configuredPath = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("Task attachment storage path is required.");

        rootPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath));
        Directory.CreateDirectory(rootPath);
    }

    public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
    {
        var destination = Resolve(storageKey);
        var temporary = destination + ".upload-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) ||
            storageKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            storageKey.Contains(Path.DirectorySeparatorChar) ||
            storageKey.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("Invalid task attachment storage key.");
        }

        var resolved = Path.GetFullPath(Path.Combine(rootPath, storageKey));
        if (!resolved.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid task attachment storage key.");
        return resolved;
    }
}

public sealed record TaskAttachmentDownload(Stream Content, string ContentType, string FileName);

public sealed class TaskAttachmentAccessService(
    IDbContextFactory<CalendarDbContext> dbFactory,
    ITaskAttachmentStorage storage)
{
    public async Task<TaskAttachmentDownload?> OpenAsync(
        Guid attachmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Users.AsNoTracking().AnyAsync(user => user.Id == currentUserId, cancellationToken))
            return null;

        var attachment = await db.TaskAttachments.AsNoTracking()
            .Where(item => item.Id == attachmentId)
            .Select(item => new { item.StorageKey, item.ContentType, item.OriginalFileName })
            .SingleOrDefaultAsync(cancellationToken);
        if (attachment is null) return null;

        try
        {
            var content = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
            return new TaskAttachmentDownload(content, attachment.ContentType, attachment.OriginalFileName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
