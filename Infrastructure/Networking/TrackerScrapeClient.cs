using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Networking
{
    /// <summary>
    /// Клиент UDP-scrape по BEP 15. Спрашивает у трекера, сколько сейчас сидов
    /// и пиров у раздачи. За один пакет отдаёт до 74 хешей, поэтому вся выдача
    /// поиска обходится одним-двумя обращениями.
    /// </summary>
    public static class TrackerScrapeClient
    {
        const long ProtocolId = 0x41727101980;
        const int ActionConnect = 0;
        const int ActionScrape = 2;

        public readonly struct Counts
        {
            public Counts(int seeders, int leechers)
            {
                Seeders = seeders;
                Leechers = leechers;
            }

            public int Seeders { get; }
            public int Leechers { get; }
        }

        /// <summary>
        /// Опрашивает один трекер о наборе раздач. Возвращает то, что удалось узнать;
        /// пустой словарь означает «трекер не ответил», а не «раздач нет».
        /// </summary>
        public static async Task<Dictionary<string, Counts>> ScrapeAsync(
            string announceUrl,
            IReadOnlyList<byte[]> hashes,
            int timeoutMs,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, Counts>(StringComparer.OrdinalIgnoreCase);
            if (hashes == null || hashes.Count == 0)
                return result;

            if (!TryParseUdp(announceUrl, out string host, out int port))
                return result;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            var token = cts.Token;

            Socket socket = null;
            try
            {
                // Часть трекеров живёт только на IPv6 — семейство выбираем по факту,
                // а не жёстко AF_INET, иначе получаем молчание вместо ответа.
                var addresses = await Dns.GetHostAddressesAsync(host, token);
                var address = addresses.FirstOrDefault(a =>
                                  a.AddressFamily == AddressFamily.InterNetwork ||
                                  a.AddressFamily == AddressFamily.InterNetworkV6);
                if (address == null)
                    return result;

                socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                await socket.ConnectAsync(new IPEndPoint(address, port), token);

                long connectionId = await ConnectAsync(socket, token);
                if (connectionId == 0)
                    return result;

                await ScrapeBatchAsync(socket, connectionId, hashes, result, token);
            }
            catch (OperationCanceledException)
            {
                // Вышли за таймаут — отдаём то, что успели.
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                socket?.Dispose();
            }

            return result;
        }

        static async Task<long> ConnectAsync(Socket socket, CancellationToken token)
        {
            int transactionId = Random.Shared.Next();

            var request = new byte[16];
            BinaryPrimitives.WriteInt64BigEndian(request.AsSpan(0, 8), ProtocolId);
            BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(8, 4), ActionConnect);
            BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(12, 4), transactionId);

            await socket.SendAsync(request, SocketFlags.None, token);

            var response = new byte[16];
            int read = await socket.ReceiveAsync(response, SocketFlags.None, token);
            if (read < 16)
                return 0;

            int action = BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4));
            int tid = BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4));
            if (action != ActionConnect || tid != transactionId)
                return 0;

            return BinaryPrimitives.ReadInt64BigEndian(response.AsSpan(8, 8));
        }

        static async Task ScrapeBatchAsync(
            Socket socket,
            long connectionId,
            IReadOnlyList<byte[]> hashes,
            Dictionary<string, Counts> result,
            CancellationToken token)
        {
            int transactionId = Random.Shared.Next();

            var request = new byte[16 + hashes.Count * 20];
            BinaryPrimitives.WriteInt64BigEndian(request.AsSpan(0, 8), connectionId);
            BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(8, 4), ActionScrape);
            BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(12, 4), transactionId);

            for (int i = 0; i < hashes.Count; i++)
            {
                if (hashes[i] == null || hashes[i].Length != 20)
                    return;
                hashes[i].CopyTo(request, 16 + i * 20);
            }

            await socket.SendAsync(request, SocketFlags.None, token);

            var response = new byte[8 + hashes.Count * 12];
            int read = await socket.ReceiveAsync(response, SocketFlags.None, token);
            if (read < 8)
                return;

            int action = BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(0, 4));
            int tid = BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(4, 4));
            if (action != ActionScrape || tid != transactionId)
                return;

            int entries = Math.Min(hashes.Count, (read - 8) / 12);
            for (int i = 0; i < entries; i++)
            {
                int offset = 8 + i * 12;
                int seeders = BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(offset, 4));
                int leechers = BinaryPrimitives.ReadInt32BigEndian(response.AsSpan(offset + 8, 4));
                result[Convert.ToHexString(hashes[i]).ToLowerInvariant()] = new Counts(seeders, leechers);
            }
        }

        /// <summary>Разбирает udp://host:port/announce. Всё остальное нам не подходит.</summary>
        public static bool TryParseUdp(string announceUrl, out string host, out int port)
        {
            host = null;
            port = 0;

            if (string.IsNullOrWhiteSpace(announceUrl))
                return false;
            if (!announceUrl.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                return false;

            string rest = announceUrl.Substring(6);
            int slash = rest.IndexOf('/');
            if (slash >= 0)
                rest = rest.Substring(0, slash);

            int colon = rest.LastIndexOf(':');
            if (colon <= 0 || colon == rest.Length - 1)
                return false;

            host = rest.Substring(0, colon).Trim('[', ']');
            if (string.IsNullOrWhiteSpace(host))
                return false;

            return int.TryParse(rest.Substring(colon + 1), out port) && port > 0 && port <= 65535;
        }
    }
}
