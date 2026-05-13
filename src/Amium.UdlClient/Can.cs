using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Amium.UdlClient;

public sealed class Can : IDisposable
{
    public delegate void OnMessageReceivedDelegate(uint id, byte dlc, byte[] data);
    public delegate void OnDiagnosticDelegate(string message);

    private readonly Action<string>? _diagnosticSink;
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly Thread _rxThread;
    private readonly Thread _txThread;
    private readonly ConcurrentQueue<CanMessage> _txBuffer = new();
    private readonly AutoResetEvent _txSignal = new(false);
    private readonly CancellationTokenSource _cancellation = new();
    private int _disposed;
    private long _txQueuedLogCount;
    private long _txSendLogCount;
    private long _rxPacketLogCount;
    private long _rxFrameLogCount;

    public Can(string ip, int port, Action<string>? diagnosticSink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        _diagnosticSink = diagnosticSink;

        WriteDiagnostic($"ctor start host={ip} port={port}");
        _remoteEndpoint = ResolveRemoteEndpoint(ip, port);
        WriteDiagnostic($"remote resolved endpoint={_remoteEndpoint.Address}:{_remoteEndpoint.Port}");
        _udpClient = CreateUdpClient(port);
        _udpClient.DontFragment = true;
        WriteDiagnostic($"udp socket created local={LocalEndpointText}");

        _rxThread = new Thread(RxLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = $"UdlClient-Rx-{ip}:{port}"
        };
        _rxThread.Start();
        WriteDiagnostic($"rx thread started name={_rxThread.Name} local={LocalEndpointText}");

        _txThread = new Thread(TxLoop)
        {
            IsBackground = true,
            Name = $"UdlClient-Tx-{ip}:{port}"
        };
        _txThread.Start();
        WriteDiagnostic($"tx thread started name={_txThread.Name} local={LocalEndpointText}");
    }

    public event OnMessageReceivedDelegate? MessageReceived;
    public event OnDiagnosticDelegate? Diagnostic;

    public int LocalPort => ((IPEndPoint?)_udpClient.Client.LocalEndPoint)?.Port ?? 0;
    public string LocalEndpointText => ((IPEndPoint?)_udpClient.Client.LocalEndPoint)?.ToString() ?? "<unbound>";

    public void Close()
    {
        WriteDiagnostic("close requested");
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        WriteDiagnostic("dispose start");
        _cancellation.Cancel();
        _txSignal.Set();

        try
        {
            _udpClient.Close();
            WriteDiagnostic("udp socket closed");
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"udp socket close failed error={exception.GetType().Name}: {exception.Message}");
        }

        JoinThread(_rxThread);
        JoinThread(_txThread);
        WriteDiagnostic("threads joined");

        _txSignal.Dispose();
        _cancellation.Dispose();
        _udpClient.Dispose();
        WriteDiagnostic("dispose completed");
    }

    public bool Transmit(CanMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (Volatile.Read(ref _disposed) != 0)
        {
            WriteDiagnostic($"tx ignored disposed id=0x{message.Id:X3}");
            return false;
        }

        _txBuffer.Enqueue(message);
        _txSignal.Set();
        if (ShouldSample(ref _txQueuedLogCount, 8, 100))
        {
            WriteDiagnostic($"tx queued id=0x{message.Id:X3} dlc={Math.Min(message.Data.Length, 8)} queue={_txBuffer.Count}");
        }

        return true;
    }

    private void TxLoop()
    {
        var token = _cancellation.Token;
        var buffer = new byte[1500];
        var packageCounter = 0;

        while (!token.IsCancellationRequested)
        {
            if (!_txBuffer.TryDequeue(out var firstMessage))
            {
                _txSignal.WaitOne(10);
                continue;
            }

            try
            {
                buffer[0] = (byte)(packageCounter >> 24);
                buffer[1] = (byte)(packageCounter >> 16);
                buffer[2] = (byte)(packageCounter >> 8);
                buffer[3] = (byte)packageCounter;

                var offset = 4;
                offset = AppendMessage(buffer, offset, firstMessage);

                while (offset < 1450 && _txBuffer.TryDequeue(out var queuedMessage))
                {
                    offset = AppendMessage(buffer, offset, queuedMessage);
                }

                packageCounter++;
                if (ShouldSample(ref _txSendLogCount, 8, 100))
                {
                    WriteDiagnostic($"tx send package={packageCounter - 1} bytes={offset} remote={_remoteEndpoint.Address}:{_remoteEndpoint.Port} local={LocalEndpointText}");
                }
                _udpClient.Send(buffer, offset, _remoteEndpoint);
            }
            catch (ObjectDisposedException) when (token.IsCancellationRequested)
            {
                WriteDiagnostic("tx loop stopped because socket disposed during cancellation");
                return;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                WriteDiagnostic("tx loop stopped because socket exception during cancellation");
                return;
            }
            catch (Exception exception)
            {
                WriteDiagnostic($"tx loop error={exception.GetType().Name}: {exception.Message}");
            }
        }

        WriteDiagnostic("tx loop exited");
    }

    private void RxLoop()
    {
        var token = _cancellation.Token;
        var sender = new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var bytes = _udpClient.Receive(ref sender);
                if (ShouldSample(ref _rxPacketLogCount, 8, 50))
                {
                    WriteDiagnostic($"rx packet bytes={bytes.Length} from={sender.Address}:{sender.Port} local={LocalEndpointText}");
                }
                var offset = 4;
                var frameCount = 0;

                while (bytes.Length >= offset + 12)
                {
                    var id = (uint)((bytes[offset + 7] << 24)
                                    | (bytes[offset + 8] << 16)
                                    | (bytes[offset + 9] << 8)
                                    | bytes[offset + 10]);

                    var dlc = bytes[offset + 11];
                    if (dlc > 8)
                    {
                        dlc = 8;
                    }

                    if (bytes.Length < offset + 12 + dlc)
                    {
                        break;
                    }

                    var data = new byte[dlc];
                    Array.Copy(bytes, offset + 12, data, 0, dlc);
                    frameCount++;
                    if (ShouldSample(ref _rxFrameLogCount, 8, 100))
                    {
                        WriteDiagnostic($"rx frame[{frameCount}] id=0x{id:X3} dlc={dlc} data={FormatBytes(data, dlc)}");
                    }
                    MessageReceived?.Invoke(id, dlc, data);

                    offset += 12 + dlc;
                }

                if (frameCount == 0)
                {
                    WriteDiagnostic($"rx packet had no decodable frames bytes={bytes.Length}");
                }
            }
            catch (ObjectDisposedException) when (token.IsCancellationRequested)
            {
                WriteDiagnostic("rx loop stopped because socket disposed during cancellation");
                return;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                WriteDiagnostic("rx loop stopped because socket exception during cancellation");
                return;
            }
            catch (Exception exception)
            {
                WriteDiagnostic($"rx loop error={exception.GetType().Name}: {exception.Message}");
                if (!token.IsCancellationRequested)
                {
                    Thread.Sleep(1);
                }
            }
        }

        WriteDiagnostic("rx loop exited");
    }

    private static int AppendMessage(byte[] buffer, int offset, CanMessage message)
    {
        var payloadLength = Math.Min(message.Data.Length, 8);
        var recordLength = 12 + payloadLength;
        if (offset + recordLength > buffer.Length)
        {
            return offset;
        }

        var timestamp = DateTime.UtcNow.Ticks;

        buffer[offset] = (byte)recordLength;
        buffer[offset + 1] = (byte)(timestamp >> 24);
        buffer[offset + 2] = (byte)(timestamp >> 16);
        buffer[offset + 3] = (byte)(timestamp >> 8);
        buffer[offset + 4] = (byte)timestamp;
        buffer[offset + 5] = 0;
        buffer[offset + 6] = 0;
        buffer[offset + 7] = (byte)(message.Id >> 24);
        buffer[offset + 8] = (byte)(message.Id >> 16);
        buffer[offset + 9] = (byte)(message.Id >> 8);
        buffer[offset + 10] = (byte)message.Id;
        buffer[offset + 11] = (byte)payloadLength;

        Array.Copy(message.Data, 0, buffer, offset + 12, payloadLength);
        return offset + recordLength;
    }

    private static IPEndPoint ResolveRemoteEndpoint(string host, int port)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return new IPEndPoint(address, port);
        }

        var addresses = Dns.GetHostAddresses(host);
        var selectedAddress = addresses.FirstOrDefault(static candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                              ?? addresses.FirstOrDefault()
                              ?? throw new SocketException((int)SocketError.HostNotFound);

        return new IPEndPoint(selectedAddress, port);
    }

    private void JoinThread(Thread thread)
    {
        if (thread.ThreadState == System.Threading.ThreadState.Unstarted)
        {
            return;
        }

        try
        {
            thread.Join(250);
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"thread join failed name={thread.Name ?? "<unnamed>"} error={exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool ShouldSample(ref long counter, long initialBurst, long every)
    {
        var current = Interlocked.Increment(ref counter);
        return current <= initialBurst || current % every == 0;
    }

    private UdpClient CreateUdpClient(int localPort)
    {
        try
        {
            var boundClient = new UdpClient(AddressFamily.InterNetwork);
            boundClient.Client.ExclusiveAddressUse = false;
            boundClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            boundClient.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            WriteDiagnostic($"udp socket bound requested localPort={localPort}");
            return boundClient;
        }
        catch (Exception exception)
        {
            WriteDiagnostic($"udp socket bind to localPort={localPort} failed error={exception.GetType().Name}: {exception.Message}");

            var fallbackClient = new UdpClient(0);
            WriteDiagnostic("udp socket fallback bind to ephemeral local port");
            return fallbackClient;
        }
    }

    private void WriteDiagnostic(string message)
    {
        var formatted = $"[Can] {message}";
        try
        {
            _diagnosticSink?.Invoke(formatted);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Can diagnostic sink failed: {exception}");
        }

        try
        {
            Diagnostic?.Invoke(formatted);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Can diagnostic callback failed: {exception}");
        }
    }

    private static string FormatBytes(byte[] data, byte dlc)
    {
        if (data.Length == 0 || dlc == 0)
        {
            return "<empty>";
        }

        var length = Math.Min(data.Length, dlc);
        var parts = new string[length];
        for (var index = 0; index < length; index++)
        {
            parts[index] = data[index].ToString("X2");
        }

        return string.Join(" ", parts);
    }
}
