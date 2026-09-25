using System.Collections.Generic;
using System.Threading;

namespace PickNBook.Api.Services.Interfaces
{
    public interface ISrdvSqlDumpParser
    {
        IAsyncEnumerable<Dictionary<string, string?>> ParseInsertRowsAsync(
            string sqlFilePath,
            string expectedTableName,
            CancellationToken cancellationToken = default);
    }
}
