using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
namespace UnityDAP
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			foreach (var arg in args)
			{
				Console.WriteLine(arg);
			}
			try
			{
				int port = 56068;
				string address = "127.0.0.1";
				await SimpleTcpClient.Connect(address, port);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
		}
	}
	class SimpleTcpClient
	{
		public static async Task Connect(string address, int port)
		{
			using TcpClient client = new();
			client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			await client.ConnectAsync(address, port);
			// https://www.mono-project.com/docs/advanced/runtime/docs/soft-debugger-wire-format/
			using var stream = client.GetStream();
			{
				var bytes = Encoding.UTF8.GetBytes("DWP-Handshake");
				await stream.WriteAsync(bytes);
				var buffer = new byte[13];
				var bytesRead = await stream.ReadAsync(buffer);
				var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
				Console.WriteLine($"{response}:{bytesRead}bytes");
			}
			{
				var buffer = new byte[1024];
				var header = new DwpCommandPacketHeader(0, 1, 0, 1, 1);
				await stream.WriteAsync(header.ToBytes());
				var bytesRead = await stream.ReadAsync(buffer);
				var reply = new DwpReplyPacketHeader(buffer);
				Console.WriteLine($"{reply}:{bytesRead}bytes");
			}
			stream.Close();
			client.Close();
		}
	}
	public class DwpCommandPacketHeader(uint inLength, uint inId, byte inFlags, byte inCommandSet, byte inCommand)
	{
		public uint length = inLength;
		public uint id = inId;
		public byte flags = inFlags;
		public byte commandSet = inCommandSet;
		public byte command = inCommand;
		const int headerSize = 11;
		public byte[] ToBytes()
		{
			var bytes = new byte[headerSize];
			BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), length);
			BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), id);
			bytes[8] = flags;
			bytes[9] = commandSet;
			bytes[10] = command;
			return bytes;
		}
		public override string ToString()
		{
			return $"length:{length}, id:{id}, flags:{flags}, commandSet:{commandSet}, command:{command}";
		}
	}
	public class DwpReplyPacketHeader
	{
		public uint length;
		public uint id;
		public byte flags;
		public ushort errorCode;
		public byte[] data = [];
		const int headerSize = 11;
		public DwpReplyPacketHeader(byte[] bytes)
		{
			length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
			id = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
			flags = bytes[8];
			errorCode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(9));
			data = [.. bytes.Skip(headerSize).Take((int)length - headerSize)];
		}
		public override string ToString()
		{
			var str = BitConverter.ToString(data);// data.Length > 0 ? Encoding.UTF8.GetString(data) : "No data";
			return $"length:{length}, id:{id}, flags:{flags}, errorCode:{errorCode}\n{str}";
		}
	}
}
