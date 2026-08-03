using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;

namespace VisionPlatform.Services.Plc;

public class ModbusException : Exception
{
    public byte FunctionCode { get; }
    public byte ExceptionCode { get; }

    public ModbusException(byte functionCode, byte exceptionCode)
        : base($"Modbus 异常响应: 功能码 0x{functionCode:X2}, 异常码 {exceptionCode}")
    {
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }
}

/// <summary>
/// 手写 Modbus TCP 主站协议（MBAP + 常用功能码），零第三方依赖。
/// 支持：01/02 读线圈, 03/04 读寄存器, 05 写单线圈, 06 写单寄存器, 10 写多寄存器。
/// 地址采用 0 基物理地址。
/// </summary>
public sealed class ModbusTcpClient : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transactionId;
    private byte _unitId = 1;
    private int _timeoutMs = 2000;

    public bool IsConnected { get; private set; }
    public string? LastError { get; private set; }

    public async Task<bool> ConnectAsync(string host, int port, byte unitId = 1, int timeoutMs = 2000)
    {
        _unitId = unitId;
        _timeoutMs = timeoutMs;
        Disconnect();
        try
        {
            _client = new TcpClient { NoDelay = true };
            using var cts = new CancellationTokenSource(timeoutMs);
            await _client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            _stream = _client.GetStream();
            _stream.ReadTimeout = _timeoutMs;
            _stream.WriteTimeout = _timeoutMs;
            IsConnected = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        IsConnected = false;
        _stream?.Dispose();
        _stream = null;
        _client?.Dispose();
        _client = null;
    }

    // ---------------- 读操作 ----------------

    /// <summary>读取线圈状态 (FC01)。</summary>
    public Task<bool> ReadCoilAsync(uint address) => ReadCoilsAsync(address, 1).ContinueWith(t => t.Result[0]);

    public async Task<bool[]> ReadCoilsAsync(uint address, ushort count)
    {
        var data = await TransactAsync(0x01, BuildReadRequest(address, count)).ConfigureAwait(false);
        var result = new bool[count];
        for (int i = 0; i < count; i++)
            result[i] = (data[i / 8] >> (i % 8) & 1) == 1;
        return result;
    }

    /// <summary>读取保持寄存器 (FC03)。</summary>
    public Task<ushort> ReadRegisterAsync(uint address) => ReadRegistersAsync(address, 1).ContinueWith(t => t.Result[0]);

    public async Task<ushort[]> ReadRegistersAsync(uint address, ushort count)
    {
        var data = await TransactAsync(0x03, BuildReadRequest(address, count)).ConfigureAwait(false);
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i * 2));
        return result;
    }

    // ---------------- 写操作 ----------------

    /// <summary>写单个线圈 (FC05)。</summary>
    public async Task WriteCoilAsync(uint address, bool value)
    {
        var req = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(0), (ushort)address);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(2), value ? (ushort)0xFF00 : (ushort)0x0000);
        await TransactAsync(0x05, req).ConfigureAwait(false);
    }

    /// <summary>写单个保持寄存器 (FC06)。</summary>
    public async Task WriteRegisterAsync(uint address, ushort value)
    {
        var req = new byte[5];
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(0), (ushort)address);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(2), value);
        await TransactAsync(0x06, req).ConfigureAwait(false);
    }

    /// <summary>写多个保持寄存器 (FC10)。</summary>
    public async Task WriteRegistersAsync(uint address, ushort[] values)
    {
        var req = new byte[5 + values.Length * 2];
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(0), (ushort)address);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(2), (ushort)values.Length);
        req[4] = (byte)(values.Length * 2);
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(5 + i * 2), values[i]);
        await TransactAsync(0x10, req).ConfigureAwait(false);
    }

    // ---------------- 协议实现 ----------------

    private static byte[] BuildReadRequest(uint address, ushort count)
    {
        var req = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(0), (ushort)address);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(2), count);
        return req;
    }

    private async Task<byte[]> TransactAsync(byte functionCode, byte[] request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsConnected || _stream is null)
                throw new IOException("未连接到 PLC");
            var pduLen = request.Length + 2; // 单元号 + 功能码 + 数据
            var frame = new byte[8 + request.Length];
            var tx = _transactionId++;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0), tx);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)pduLen);
            frame[6] = _unitId;
            frame[7] = functionCode;
            request.CopyTo(frame.AsSpan(8));
            await _stream.WriteAsync(frame).AsTask().ConfigureAwait(false);
            return await ReadResponseAsync(tx, functionCode).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> ReadResponseAsync(ushort expectedTx, byte expectedFc)
    {
        var header = await ReadExactlyAsync(7).ConfigureAwait(false);
        var tx = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0));
        var len = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        var unit = header[6];
        if (tx != expectedTx || unit != _unitId)
            throw new IOException($"响应事务号/单元号不匹配 (tx={tx}, unit={unit})");
        if (len < 2) throw new IOException("响应长度非法");
        var pdu = await ReadExactlyAsync(len - 1).ConfigureAwait(false);
        var fc = pdu[0];
        if (fc == (expectedFc | 0x80))
            throw new ModbusException(expectedFc, pdu[1]);
        if (fc != expectedFc)
            throw new IOException($"功能码不匹配: 期望 0x{expectedFc:X2}, 收到 0x{fc:X2}");
        // 读响应 pdu = [fc, byteCount, data...]；写响应为请求回显（调用方不使用其数据）
        return pdu[2..];
    }

    private async Task<byte[]> ReadExactlyAsync(int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset)).ConfigureAwait(false);
            if (read <= 0) throw new IOException("连接中断");
            offset += read;
        }
        return buffer;
    }

    public void Dispose() => Disconnect();
}
