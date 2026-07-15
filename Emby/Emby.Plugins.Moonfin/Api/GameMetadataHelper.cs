using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Emby.Plugins.Moonfin.Api
{
    /// <summary>
    /// Keyless game metadata via the libretro <c>.rdb</c> databases. Files are fetched lazily
    /// per system from the configured base (jsDelivr CDN by default) and cached under the plugin
    /// data folder. A system's file only downloads when a game from it is opened. The ROM is
    /// hashed server-side and matched by CRC (then filename/title). Static because the Emby
    /// GamesScanner is created per request.
    /// </summary>
    internal static class GameMetadataHelper
    {
        private static readonly Dictionary<string, string> CoreToPlatform =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nes"] = "Nintendo - Nintendo Entertainment System",
                ["snes"] = "Nintendo - Super Nintendo Entertainment System",
                ["gb"] = "Nintendo - Game Boy",
                ["gba"] = "Nintendo - Game Boy Advance",
                ["n64"] = "Nintendo - Nintendo 64",
                ["nds"] = "Nintendo - Nintendo DS",
                ["vb"] = "Nintendo - Virtual Boy",
                ["segaMD"] = "Sega - Mega Drive - Genesis",
                ["segaMS"] = "Sega - Master System - Mark III",
                ["segaGG"] = "Sega - Game Gear",
                ["atari2600"] = "Atari - 2600",
                ["atari7800"] = "Atari - 7800",
                ["lynx"] = "Atari - Lynx",
                ["ws"] = "Bandai - WonderSwan",
                ["ngp"] = "SNK - Neo Geo Pocket",
                ["pce"] = "NEC - PC Engine - TurboGrafx 16",
                ["psx"] = "Sony - PlayStation",
                ["psp"] = "Sony - PlayStation Portable",
            };

        private static readonly ConcurrentDictionary<string, PlatformIndex> Indexes =
            new ConcurrentDictionary<string, PlatformIndex>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, byte> Downloading =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, CachedLookup> LookupCache =
            new ConcurrentDictionary<string, CachedLookup>(StringComparer.Ordinal);

        public static RdbRecord? TryLookup(string core, string romPath, string? title)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.GamesMetadataEnabled)
                {
                    return null;
                }

                if (!CoreToPlatform.TryGetValue(core, out var platform))
                {
                    return null;
                }

                long size;
                long mtime;
                try
                {
                    var info = new FileInfo(romPath);
                    size = info.Length;
                    mtime = info.LastWriteTimeUtc.Ticks;
                }
                catch
                {
                    return null;
                }

                if (LookupCache.TryGetValue(romPath, out var cached) &&
                    cached.Size == size && cached.Mtime == mtime)
                {
                    return cached.Record;
                }

                var index = GetIndex(platform, config);
                if (index == null)
                {
                    return null;
                }

                var record = Match(index, romPath, title);
                LookupCache[romPath] = new CachedLookup(size, mtime, record);
                return record;
            }
            catch
            {
                return null;
            }
        }

        private static RdbRecord? Match(PlatformIndex index, string romPath, string? title)
        {
            foreach (var crc in ComputeCrcCandidates(romPath))
            {
                if (index.ByCrc.TryGetValue(crc, out var byCrc))
                {
                    return byCrc;
                }
            }

            var fileName = NormalizeName(Path.GetFileNameWithoutExtension(romPath));
            if (fileName.Length > 0 && index.ByName.TryGetValue(fileName, out var byFile))
            {
                return byFile;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                var norm = NormalizeName(title!);
                if (norm.Length > 0 && index.ByName.TryGetValue(norm, out var byTitle))
                {
                    return byTitle;
                }
            }

            return null;
        }

        private static PlatformIndex? GetIndex(string platform, PluginConfiguration config)
        {
            if (Indexes.TryGetValue(platform, out var existing))
            {
                return existing;
            }

            var localPath = LocalPath(platform);
            if (localPath == null || !File.Exists(localPath))
            {
                EnsureDownloaded(platform, config);
                return null;
            }

            var index = BuildIndex(localPath);
            Indexes[platform] = index;
            return index;
        }

        private static PlatformIndex BuildIndex(string path)
        {
            var byCrc = new Dictionary<uint, RdbRecord>();
            var byName = new Dictionary<string, RdbRecord>(StringComparer.Ordinal);

            foreach (var record in RdbReader.ReadAll(path))
            {
                if (record.Crc.HasValue)
                {
                    byCrc[record.Crc.Value] = record;
                }

                if (record.RomName != null)
                {
                    byName[NormalizeName(Path.GetFileNameWithoutExtension(record.RomName))] = record;
                }

                if (record.Name != null)
                {
                    byName[NormalizeName(record.Name)] = record;
                }
            }

            return new PlatformIndex(byCrc, byName);
        }

        private static void EnsureDownloaded(string platform, PluginConfiguration config)
        {
            var localPath = LocalPath(platform);
            if (localPath == null || File.Exists(localPath))
            {
                return;
            }

            var baseLocation = config.GamesMetadataDbUrlBase;
            if (string.IsNullOrWhiteSpace(baseLocation))
            {
                return;
            }

            // A non-http base is treated as a local mirror directory (offline servers).
            if (!baseLocation.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var source = Path.Combine(baseLocation, platform + ".rdb");
                    if (File.Exists(source))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        File.Copy(source, localPath, overwrite: true);
                    }
                }
                catch
                {
                    // ignore
                }

                return;
            }

            if (!Downloading.TryAdd(platform, 0))
            {
                return;
            }

            var url = baseLocation.TrimEnd('/') + "/" + Uri.EscapeDataString(platform) + ".rdb";
            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = MoonfinHttp.CreateClient(TimeSpan.FromMinutes(2), "Moonfin/1.0");

                    var data = await client.GetByteArrayAsync(url).ConfigureAwait(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    var temp = localPath + ".tmp";
                    File.WriteAllBytes(temp, data);
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }

                    File.Move(temp, localPath);
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    Downloading.TryRemove(platform, out _);
                }
            });
        }

        private static string? LocalPath(string platform)
        {
            var dataFolder = Plugin.Instance?.DataFolderPath;
            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                return null;
            }

            return Path.Combine(dataFolder!, "gamemeta", platform + ".rdb");
        }

        // The whole file, plus the body without an iNES header for NES dumps (No-Intro CRCs
        // exclude that 16-byte header). Streamed so large ROMs are not loaded into memory.
        private static IReadOnlyList<uint> ComputeCrcCandidates(string romPath)
        {
            var candidates = new List<uint>(2);
            try
            {
                candidates.Add(Crc32File(romPath, 0));
                if (HasInesHeader(romPath))
                {
                    candidates.Add(Crc32File(romPath, 16));
                }
            }
            catch
            {
                // ignore
            }

            return candidates;
        }

        private static bool HasInesHeader(string path)
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            return fs.Length > 16 && fs.Read(head) == 4 &&
                head[0] == (byte)'N' && head[1] == (byte)'E' && head[2] == (byte)'S' && head[3] == 0x1A;
        }

        private static uint Crc32File(string path, int skip)
        {
            var crc = 0xFFFFFFFFu;
            using var fs = File.OpenRead(path);
            if (skip > 0)
            {
                fs.Seek(skip, SeekOrigin.Begin);
            }

            var buffer = new byte[65536];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                }
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }

        private static string NormalizeName(string value) => GamesScanner.NormalizeAlphanumericLower(value);

        private sealed class PlatformIndex
        {
            public PlatformIndex(
                IReadOnlyDictionary<uint, RdbRecord> byCrc,
                IReadOnlyDictionary<string, RdbRecord> byName)
            {
                ByCrc = byCrc;
                ByName = byName;
            }

            public IReadOnlyDictionary<uint, RdbRecord> ByCrc { get; }

            public IReadOnlyDictionary<string, RdbRecord> ByName { get; }
        }

        private readonly struct CachedLookup
        {
            public CachedLookup(long size, long mtime, RdbRecord? record)
            {
                Size = size;
                Mtime = mtime;
                Record = record;
            }

            public long Size { get; }

            public long Mtime { get; }

            public RdbRecord? Record { get; }
        }
    }
}
