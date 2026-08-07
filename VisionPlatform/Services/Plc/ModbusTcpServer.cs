using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace VisionPlatform.Services.Plc;

/// <summary>
/// 内置 Modbus TCP 从站模拟器：模拟 PLC 的线圈/寄存器存储区，
/// 支持自动触发信号，便于无硬件环境演示联机流程。
/// </summary>
public sealed class ModbusTcpServer : IDisposable
{
    public const int CoilCount = 2000;
    public const int RegisterCount = 2000;

    private readonly TcpListener _listener;
    private readonly bool[] _coils = new bool[CoilCount];
    private readonly ushort[] _registers = new ushort[RegisterCount];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpClient> _clients = [];
    private Task? _acceptTask;
    private bool _disposed;

    public int Port { get; }
    public bool IsRunning { get; private set; }

    public event Action? ClientConnected;

    public ModbusTcpServer(int port = 502)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start()
    {
        _listener.Start();
        IsRunning = true;
        _acceptTask = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                client.NoDelay = true;
                lock (_lock) _clients.Add(client);
                ClientConnected?.Invoke();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var header = new byte[7];
        try
        {
            while (client.Connected)
            {
                var read = await ReadExactlyAsync(stream, header, 7).ConfigureAwait(false);
                if (read < 7) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                var pdu = new byte[length - 1];
                if (await ReadExactlyAsync(stream, pdu, pdu.Length).ConfigureAwait(false) < pdu.Length) break;

                var tx = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0));
                var unit = header[6];
                var response = ProcessPdu(pdu);
                if (response is null) continue;

                var frame = new byte[7 + response.Length];
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0), tx);
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(response.Length + 1));
                frame[6] = unit;
                response.CopyTo(frame.AsSpan(7));
                await stream.WriteAsync(frame).ConfigureAwait(false);
            }
        }
        catch
        {
            // 客户端断开
        }
        finally
        {
            lock (_lock) _clients.Remove(client);
            client.Dispose();
        }
    }

    private static async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset, count - offset)).ConfigureAwait(false);
            if (n <= 0) return offset;
            offset += n;
        }
        return offset;
    }

    private byte[]? ProcessPdu(byte[] pdu)
    {
        if (pdu.Length < 1) return null;
        var fc = pdu[0];
        try
        {
            switch (fc)
            {
                case 0x01: // Read Coils
                case 0x02:
                {
                    var addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                    var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                    if (addr + count > CoilCount) return Exception(fc, 2);
                    lock (_lock)
                    {
                        var bytes = (count + 7) / 8;
                        var resp = new byte[2 + bytes];
                        resp[0] = fc; resp[1] = (byte)bytes;
                        for (int i = 0; i < count; i++)
                            if (_coils[addr + i]) resp[2 + i / 8] |= (byte)(1 << (i % 8));
                        return resp;
                    }
                }
                case 0x03:
                case 0x04:
                {
                    var addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                    var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                    if (addr + count > RegisterCount) return Exception(fc, 2);
                    lock (_lock)
                    {
                        var resp = new byte[2 + count * 2];
                        resp[0] = fc; resp[1] = (byte)(count * 2);
                        for (int i = 0; i < count; i++)
                            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2 + i * 2), _registers[addr + i]);
                        return resp;
                    }
                }
                case 0x05: // Write Single Coil
                {
                    var addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                    var value = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                    if (addr >= CoilCount) return Exception(fc, 2);
                    lock (_lock) _coils[addr] = value == 0xFF00;
                    return pdu;
                }
                case 0x06: // Write Single Register
                {
                    var addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                    var value = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                    if (addr >= RegisterCount) return Exception(fc, 2);
                    lock (_lock) _registers[addr] = value;
                    return pdu;
                }
                case 0x10: // Write Multiple Registers
                {
                    var addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
                    var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
                    var byteCount = pdu[5];
                    if (addr + count > RegisterCount || pdu.Length < 6 + byteCount) return Exception(fc, 2);
                    lock (_lock)
                    {
                        for (int i = 0; i < count; i++)
                            _registers[addr + i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(6 + i * 2));
                    }
                    return [0x10, (byte)(addr >> 8), (byte)(addr & 0xFF), (byte)(count >> 8), (byte)(count & 0xFF)];
                }
                default:
                    return Exception(fc, 1);
            }
        }
        catch (Exception)
        {
            return Exception(fc, 4);
        }
    }

    private static byte[] Exception(byte fc, byte code) => [(byte)(fc | 0x80), code];

    // ---------------- 模拟操作（供 UI 与自动触发使用） ----------------
    public void SetCoil(int address, bool value)
    {
        lock (_lock)
        {
            if (address < CoilCount) _coils[address] = value;
        }
    }

    public bool GetCoil(int address)
    {
        lock (_lock) return address < CoilCount && _coils[address];
    }

    public void SetRegister(int address, ushort value)
    {
        lock (_lock)
        {
            if (address < RegisterCount) _registers[address] = value;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        lock (_lock)
        {
            foreach (var c in _clients) c.Dispose();
            _clients.Clear();
        }
        _listener.Stop();
        IsRunning = false;
    }
}
