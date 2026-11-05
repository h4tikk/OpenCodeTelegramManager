namespace OpenCodeManager.Application;
public sealed record PromptRequest(string Id, string Text, Dictionary<string, string> Metadata, List<OpenCodeFileInput>? Files = null);
