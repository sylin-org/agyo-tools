using System.Collections.Generic;
using Koan.Data.Vector.Abstractions.Schema;

namespace Agyo.Service.Librarian.Models;

/// <summary>
/// Vector class schema for <see cref="LibraryDoc"/> (AGYO-0003). The property names match exactly the
/// metadata keys Agyo.Rag's file-ingest writes to <c>Vector&lt;LibraryDoc&gt;.Save</c> — so the Weaviate
/// class is created correctly and the chunk text + provenance round-trip through <c>VectorMatch.Metadata</c>.
/// </summary>
public sealed class LibraryDocVectorMetadata : IVectorMetadataDictionary
{
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "text", Searchable = true)]
    public string Text { get; init; } = "";

    [VectorProperty(VectorSchemaPropertyType.Text, Name = "document_id", Filterable = true)]
    public string DocumentId { get; init; } = "";

    [VectorProperty(VectorSchemaPropertyType.Text, Name = "parent_id")]
    public string ParentId { get; init; } = "";

    [VectorProperty(VectorSchemaPropertyType.Text, Name = "section")]
    public string Section { get; init; } = "";

    [VectorProperty(VectorSchemaPropertyType.Text, Name = "title")]
    public string Title { get; init; } = "";

    [VectorProperty(VectorSchemaPropertyType.Boolean, Name = "is_child")]
    public bool IsChild { get; init; }

    [VectorProperty(VectorSchemaPropertyType.Text, Name = "source_path", Filterable = true)]
    public string SourcePath { get; init; } = "";

    [VectorProperty(VectorSchemaPropertyType.Text, Name = "language", Filterable = true)]
    public string Language { get; init; } = "";

    public IReadOnlyDictionary<string, object?> ToDictionary() => new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = Text,
        ["document_id"] = DocumentId,
        ["parent_id"] = ParentId,
        ["section"] = Section,
        ["title"] = Title,
        ["is_child"] = IsChild,
        ["source_path"] = SourcePath,
        ["language"] = Language
    };
}
