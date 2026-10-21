namespace WfWasm.Rendering.Gles;

// Milestone 1 has no text service. Every entry point reports that explicitly;
// shaping and glyph drawing arrive in a later milestone with a real font backend.
internal sealed class GlesTextService : ITextService
{
    internal const string Reason = "GLES milestone 1 has no text service. Shaped-text commands are reported as unsupported at enqueue time.";

    public ValueTask<FontHandle> RegisterFontAsync(string family, ReadOnlyMemory<byte> fontBytes, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<FontHandle>(new NotSupportedException(Reason));

    public void Release(FontHandle font) => throw new NotSupportedException(Reason);

    public ShapedText Shape(TextRequest request) => throw new NotSupportedException(Reason);
}
