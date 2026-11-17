using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BugyardSDK
{
    /// <summary>
    /// On-disk store for reports that failed to upload (e.g. while offline). The already-clamped
    /// wire artifacts — metadata JSON, screenshot PNG and logs — are persisted in
    /// a per-destination directory under <c>{persistentDataPath}/Bugyard/queue</c>, one file
    /// per report, so a later launch can replay them against the same endpoint and API key.
    /// The original <c>clientReportId</c> is preserved, so a replay the backend
    /// already received is deduplicated idempotently rather than creating a duplicate.
    ///
    /// The queue is bounded by <see cref="BugyardConfig.maxQueuedReports"/> per destination and
    /// <see cref="BugyardConfig.maxQueuedBytes"/> across all destinations. The oldest reports
    /// are dropped to make room. Every method is tolerant of I/O failures and never
    /// throws into the upload path — a queue problem must never break sending or the game.
    /// </summary>
    public static class OfflineReportQueue
    {
        // Bump if the persisted envelope shape changes; files with a different/absent version are
        // dropped on load rather than risking a malformed replay.
        const int SchemaVersion = 1;
        const int FilenamePrefixLength = 20; // 19 timestamp digits and one hyphen
        static readonly object QueueLock = new object();

        // Persisted envelope. Binary attachments are base64-encoded so the whole record is a single
        // JSON file (JsonUtility-friendly, atomic to write) and the embedded metadata JSON needs no
        // re-escaping. base64 has no characters JsonUtility must escape.
        [Serializable]
        class Envelope
        {
            public int version;
            public string clientReportId;
            public long enqueuedAtUtcTicks;
            public string metadataBase64;
            public string logsBase64;
            public string screenshotBase64;
        }

        /// <summary>A decoded queued report ready to replay, plus the disk path backing it.</summary>
        public class Entry
        {
            public string Path;
            public string ClientReportId;
            public string MetadataJson;
            public string Logs;
            public byte[] Screenshot;
            public long EnqueuedAtUtcTicks;
        }

        static string Root => Path.Combine(Application.persistentDataPath, "Bugyard", "queue");

        /// <summary>
        /// Persist a failed report so it can be retried on a later launch. Enforces the configured
        /// queue limits by dropping the oldest entries first, and replaces any existing file for the
        /// same <paramref name="clientReportId"/> so re-enqueueing the same report can't duplicate it.
        /// Returns true if the report was written; false if it exceeds the byte limit or cannot be written.
        /// </summary>
        public static bool Enqueue(
            BugyardConfig config, string metadataJson, byte[] screenshot, string logs, string clientReportId)
        {
            if (config == null) return false;
            return Enqueue(new ReportDestination(config), config.maxQueuedReports, config.maxQueuedBytes,
                metadataJson, screenshot, logs, clientReportId);
        }

        internal static bool Enqueue(ReportDestination destination, int maxQueuedReports, int maxQueuedBytes,
            string metadataJson, byte[] screenshot, string logs, string clientReportId)
        {
            if (string.IsNullOrEmpty(metadataJson) || string.IsNullOrEmpty(clientReportId)) return false;

            lock (QueueLock)
            {
                string tmpPath = null;
                try
                {
                    if (clientReportId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        Debug.LogWarning("[Bugyard] Could not persist a report with an invalid clientReportId.");
                        return false;
                    }

                    int maxBytes = Mathf.Max(1, maxQueuedBytes);
                    // Check a lower bound before allocating Base64 strings for a report that cannot fit.
                    long base64Bytes = Base64Length(Encoding.UTF8.GetByteCount(metadataJson)) +
                        Base64Length(string.IsNullOrEmpty(logs) ? 0 : Encoding.UTF8.GetByteCount(logs)) +
                        Base64Length(screenshot?.Length ?? 0);
                    if (base64Bytes > maxBytes) return ReportTooLarge(maxBytes);

                    var env = new Envelope
                    {
                        version = SchemaVersion,
                        clientReportId = clientReportId,
                        enqueuedAtUtcTicks = DateTime.UtcNow.Ticks,
                        metadataBase64 = ToBase64(metadataJson),
                        logsBase64 = string.IsNullOrEmpty(logs) ? null : ToBase64(logs),
                        screenshotBase64 = (screenshot != null && screenshot.Length > 0)
                            ? Convert.ToBase64String(screenshot)
                            : null,
                    };

                    string json = JsonUtility.ToJson(env);
                    long size = (long)Encoding.UTF8.GetByteCount(json) + Encoding.UTF8.GetPreamble().Length;
                    if (size > maxBytes) return ReportTooLarge(maxBytes);

                    string root = Path.GetFullPath(Root);
                    string directory = QueueDirectory(root, destination);
                    string idFilename = clientReportId + ".json";
                    string name = env.enqueuedAtUtcTicks.ToString("D19", CultureInfo.InvariantCulture) + "-" + idFilename;
                    string finalPath = Path.Combine(directory, name);

                    Directory.CreateDirectory(directory);
                    TrimQueue(root, directory, maxQueuedReports, maxBytes, size, idFilename);

                    // Reserve space before writing. A crash mid-write leaves only a temporary file.
                    tmpPath = finalPath + ".tmp";
                    File.WriteAllText(tmpPath, json, Encoding.UTF8);
                    File.Move(tmpPath, finalPath);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Bugyard] Could not persist report for offline retry: " + e.Message);
                    return false;
                }
                finally
                {
                    if (tmpPath != null) SafeDelete(tmpPath);
                }
            }
        }

        /// <summary>
        /// Load and decode all queued reports for inspection, oldest first. Includes legacy
        /// reports from the shared queue directory. Records that cannot be decoded are dropped.
        /// </summary>
        public static List<Entry> LoadAll()
        {
            var result = new List<Entry>();
            try
            {
                string root = Root;
                if (!Directory.Exists(root)) return result;
                foreach (FileInfo file in QueueFiles(root))
                {
                    Entry entry = Read(file.FullName);
                    if (entry != null) result.Add(entry);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bugyard] Could not read the offline report queue: " + e.Message);
            }
            return result;
        }

        // Snapshot paths after applying current limits without reading any report payloads.
        internal static List<string> GetPathsForReplay(
            ReportDestination destination, int maxQueuedReports, int maxQueuedBytes)
        {
            lock (QueueLock)
            {
                try
                {
                    string root = Path.GetFullPath(Root);
                    if (Directory.Exists(root))
                    {
                        string directory = QueueDirectory(root, destination);
                        return TrimQueue(root, directory, maxQueuedReports, Mathf.Max(1, maxQueuedBytes));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Bugyard] Could not read the offline report queue: " + e.Message);
                }
                return new List<string>();
            }
        }

        internal static Entry Read(string path)
        {
            lock (QueueLock)
            {
                try
                {
                    // A send can evict a report after the replay took its snapshot.
                    if (!File.Exists(path)) return null;

                    var env = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
                    if (env == null || env.version != SchemaVersion || string.IsNullOrEmpty(env.metadataBase64))
                    {
                        Debug.LogWarning("[Bugyard] Dropping an unrecognized queued report: " + Path.GetFileName(path));
                        SafeDelete(path);
                        return null;
                    }

                    return new Entry
                    {
                        Path = path,
                        ClientReportId = env.clientReportId,
                        MetadataJson = FromBase64Utf8(env.metadataBase64),
                        Logs = string.IsNullOrEmpty(env.logsBase64) ? null : FromBase64Utf8(env.logsBase64),
                        Screenshot = string.IsNullOrEmpty(env.screenshotBase64)
                            ? null
                            : Convert.FromBase64String(env.screenshotBase64),
                        EnqueuedAtUtcTicks = env.enqueuedAtUtcTicks,
                    };
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Bugyard] Dropping an unreadable queued report: " + e.Message);
                    SafeDelete(path);
                    return null;
                }
            }
        }

        /// <summary>Delete a queued report once it has been delivered (or given up on).</summary>
        public static void Remove(string path)
        {
            lock (QueueLock) SafeDelete(path);
        }

        /// <summary>Number of reports currently persisted on disk (best-effort; 0 on any error).</summary>
        public static int Count()
        {
            try { return Directory.Exists(Root) ? Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories).Length : 0; }
            catch { return 0; }
        }

        // File names start with a fixed-width timestamp across all destination directories.
        static FileInfo[] QueueFiles(string root)
        {
            var files = new DirectoryInfo(root).GetFiles("*.json", SearchOption.AllDirectories);
            Array.Sort(files, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            return files;
        }

        static List<string> TrimQueue(string root, string directory, int maxQueuedReports, int maxBytes,
            long incomingBytes = 0, string idFilename = null)
        {
            // Clear interrupted writes before reserving space for another file.
            foreach (string path in Directory.GetFiles(root, "*.json.tmp", SearchOption.AllDirectories))
                File.Delete(path);

            var files = QueueFiles(root);
            var paths = new List<string>();
            var evicted = new List<FileInfo>();
            int remainingReports = Mathf.Max(1, maxQueuedReports) - (idFilename == null ? 0 : 1);
            long remainingBytes = maxBytes - incomingBytes;

            // Keep the newest reports that fit. The byte budget covers legacy files at the root
            // and files for other destinations, which are never returned for this replay.
            for (int i = files.Length - 1; i >= 0; i--)
            {
                FileInfo file = files[i];
                bool sameDestination = string.Equals(file.DirectoryName, directory, StringComparison.Ordinal);
                bool replaced = idFilename != null && file.Name.Length == FilenamePrefixLength + idFilename.Length &&
                    file.Name.EndsWith(idFilename, StringComparison.Ordinal);
                long size = file.Length;
                if (size > maxBytes || (sameDestination && (remainingReports == 0 || replaced)))
                {
                    evicted.Add(file);
                    continue;
                }

                // Once the queue runs out of space, all older records are evicted.
                if (size > remainingBytes)
                {
                    remainingBytes = -1;
                    evicted.Add(file);
                    continue;
                }

                remainingBytes -= size;
                if (sameDestination)
                {
                    remainingReports--;
                    paths.Add(file.FullName);
                }
            }

            for (int i = evicted.Count - 1; i >= 0; i--) evicted[i].Delete();
            paths.Reverse();
            return paths;
        }

        static string QueueDirectory(string root, ReportDestination destination)
        {
            // Length-prefix the URL to keep the pair unambiguous and hash it with the API key.
            string identity = destination.Url.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                destination.Url + destination.ApiKey;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                return Path.Combine(root, BitConverter.ToString(hash).Replace("-", ""));
            }
        }

        static bool ReportTooLarge(int maxBytes)
        {
            Debug.LogWarning($"[Bugyard] Report exceeds the offline queue's {maxBytes}-byte limit.");
            return false;
        }

        static long Base64Length(int bytes) => ((bytes + 2L) / 3) * 4;

        static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("[Bugyard] Could not delete a queued report file: " + e.Message); }
        }

        static string ToBase64(string utf8) => Convert.ToBase64String(Encoding.UTF8.GetBytes(utf8));

        static string FromBase64Utf8(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
