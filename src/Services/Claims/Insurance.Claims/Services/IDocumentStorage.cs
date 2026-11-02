namespace Insurance.Claims.Services;

// Where the files themselves live. Swap the implementation in Program.cs to move to blob storage;
// nothing else in the service knows how files are stored.
public interface IDocumentStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct = default);

    // Null if there is no file under this key. The caller disposes the stream.
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
