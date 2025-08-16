using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
namespace UnityDAP
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			foreach(var arg in args)
			{
				Console.WriteLine(arg);
			}
			var unityProcesses = new UnityProcessList();
			await unityProcesses.Create();
		}
	}
	public partial class UnityProcessList
	{
		readonly List<UnityProcess> unityProcesses = [];
		readonly Regex parseUnityInfo = MyRegex();
		public override string ToString()
		{
			var procStr = string.Join("\n", unityProcesses); ;
			return $"unityProcesses:\n{procStr}\n";
		}
		public async Task Create()
		{
			ScanProcess();
			if(!HasAddress())
			{
				await UnityPlayerInfo();
			}
			Console.WriteLine(ToString());
		}
		async Task UnityPlayerInfo()
		{
			if(unityProcesses.Count == 0) { return; }
			var unityAddress = IPAddress.Parse("225.0.0.222");
			var unityPorts = new[] { 54997, 34997, 57997, 58997 };
			var ipAddresses = IPAddressList();
			var cts = new CancellationTokenSource();
			List<Task> tasks = [];
			List<UdpSocketInfo> sockets = [];
			foreach(var ipAddress in ipAddresses)
			{
				foreach(var unityPort in unityPorts)
				{
					var socket = new UdpSocketInfo(cts, Receive, unityAddress, unityPort, ipAddress);
					sockets.Add(socket);
					tasks.Add(Task.Run(socket.StartReceivingLoop));
				}
			}
			int timeoutMilliseconds = 5000;
			var timeoutTask = Task.Delay(timeoutMilliseconds);
			await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);
			if(timeoutTask.IsCompleted)
			{
				Console.WriteLine($"timeout {timeoutMilliseconds}ms");
			}
			foreach(var socket in sockets)
			{
				socket.Dispose();
			}
		}
		bool Receive(byte[] bytes)
		{
			foreach(var unityProcess in unityProcesses)
			{
				if(unityProcess.runtime != UnityProcess.Runtime.Player || unityProcess.hasAddress)
				{
					continue;
				}
				var infoText = Encoding.UTF8.GetString(bytes);
				var match = parseUnityInfo.Match(infoText);
				if(!match.Success) { continue; }
				var projectName = match.Groups["ProjectName"].Value;
				if(projectName.CompareTo(unityProcess.name) != 0) { continue; }
				var guid = match.Groups["Guid"].Value;
				var ip = match.Groups["IP"].Value;
				unityProcess.GuidToPorts(ip, int.Parse(guid));
			}
			return HasAddress();
		}
		bool HasAddress()
		{
			foreach(var unityProcess in unityProcesses)
			{
				if(!unityProcess.hasAddress)
				{
					return false;
				}
			}
			return true;
		}
		void ScanProcess()
		{
			var procs = Process.GetProcesses();
			foreach(var process in procs)
			{
				var runtime = UnityProcess.Runtime.None;
				if(IsWindowsApp(process))
				{
					runtime = UnityProcess.Runtime.Player;
				}
				if(IsEditor(process))
				{
					runtime = UnityProcess.Runtime.Editor;
				}
				if(runtime == UnityProcess.Runtime.None) { continue; }
				unityProcesses.Add(new(process, runtime));
			}
		}
		static bool IsEditor(Process process)
		{
			if(process.MainWindowHandle == IntPtr.Zero) { return false; }
			return process.ProcessName == "Unity";
		}
		static bool IsWindowsApp(Process process)
		{
			if(process.MainWindowHandle == IntPtr.Zero || process.MainModule == null) { return false; }
			var fileName = process.MainModule.FileName;
			var directory = Path.GetDirectoryName(fileName);
			if(directory == null) { return false; }
			if(!File.Exists(Path.Combine(directory, "UnityPlayer.dll"))) { return false; }
			return true;
		}
		static List<IPAddress> IPAddressList()
		{
			var ipAddresses = new List<IPAddress>();
			var networkList = NetworkInterface.GetAllNetworkInterfaces();
			foreach(var network in networkList)
			{
				if(!network.SupportsMulticast || network.NetworkInterfaceType == NetworkInterfaceType.Loopback || network.OperationalStatus != OperationalStatus.Up)
				{
					continue;
				}
				var properties = network.GetIPProperties();
				if(properties == null) { continue; }
				var unicastAddresses = properties.UnicastAddresses;
				foreach(var address in unicastAddresses)
				{
					if(address.Address.AddressFamily == AddressFamily.InterNetwork)
					{
						ipAddresses.Add(address.Address);
					}
				}
			}
			return ipAddresses;
		}
		[GeneratedRegex(@"\[IP\]\s(?<IP>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s\[Port\]\s(?<Port>\d+)\s\[Flags\]\s(?<Flags>\d+)\s\[Guid\]\s(?<Guid>\d+)\s\[EditorId\]\s(?<EditorId>\d+)\s\[Version\]\s(?<Version>\d+)\s\[Id\]\s(?<Id>.*?)\s\[Debug\]\s(?<Debug>\d+)\s\[PackageName\]\s(?<PackageName>.*?)\s\[ProjectName\]\s(?<ProjectName>.*)")]
		private static partial Regex MyRegex();
	}
	public class UnityProcess
	{
		public enum Platform
		{
			Windows,
			Linux,
			OSX,
			iOS,
			Android,
			WebGL
		}
		public enum Runtime
		{
			None,
			Editor,
			Player
		}
		public int Id { get; set; }
		public string address { get; set; } = string.Empty;
		public int debugPort { get; set; }
		public int messagePort { get; set; }
		public string name { get; set; } = string.Empty;
		public Runtime runtime { get; set; }
		public bool hasAddress => !string.IsNullOrEmpty(address);
		public override string ToString() => $" {name}:{Id} ,address:{address} ,debug:{debugPort}, message:{messagePort}, runtime:{runtime}";
		public UnityProcess(Process process, Runtime inRuntime)
		{
			Id = process.Id;
			name = process.MainWindowTitle;
			debugPort = GetDebugPort();
			messagePort = GetMessagePort();
			runtime = inRuntime;
			if(runtime == Runtime.Editor)
			{
				address = "127.0.0.1";
			}
		}
		public void GuidToPorts(string inAddress, int guid)
		{
			address = inAddress;
			debugPort = 56000 + (guid % 1000);
			messagePort = GetMessagePort();
		}
		int GetDebugPort()
		{
			return 56000 + (Id % 1000);
		}
		int GetMessagePort()
		{
			return GetDebugPort() + 2;
		}
	}
	public class UdpSocketInfo(CancellationTokenSource cts, UdpSocketInfo.Receive receive, IPAddress? address, int port, IPAddress? iface = null, int ttl = 4) : IDisposable
	{
		UdpClient? udpClient = CreateUdpClient(address, port, iface, ttl);
		public delegate bool Receive(byte[] bytes);
		readonly Receive receive = receive;
		public async Task StartReceivingLoop()
		{
			if(udpClient == null) { return; }
			try
			{
				while(!cts.Token.IsCancellationRequested)
				{
					var result = await udpClient.ReceiveAsync(cts.Token);
					if(receive(result.Buffer))
					{
						cts.Cancel();
					}
				}
			}
			catch(OperationCanceledException)
			{
			}
			catch(Exception ex)
			{
				Console.WriteLine($"error: {ex.Message}");
			}
			finally
			{
				Dispose(true);
			}
		}
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		protected virtual void Dispose(bool disposing)
		{
			if(disposing && udpClient != null)
			{
				cts.Cancel();
				udpClient.Close();
				udpClient = null;
			}
		}
		static UdpClient CreateUdpClient(IPAddress? address, int port, IPAddress? iface = null, int ttl = 4)
		{
			var udpClient = new UdpClient();
			udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
			if(address != null)
			{
				if(iface != null)
				{
					udpClient.JoinMulticastGroup(address, iface);
				}
				else
				{
					udpClient.JoinMulticastGroup(address);
				}
			}
			udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);
			return udpClient;
		}
	}
}
