using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace EOL_Net_Verifier
{
    public class ScanResult
    {
        public string Address { get; }
        public bool IsAlive { get; }
        public long RoundtripTime { get; }
        public string Hostname { get; }

        public ScanResult(string address, bool isAlive, long rtt, string hostname = "")
        {
            Address = address;
            IsAlive = isAlive;
            RoundtripTime = rtt;
            Hostname = hostname ?? string.Empty;
        }
    }

    public class IpScanner
    {
        /// <summary>
        /// Scan a range of IPv4 addresses using ICMP ping.
        /// </summary>
        /// <param name="prefix">Network prefix, e.g. "192.168.1." (must include trailing dot)</param>
        /// <param name="start">Start octet (inclusive)</param>
        /// <param name="end">End octet (inclusive)</param>
        /// <param name="onResult">Optional callback invoked for each host as result arrives</param>
        /// <param name="timeout">Ping timeout in milliseconds</param>
        /// <param name="maxConcurrent">Max concurrent pings</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of ScanResult for the requested range</returns>
        public async Task<List<ScanResult>> ScanRangeAsync(
            string prefix,
            int start,
            int end,
            Action<ScanResult> onResult = null,
            int timeout = 1000,
            int maxConcurrent = 5,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("prefix");
            if (start < 0 || start > 255) throw new ArgumentOutOfRangeException(nameof(start));
            if (end < 0 || end > 255) throw new ArgumentOutOfRangeException(nameof(end));
            if (start > end) throw new ArgumentException("start must be <= end");

            var results = new List<ScanResult>();
            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(maxConcurrent);
            var locker = new object();

            for (int i = start; i <= end; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string address = prefix + i.ToString();

                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        using (var ping = new Ping())
                        {
                            try
                            {
                                var reply = await ping.SendPingAsync(address, timeout).ConfigureAwait(false);
                                string hostname = string.Empty;
                                if (reply.Status == IPStatus.Success)
                                {
                                    try
                                    {
                                        var entry = await Dns.GetHostEntryAsync(address).ConfigureAwait(false);
                                        hostname = entry?.HostName ?? string.Empty;
                                    }
                                    catch
                                    {
                                        hostname = string.Empty;
                                    }
                                }

                                var res = new ScanResult(address, reply.Status == IPStatus.Success, reply.RoundtripTime, hostname);
                                lock (locker) results.Add(res);
                                onResult?.Invoke(res);
                            }
                            catch
                            {
                                var res = new ScanResult(address, false, -1);
                                lock (locker) results.Add(res);
                                onResult?.Invoke(res);
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return results.OrderBy(r => r.Address, StringComparer.Ordinal).ToList();
        }
    }
}
