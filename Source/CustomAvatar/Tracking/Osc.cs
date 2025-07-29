using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System;
using static System.MemoryExtensions;

namespace CustomAvatar.Tracking;

[StructLayout(LayoutKind.Sequential)]
internal readonly ref struct OscMessage {
	public enum Type {
		Null,
		Integer,
		Float,
		String,
		Blob,
		True,
		False,
		Impulse,
		Timetag,
	}
	public ref struct Value {
		public readonly Type type;
		private readonly ReadOnlySpan<byte> _raw;
		private Value(Type type, ReadOnlySpan<byte> raw) {
			this.type = type;
			_raw = raw;
		}
		public int? asInteger => (type == Type.Integer) ? BinaryPrimitives.ReadInt32BigEndian(_raw) : null;
		public float? asFloat => (type == Type.Float) ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(_raw)) : null;
		public ReadOnlySpan<byte> asString => (type == Type.String) ? _raw : default;
		public ReadOnlySpan<byte> asBlob => (type == Type.Blob) ? _raw : default;
		public bool? asBool => type switch {Type.True => true, Type.False => false, _ => null};
		public ulong? asTimetag => (type == Type.Timetag) ? BinaryPrimitives.ReadUInt64BigEndian(_raw) : null;
		public static int Parse(ReadOnlySpan<byte> packet, byte type, out Value value) {
			value = type switch {
				(byte)'b' => new(Type.Blob, packet.Slice(4, BinaryPrimitives.ReadInt32BigEndian(packet.Slice(0, 4)))),
				(byte)'f' => new(Type.Float, packet.Slice(0, 4)),
				(byte)'F' => new(Type.False, default),
				(byte)'i' => new(Type.Integer, packet.Slice(0, 4)),
				(byte)'I' => new(Type.Impulse, default),
				(byte)'N' or (byte)'[' or (byte)']' => new(Type.Null, default),
				(byte)'s' => new(Type.String, packet.Slice(0, packet.IndexOf((byte)'\0'))),
				(byte)'T' => new(Type.True, default),
				(byte)'t' => new(Type.Timetag, packet.Slice(0, 8)),
				_ => throw new Exception("Unrecognized type tag"),
			};
			return (value._raw.Length + value.type switch {Type.String => 1, Type.Blob => 4, _ => 0} + 3) & ~0x3;
		}
	}
	public readonly ReadOnlySpan<byte> address;
	public readonly int length;
	private readonly Value _0 = default, _1 = default, _2 = default, _3 = default, _4 = default, _5 = default, _6 = default, _7 = default;
	public readonly Value this[int index] {
		get {
			if((uint)index >= length)
				throw new IndexOutOfRangeException();
			return index switch {0 => _0, 1 => _1, 2 => _2, 3 => _3, 4 => _4, 5 => _5, 6 => _6, 7 => _7, _ => default};
		}
	}
	public OscMessage(ReadOnlySpan<byte> packet) {
		if(packet.Length < 4 || packet[0] != '/')
			throw new Exception("Not an OSC packet");
		int address_len = 1 + packet.Slice(1).IndexOf((byte)'\0');
		if(address_len == 0)
			throw new Exception("Unterminated address string");
		int header = (address_len + 4) & ~0x3;
		if(packet[header++] != ',')
			throw new Exception("Expected type tag string");
		int header_len = packet.Slice(header).IndexOf((byte)'\0');
		if(header_len == -1)
			throw new Exception("Unterminated type tag string");
		if(header_len > 8)
			throw new Exception("Too many fields");
		int header_end = header + header_len;
		int body = (header_end + 4) & ~0x3;
		address = packet.Slice(0, address_len);
		length = header_end - header;
		if (header < header_end) body += OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _0);
		if (header < header_end) body += OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _1);
		if (header < header_end) body += OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _2);
		if (header < header_end) body += OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _3);
		if (header < header_end) body += OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _4);
		if (header < header_end) body += OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _5);
		if (header < header_end) body += OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _6);
		if (header < header_end)         OscMessage.Value.Parse(packet.Slice(body), packet[header++], out _7);
	}
}

internal ref struct OscPacket {
	public ref struct Enumerator {
		private ReadOnlySpan<byte> _packet;
		private uint _length;
		private bool _single;
		public Enumerator(ReadOnlySpan<byte> packet, bool bundle) {
			_packet = packet;
			_length = bundle ? 16 : (uint)packet.Length;
			_single = !bundle;
		}
		#pragma warning disable IDE1006
		public OscMessage Current => new(_packet.Slice(0, (int)_length));
		#pragma warning restore IDE1006
		public bool MoveNext() {
			if(_single) {
				_single = false;
				return true;
			}
			_packet = _packet.Slice((int)_length);
			if(_packet.Length == 0) {
				_length = 0;
				return false;
			}
			_length = BinaryPrimitives.ReadUInt32BigEndian(_packet);
			_packet = _packet.Slice(4);
			return true;
		}
	}
	private readonly ReadOnlySpan<byte> _raw;
	public OscPacket(ReadOnlySpan<byte> raw) => _raw = raw;
	public Enumerator GetEnumerator() {
		bool bundle = (_raw.Length >= 16 && BinaryPrimitives.ReadUInt64LittleEndian(_raw) == 0x656c646e756223); // "#bundle\0"
		return (bundle || (_raw.Length >= 8 && _raw[0] == '/')) ? new(_raw, bundle) : default;
	}
}
