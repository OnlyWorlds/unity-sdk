using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace OnlyWorlds.Sdk.Tests.Integration
{
    /// <summary>
    /// An image and a file through the real hosts, from Unity's own transport: uploaded, read back from where they are
    /// served, and removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the fakes cannot show is the request <c>UnityWebRequest</c> builds from raw bytes: the client sets no
    /// Content-Type, and the upload handler may add one of its own. The hosts read the type from the bytes, so the
    /// verdict is the type the host answers and the bytes it serves back.
    /// </para>
    /// <para>
    /// These write. They need a write key and its PIN on a world made for the run (<c>OW_LIVE_WRITE_KEY</c>,
    /// <c>OW_LIVE_WRITE_PIN</c>), with at least one Character for the file, and they remove what they upload, also
    /// when an assertion fails. Never point them at a world anyone keeps.
    /// </para>
    /// </remarks>
    [Category("Integration")]
    [Explicit("Needs a write key on a throwaway world and a network: uploads to the real hosts, then removes them.")]
    public class LiveMediaTests
    {
        private static string Key => Environment.GetEnvironmentVariable("OW_LIVE_WRITE_KEY");
        private static string Pin => Environment.GetEnvironmentVariable("OW_LIVE_WRITE_PIN");

        // One transparent pixel: the smallest PNG a host will take.
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        private static OWClient MakeClient()
        {
            if (string.IsNullOrEmpty(Key)) Assert.Ignore("OW_LIVE_WRITE_KEY not set -- skipping live media tests.");

            return new OWClient(new OWClientConfig
            {
                ApiKey = Key,
                ApiPin = Pin,
                Transport = new UnityWebRequestTransport(),
            });
        }

        private static async Task<byte[]> Served(string url)
        {
            using (var http = new HttpClient())
            {
                return await http.GetByteArrayAsync(url);
            }
        }

        [Test]
        public async Task Image_UploadedFromRawBytes_IsReadAsPng_ServedBack_AndRemoved()
        {
            var client = MakeClient();
            var uploaded = await client.UploadImageAsync(Png);
            TestContext.WriteLine($"uploaded: {uploaded.Type}, {uploaded.Bytes} bytes");

            try
            {
                Assert.AreEqual("image/png", uploaded.Type, "the host's reading of the bytes");
                Assert.AreEqual(Png.Length, uploaded.Bytes);
                StringAssert.StartsWith("https://", uploaded.Url);
                CollectionAssert.AreEqual(Png, await Served(uploaded.Url), "the bytes as served");
            }
            finally
            {
                var removed = await client.RemoveImageAsync(uploaded.Url);
                TestContext.WriteLine($"removed: {removed.Bytes} bytes back, cache {removed.Cache}");
            }
        }

        [Test]
        public async Task File_UploadedFromRawBytes_IsListed_DownloadedBack_AndRemoved()
        {
            var client = MakeClient();
            var characters = await client.ListAsync<OWCharacter>("character");
            if (characters.Data.Count == 0) Assert.Ignore("the world has no Character to hold the file.");
            var holder = characters.Data[0].Id;

            var text = Encoding.UTF8.GetBytes("A file from Unity's transport, " + DateTime.UtcNow.ToString("O") + "\n");
            var uploaded = await client.UploadFileAsync("character", holder, text, "unity-live-test.txt");
            TestContext.WriteLine($"uploaded: {uploaded.Type}, {uploaded.Bytes} bytes");

            try
            {
                Assert.AreEqual(text.Length, uploaded.Bytes);
                Assert.IsFalse(string.IsNullOrEmpty(uploaded.Fid), "the host names the file");

                var listed = await client.ListFilesAsync("character", holder);
                Assert.IsTrue(listed.Any(f => f.Id == uploaded.Fid), "the element lists the new file by the host's id");

                var link = await client.FileDownloadLinkAsync("character", holder, uploaded.Fid);
                CollectionAssert.AreEqual(text, await Served(link.Url), "the bytes as downloaded");
            }
            finally
            {
                await client.RemoveFileAsync("character", holder, uploaded.Fid);
            }
        }
    }
}
