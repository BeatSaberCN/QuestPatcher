using System;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Serilog;

namespace QuestPatcher.Core.Downgrading
{
    public static class HashUtil
    {
        public static Task<bool> CheckCrc32Async(string filePath, uint expected)
        {
            return Task.Factory.StartNew(() =>
            {
                Log.Debug("Checking CRC32 of {FilePath}", filePath);
                var crc = new Crc32();
                using var stream = File.OpenRead(filePath);
                crc.Append(stream);
                return expected == crc.GetCurrentHashAsUInt32();
            }, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);
        }

        /// <summary>
        /// Computes the SHA-256 hash of a file.
        /// </summary>
        /// <returns>The hash as a lowercase hex string</returns>
        public static async Task<string> GetSha256Async(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        }
    }
}
