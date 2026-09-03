using System.Threading;
using System.Threading.Tasks;

namespace PickNBook.Api.Services.Interfaces
{
    public interface ISrdvMasterDataDownloader
    {
        Task<string> DownloadAndExtractAsync(string resourceUrl, string stagingSubdir, CancellationToken cancellationToken = default);
        Task CleanupStagingAsync(string stagingSubdir);
    }
}
