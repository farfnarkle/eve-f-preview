// Port of eve_blue_marshal.py — CCP "blue marshal" decoder/encoder for
// core_char_*.dat / core_user_*.dat. Read side from reverence (BSD);
// write side verified by Python round-trip on Tranquility settings.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EveFPreview.Services
{
	public sealed class EveBlueGlobal
	{
		public EveBlueGlobal(string name)
		{
			this.Name = name ?? string.Empty;
		}

		public string Name { get; }

		public override string ToString() => this.Name;

		public override bool Equals(object obj) =>
			obj is EveBlueGlobal other && string.Equals(this.Name, other.Name, StringComparison.Ordinal);

		public override int GetHashCode() => this.Name.GetHashCode();
	}

	public sealed class EveBlueObj
	{
		public string Kind; // "instance" | "reduce" | "newobj"
		public object Callable;
		public object Args;
		public object State;
		public readonly List<object> ListItems = new List<object>();
		public readonly Dictionary<object, object> DictItems = new Dictionary<object, object>();
	}

	public static class EveBlueMarshal
	{
		private const byte T_NONE = 0x01;
		private const byte T_GLOBAL = 0x02;
		private const byte T_INT64 = 0x03;
		private const byte T_INT32 = 0x04;
		private const byte T_INT16 = 0x05;
		private const byte T_INT8 = 0x06;
		private const byte T_MINUSONE = 0x07;
		private const byte T_ZERO = 0x08;
		private const byte T_ONE = 0x09;
		private const byte T_FLOAT = 0x0a;
		private const byte T_FLOAT0 = 0x0b;
		private const byte T_STRINGL = 0x0d;
		private const byte T_STRING0 = 0x0e;
		private const byte T_STRING1 = 0x0f;
		private const byte T_STRING = 0x10;
		private const byte T_STRINGR = 0x11;
		private const byte T_UNICODE = 0x12;
		private const byte T_BUFFER = 0x13;
		private const byte T_TUPLE = 0x14;
		private const byte T_LIST = 0x15;
		private const byte T_DICT = 0x16;
		private const byte T_INSTANCE = 0x17;
		private const byte T_BLUE = 0x18;
		private const byte T_REF = 0x1b;
		private const byte T_CHECKSUM = 0x1c;
		private const byte T_TRUE = 0x1f;
		private const byte T_FALSE = 0x20;
		private const byte T_REDUCE = 0x22;
		private const byte T_NEWOBJ = 0x23;
		private const byte T_TUPLE0 = 0x24;
		private const byte T_TUPLE1 = 0x25;
		private const byte T_LIST0 = 0x26;
		private const byte T_LIST1 = 0x27;
		private const byte T_UNICODE0 = 0x28;
		private const byte T_UNICODE1 = 0x29;
		private const byte T_TUPLE2 = 0x2c;
		private const byte T_MARK = 0x2d;
		private const byte T_UTF8 = 0x2e;
		private const byte T_LONG = 0x2f;
		private const byte SHARED = 0x40;

		private static readonly HashSet<byte> NeedLen = new HashSet<byte>
		{
			T_TUPLE, T_DICT, T_LIST, T_STRINGL, T_STRINGR, T_UNICODE, T_GLOBAL,
			T_UTF8, T_LONG, T_REF, T_BLUE, T_BUFFER
		};

		private static readonly object MarkSentinel = new object();

		// From reverence strings.py (BSD) — STRINGR table; encoder never emits STRINGR.
		private static readonly string[] StringTable = EveBlueStringTable.Entries;

		public static object Load(string path)
		{
			return new Decoder(File.ReadAllBytes(path)).Load();
		}

		public static object Loads(byte[] data)
		{
			return new Decoder(data).Load();
		}

		public static byte[] Dumps(object obj)
		{
			var parts = new List<byte[]> { new byte[] { 0x7e, 0, 0, 0, 0 } };
			Encode(obj, parts);
			int total = parts.Sum(p => p.Length);
			var result = new byte[total];
			int offset = 0;
			foreach (byte[] part in parts)
			{
				Buffer.BlockCopy(part, 0, result, offset, part.Length);
				offset += part.Length;
			}

			return result;
		}

		public static void Save(object obj, string path)
		{
			File.WriteAllBytes(path, Dumps(obj));
		}

		private sealed class Decoder
		{
			private readonly byte[] _data;
			private readonly int[] _map;
			private readonly object[] _shared;
			private int _pos;
			private readonly int _end;
			private int _sharedCount;

			public Decoder(byte[] data)
			{
				if (data == null || data.Length < 5 || data[0] != 0x7e)
				{
					throw new InvalidDataException("not a blue marshal stream (no 0x7e header)");
				}

				int mapSize = BitConverter.ToInt32(data, 1);
				_map = new int[mapSize];
				for (int i = 0; i < mapSize; i++)
				{
					_map[i] = BitConverter.ToInt32(data, data.Length - mapSize * 4 + i * 4);
				}

				_data = data;
				_pos = 5;
				_end = data.Length - mapSize * 4;
				_shared = new object[mapSize];
			}

			public object Load() => this.ReadObject();

			private int ReadLen()
			{
				int n = _data[_pos++];
				if (n == 255)
				{
					n = BitConverter.ToInt32(_data, _pos);
					_pos += 4;
				}

				return n;
			}

			private int Reserve()
			{
				int idx = _map[_sharedCount++];
				return idx - 1;
			}

			private object Keep(object o, int? slot)
			{
				if (slot.HasValue)
				{
					_shared[slot.Value] = o;
				}

				return o;
			}

			private object ReadObject()
			{
				if (_pos >= _end)
				{
					throw new InvalidDataException("unexpected end of blue marshal stream");
				}

				byte raw = _data[_pos++];
				bool shared = (raw & SHARED) != 0;
				byte t = (byte)(raw & ~SHARED);
				int length = NeedLen.Contains(t) ? this.ReadLen() : 0;
				int? slot = shared ? this.Reserve() : (int?)null;
				int p = _pos;

				switch (t)
				{
					case T_NONE: return this.Keep(null, slot);
					case T_TRUE: return this.Keep(true, slot);
					case T_FALSE: return this.Keep(false, slot);
					case T_MINUSONE: return this.Keep(-1, slot);
					case T_ZERO: return this.Keep(0, slot);
					case T_ONE: return this.Keep(1, slot);
					case T_FLOAT0: return this.Keep(0.0, slot);
					case T_INT8:
						_pos += 1;
						return this.Keep((int)(sbyte)_data[p], slot);
					case T_INT16:
						_pos += 2;
						return this.Keep((int)BitConverter.ToInt16(_data, p), slot);
					case T_INT32:
						_pos += 4;
						return this.Keep(BitConverter.ToInt32(_data, p), slot);
					case T_INT64:
						_pos += 8;
						return this.Keep(BitConverter.ToInt64(_data, p), slot);
					case T_FLOAT:
						_pos += 8;
						return this.Keep(BitConverter.ToDouble(_data, p), slot);
					case T_LONG:
						_pos += length;
						if (length == 0)
						{
							return this.Keep(0L, slot);
						}

						return this.Keep(DecodeSignedLittleEndian(_data, p, length), slot);
					case T_CHECKSUM:
						_pos += 4;
						return this.ReadObject();
					case T_STRING0:
						return this.Keep(string.Empty, slot);
					case T_STRING1:
						_pos += 1;
						return this.Keep(BytesToStr(_data, p, 1), slot);
					case T_STRING:
						{
							int n = _data[p];
							_pos += 1 + n;
							return this.Keep(BytesToStr(_data, p + 1, n), slot);
						}
					case T_STRINGL:
					case T_BUFFER:
					case T_BLUE:
						_pos += length;
						return this.Keep(BytesToStr(_data, p, length), slot);
					case T_UNICODE0:
						return this.Keep(string.Empty, slot);
					case T_UNICODE1:
						_pos += 2;
						return this.Keep(Encoding.Unicode.GetString(_data, p, 2), slot);
					case T_UNICODE:
						_pos += length * 2;
						return this.Keep(Encoding.Unicode.GetString(_data, p, length * 2), slot);
					case T_UTF8:
						_pos += length;
						return this.Keep(Encoding.UTF8.GetString(_data, p, length), slot);
					case T_STRINGR:
						return this.Keep(StringTable[length], slot);
					case T_GLOBAL:
						_pos += length;
						return this.Keep(new EveBlueGlobal(Encoding.Latin1.GetString(_data, p, length)), slot);
					case T_TUPLE0:
						return this.Keep(Array.Empty<object>(), slot);
					case T_TUPLE1:
					case T_TUPLE2:
					case T_TUPLE:
						{
							int n = t == T_TUPLE1 ? 1 : t == T_TUPLE2 ? 2 : length;
							var items = new object[n];
							for (int i = 0; i < n; i++)
							{
								items[i] = this.ReadObject();
							}

							return this.Keep(items, slot);
						}
					case T_LIST0:
						{
							var list = new List<object>();
							return this.Keep(list, slot);
						}
					case T_LIST1:
					case T_LIST:
						{
							int n = t == T_LIST1 ? 1 : length;
							var list = (List<object>)this.Keep(new List<object>(), slot);
							for (int i = 0; i < n; i++)
							{
								list.Add(this.ReadObject());
							}

							return list;
						}
					case T_DICT:
						{
							var dict = (Dictionary<object, object>)this.Keep(new Dictionary<object, object>(), slot);
							for (int i = 0; i < length; i++)
							{
								object v = this.ReadObject();
								object k = this.ReadObject();
								dict[MakeHashable(k)] = v;
							}

							return dict;
						}
					case T_INSTANCE:
						{
							var obj = (EveBlueObj)this.Keep(new EveBlueObj(), slot);
							obj.Kind = "instance";
							obj.Callable = this.ReadObject();
							obj.State = this.ReadObject();
							return obj;
						}
					case T_REDUCE:
					case T_NEWOBJ:
						{
							var obj = (EveBlueObj)this.Keep(new EveBlueObj(), slot);
							object spec = this.ReadObject();
							var specArr = AsArray(spec);
							if (t == T_REDUCE)
							{
								obj.Kind = "reduce";
								obj.Callable = specArr[0];
								obj.Args = specArr[1];
								obj.State = specArr.Length > 2 ? specArr[2] : null;
							}
							else
							{
								obj.Kind = "newobj";
								var head = AsArray(specArr[0]);
								obj.Callable = head[0];
								obj.Args = head.Skip(1).ToArray();
								obj.State = specArr.Length > 1 ? specArr[1] : null;
							}

							while (true)
							{
								object o = this.ReadObject();
								if (ReferenceEquals(o, MarkSentinel))
								{
									break;
								}

								obj.ListItems.Add(o);
							}

							while (true)
							{
								object k = this.ReadObject();
								if (ReferenceEquals(k, MarkSentinel))
								{
									break;
								}

								object v = this.ReadObject();
								obj.DictItems[MakeHashable(k)] = v;
							}

							return obj;
						}
					case T_MARK:
						return MarkSentinel;
					case T_REF:
						{
							object o = _shared[length - 1];
							if (o == null)
							{
								throw new InvalidDataException($"REF to empty slot {length} @{p}");
							}

							return o;
						}
					default:
						throw new InvalidDataException($"unhandled opcode 0x{t:x2} at pos {p - 1}");
				}
			}
		}

		private static void Encode(object o, List<byte[]> outParts)
		{
			if (o == null || o is NullDictKeyMarker)
			{
				outParts.Add(new byte[] { T_NONE });
			}
			else if (o is bool b)
			{
				outParts.Add(new byte[] { b ? T_TRUE : T_FALSE });
			}
			else if (o is EveBlueGlobal g)
			{
				byte[] bytes = Encoding.Latin1.GetBytes(g.Name);
				outParts.Add(Concat(new byte[] { T_GLOBAL }, EncLen(bytes.Length), bytes));
			}
			else if (o is byte[] rawBytes)
			{
				outParts.Add(Concat(new byte[] { T_BUFFER }, EncLen(rawBytes.Length), rawBytes));
			}
			else if (IsInteger(o, out long iv))
			{
				EncodeInt(iv, outParts);
			}
			else if (o is float f)
			{
				EncodeDouble(f, outParts);
			}
			else if (o is double d)
			{
				EncodeDouble(d, outParts);
			}
			else if (o is string s)
			{
				byte[] utf8 = Encoding.UTF8.GetBytes(s);
				outParts.Add(Concat(new byte[] { T_UTF8 }, EncLen(utf8.Length), utf8));
			}
			else if (o is object[] tuple)
			{
				int n = tuple.Length;
				if (n == 0)
				{
					outParts.Add(new byte[] { T_TUPLE0 });
				}
				else if (n == 1)
				{
					outParts.Add(new byte[] { T_TUPLE1 });
				}
				else if (n == 2)
				{
					outParts.Add(new byte[] { T_TUPLE2 });
				}
				else
				{
					outParts.Add(Concat(new byte[] { T_TUPLE }, EncLen(n)));
				}

				foreach (object x in tuple)
				{
					Encode(x, outParts);
				}
			}
			else if (o is List<object> list)
			{
				int n = list.Count;
				if (n == 0)
				{
					outParts.Add(new byte[] { T_LIST0 });
				}
				else if (n == 1)
				{
					outParts.Add(new byte[] { T_LIST1 });
				}
				else
				{
					outParts.Add(Concat(new byte[] { T_LIST }, EncLen(n)));
				}

				foreach (object x in list)
				{
					Encode(x, outParts);
				}
			}
			else if (o is Dictionary<object, object> dict)
			{
				outParts.Add(Concat(new byte[] { T_DICT }, EncLen(dict.Count)));
				foreach (KeyValuePair<object, object> kv in dict)
				{
					Encode(kv.Value, outParts);
					Encode(kv.Key, outParts);
				}
			}
			else if (o is EveBlueObj obj)
			{
				if (obj.Kind == "instance")
				{
					outParts.Add(new byte[] { T_INSTANCE });
					Encode(obj.Callable, outParts);
					Encode(obj.State, outParts);
				}
				else
				{
					if (obj.Kind == "reduce")
					{
						outParts.Add(new byte[] { T_REDUCE });
						object spec = obj.State == null
							? new object[] { obj.Callable, obj.Args }
							: new object[] { obj.Callable, obj.Args, obj.State };
						Encode(spec, outParts);
					}
					else
					{
						outParts.Add(new byte[] { T_NEWOBJ });
						var head = new List<object> { obj.Callable };
						if (obj.Args is object[] args)
						{
							head.AddRange(args);
						}
						else if (obj.Args != null)
						{
							head.Add(obj.Args);
						}

						object spec = obj.State == null
							? new object[] { head.ToArray() }
							: new object[] { head.ToArray(), obj.State };
						Encode(spec, outParts);
					}

					foreach (object x in obj.ListItems)
					{
						Encode(x, outParts);
					}

					outParts.Add(new byte[] { T_MARK });
					foreach (KeyValuePair<object, object> kv in obj.DictItems)
					{
						Encode(kv.Key, outParts);
						Encode(kv.Value, outParts);
					}

					outParts.Add(new byte[] { T_MARK });
				}
			}
			else
			{
				throw new NotSupportedException("cannot encode " + o.GetType().FullName);
			}
		}

		private static void EncodeInt(long o, List<byte[]> outParts)
		{
			if (o == -1)
			{
				outParts.Add(new byte[] { T_MINUSONE });
			}
			else if (o == 0)
			{
				outParts.Add(new byte[] { T_ZERO });
			}
			else if (o == 1)
			{
				outParts.Add(new byte[] { T_ONE });
			}
			else if (o >= sbyte.MinValue && o <= sbyte.MaxValue)
			{
				outParts.Add(new byte[] { T_INT8, (byte)(sbyte)o });
			}
			else if (o >= short.MinValue && o <= short.MaxValue)
			{
				outParts.Add(Concat(new byte[] { T_INT16 }, BitConverter.GetBytes((short)o)));
			}
			else if (o >= int.MinValue && o <= int.MaxValue)
			{
				outParts.Add(Concat(new byte[] { T_INT32 }, BitConverter.GetBytes((int)o)));
			}
			else
			{
				outParts.Add(Concat(new byte[] { T_INT64 }, BitConverter.GetBytes(o)));
			}
		}

		private static void EncodeDouble(double o, List<byte[]> outParts)
		{
			if (o == 0.0)
			{
				outParts.Add(new byte[] { T_FLOAT0 });
			}
			else
			{
				outParts.Add(Concat(new byte[] { T_FLOAT }, BitConverter.GetBytes(o)));
			}
		}

		private static bool IsInteger(object o, out long value)
		{
			switch (o)
			{
				case int i:
					value = i;
					return true;
				case long l:
					value = l;
					return true;
				case short s:
					value = s;
					return true;
				case byte b:
					value = b;
					return true;
				case sbyte sb:
					value = sb;
					return true;
				default:
					value = 0;
					return false;
			}
		}

		private static byte[] EncLen(int n)
		{
			if (n < 255)
			{
				return new byte[] { (byte)n };
			}

			return Concat(new byte[] { 0xFF }, BitConverter.GetBytes(n));
		}

		private static byte[] Concat(params byte[][] parts)
		{
			int len = parts.Sum(p => p.Length);
			var result = new byte[len];
			int offset = 0;
			foreach (byte[] part in parts)
			{
				Buffer.BlockCopy(part, 0, result, offset, part.Length);
				offset += part.Length;
			}

			return result;
		}

		private static object BytesToStr(byte[] data, int offset, int length)
		{
			try
			{
				return Encoding.UTF8.GetString(data, offset, length);
			}
			catch
			{
				var copy = new byte[length];
				Buffer.BlockCopy(data, offset, copy, 0, length);
				return copy;
			}
		}

		private static long DecodeSignedLittleEndian(byte[] data, int offset, int length)
		{
			if (length <= 0)
			{
				return 0;
			}

			if (length > 8)
			{
				throw new NotSupportedException("integer longer than 8 bytes is not supported");
			}

			ulong result = 0;
			for (int i = 0; i < length; i++)
			{
				result |= (ulong)data[offset + i] << (8 * i);
			}

			int signBits = 64 - length * 8;
			return (long)result << signBits >> signBits;
		}

		/// <summary>
		/// Placeholder for blue-marshal dict keys that are None. .NET dictionaries cannot use null keys.
		/// Encoded back as T_NONE.
		/// </summary>
		private sealed class NullDictKeyMarker
		{
			public static readonly NullDictKeyMarker Instance = new NullDictKeyMarker();
			private NullDictKeyMarker() { }
			public override string ToString() => string.Empty;
		}

		private static object MakeHashable(object k)
		{
			if (k == null)
			{
				return NullDictKeyMarker.Instance;
			}

			if (k is List<object> list)
			{
				return list.ToArray();
			}

			if (k is Dictionary<object, object> dict)
			{
				return dict.OrderBy(kv => kv.Key?.ToString() ?? string.Empty)
					.Select(kv => new object[] { kv.Key, kv.Value })
					.ToArray();
			}

			return k;
		}

		private static object[] AsArray(object o)
		{
			if (o is object[] arr)
			{
				return arr;
			}

			if (o is List<object> list)
			{
				return list.ToArray();
			}

			throw new InvalidDataException("expected sequence, got " + (o?.GetType().Name ?? "null"));
		}
	}
}
