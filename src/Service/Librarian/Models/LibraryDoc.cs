using Koan.Data.Core.Model;
using Koan.Data.Vector.Abstractions.Schema;

namespace Agyo.Service.Librarian.Models;

/// <summary>
/// The Rag corpus entity for a project's code + docs (AGYO-0003). A repository is ingested into
/// <c>Rag.Corpus&lt;LibraryDoc&gt;()</c> under <c>EntityContext.Partition(projectId)</c> — one vector class
/// per project. The chunks (text + provenance) live in the vector store; <c>LibraryDoc</c> is just the
/// corpus type binding (Rag's file-ingest path keys chunks by file path, not by LibraryDoc rows).
/// </summary>
[VectorSchema(typeof(LibraryDocVectorMetadata), EntityName = "LibrarianDocVector")]
public sealed class LibraryDoc : Entity<LibraryDoc>
{
}
