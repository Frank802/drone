using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace Drone.Helpers
{
    public static class BlobStorageService
    {
        public static CloudStorageAccount StorageAccount;
        public static CloudBlobClient BlobClient;
        public static string StorageAccountName = "YOUR_STORAGE_ACCOUNT_NAME";
        public static string StorageAccountKey = "YOUR_STORAGE_ACCOUNT_KEY";
        public static bool isInitialized = false;

        private static void Init()
        {
            if (!isInitialized)
            {
                var credentials = new StorageCredentials(StorageAccountName, StorageAccountKey);
                StorageAccount = new CloudStorageAccount(credentials, true);
                BlobClient = StorageAccount.CreateCloudBlobClient();

                isInitialized = true;
            }
        }

        public static async Task<List<string>> GetBlobList(string containerName)
        {
            if (!isInitialized)
                Init();

            var list = new List<string>();
            CloudBlobContainer container = BlobClient.GetContainerReference(containerName);
            BlobContinuationToken token = null;

            do
            {
                BlobResultSegment resultSegment = await container.ListBlobsSegmentedAsync("", true, BlobListingDetails.All, 10, token, null, null);
                token = resultSegment.ContinuationToken;

                foreach (IListBlobItem item in resultSegment.Results)
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob blob = (CloudBlockBlob)item;
                        if (await blob.ExistsAsync())
                            list.Add(blob.Name);
                    }
                }
            } while (token != null);

            return list;
        }

        public static async Task UploadData(string containerName, byte[] bytes)
        {
            if (!isInitialized)
                Init();

            CloudBlobContainer container = BlobClient.GetContainerReference(containerName);
            CloudBlockBlob blockBlob = container.GetBlockBlobReference($"frame_{DateTime.UtcNow.ToString("O")}.jpg");
            await blockBlob.UploadFromByteArrayAsync(bytes, 0, bytes.Length);
        }

        public static async Task<Stream> DownloadData(string containerName)
        {
            if (!isInitialized)
                Init();

            MemoryStream stream = new MemoryStream();
            var list = await GetBlobList(containerName);

            if (!string.IsNullOrWhiteSpace(list.LastOrDefault()))
            {
                CloudBlobContainer container = BlobClient.GetContainerReference(containerName);
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(list.LastOrDefault());
                await blockBlob.DownloadToStreamAsync(stream);
            }

            return stream;
        }
    }
}
