using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.CommandLine;
using Zeroconf;
namespace UnityTargets
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			try
			{
				await CreateProcess(args);
			}
			catch(Exception ex)
			{
				Console.WriteLine(ex);
			}
		}
		static async Task CreateProcess(string[] args)
		{
			var timeoutOption = new Option<int>("--timeout") { Description = "UDP Timeout in milliseconds", DefaultValueFactory = _ => 1000 };
			var excludeEditor = new Option<bool>("--exclude-editor") { Description = "Exclude Editor processes", DefaultValueFactory = _ => false };
			var excludePlayer = new Option<bool>("--exclude-player") { Description = "Exclude Player processes", DefaultValueFactory = _ => false };
			var excludeiOS = new Option<bool>("--exclude-ios") { Description = "Exclude iOS Player processes", DefaultValueFactory = _ => false };
			var rootCommand = new RootCommand("UnityTargets")
			{
				timeoutOption,
				excludeEditor,
				excludePlayer,
				excludeiOS,
			};
			rootCommand.SetAction(async result =>
			{
				var unityProcesses = new UnityProcessList();
				await unityProcesses.Create(new()
				{
					excludeEditor = result.GetValue(excludeEditor),
					excludePlayer = result.GetValue(excludePlayer),
					excludeiOS = result.GetValue(excludeiOS),
					timeoutMilliseconds = result.GetValue(timeoutOption),
				});
			});
			rootCommand.Parse(args).Invoke();
		}
	}
	public partial class UnityProcessList
	{
		public class Options
		{
			public bool excludeEditor { get; set; }
			public bool excludePlayer { get; set; }
			public bool excludeiOS { get; set; }
			public int timeoutMilliseconds { get; set; }
		}
		readonly List<UnityProcess> unityProcesses = [];
		readonly Regex parseUnityInfo = MyRegex();
		public Options options { get; set; } = new();
		public async Task Create(Options inOptions)
		{
			options = inOptions;
			if(!options.excludeEditor) { ScanProcess(); }
			if(!options.excludePlayer) { await UnityPlayerInfo(inOptions.timeoutMilliseconds); }
			if(!options.excludeiOS) { await iOSRemoteUnityProcess(); }
			var json = JsonSerializer.Serialize(unityProcesses);
			Console.WriteLine(json);
		}
		async Task UnityPlayerInfo(int timeoutMilliseconds)
		{
			var unityAddress = IPAddress.Parse("225.0.0.222");
			var unityPorts = new[] { 54997, 34997, 57997, 58997 };
			var cts = new CancellationTokenSource();
			List<Task> tasks = [];
			List<UdpSocketInfo> sockets = [];
			var ipAddresses = IPAddressList();
			foreach(var address in ipAddresses)
			{
				foreach(var unityPort in unityPorts)
				{
					var socket = new UdpSocketInfo(cts, Receive, unityAddress, unityPort, address);
					sockets.Add(socket);
					tasks.Add(Task.Run(socket.StartReceivingLoop));
				}
			}
			var timeoutTask = Task.Delay(timeoutMilliseconds);
			await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);
			foreach(var socket in sockets)
			{
				socket.Dispose();
			}
		}
		bool Receive(byte[] bytes)
		{
			var infoText = Encoding.UTF8.GetString(bytes);
			var match = parseUnityInfo.Match(infoText);
			if(!match.Success) { return false; }
			var projectName = match.Groups["ProjectName"].Value;
			var guidStr = match.Groups["Guid"].Value;
			var ip = match.Groups["IP"].Value;
			var id = match.Groups["Id"].Value;
			if(long.TryParse(guidStr, out var guid))
			{
				var name = $"{projectName.Trim('\0')} {id}";
				var process = new UnityProcess(ip, name, guid, infoText);
				if(Contains(process)) { return true; }
				unityProcesses.Add(process);
			}
			return false;
		}
		void ScanProcess()
		{
			var procs = Process.GetProcesses();
			foreach(var process in procs)
			{
				if(process.ProcessName == "Unity" && process.BasePriority == 8)
				{
					unityProcesses.Add(new(process));
				}
			}
		}
		bool Contains(UnityProcess inProcess)
		{
			foreach(var unityProcess in unityProcesses)
			{
				if(unityProcess.Compare(inProcess))
				{
					return true;
				}
			}
			return false;
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
		static async Task<List<string>> SearchZconf(string domain)
		{
			var hosts = await ZeroconfResolver.ResolveAsync(domain);
			List<string> addresses = [];
			foreach(var host in hosts)
			{
				addresses.Add(host.IPAddress);
			}
			return addresses;
		}
		async Task iOSRemoteUnityProcess()
		{
			var zconf = await SearchZconf("_apple-mobdev2._tcp.local.");
			foreach(var address in zconf)
			{
				var proc = new UnityProcess("iOSPlayer", address, 56000, 55000);
				unityProcesses.Add(proc);
			}
		}
	}
	public class UnityProcess
	{
		public string address { get; set; } = string.Empty;
		public int debugPort { get; set; }
		public int messagePort { get; set; }
		public int pid { get; set; }
		public string name { get; set; } = string.Empty;
		public string runtime { get; set; }
		public string description { get; set; } = string.Empty;
		public UnityProcess(Process process)
		{
			debugPort = GetDebugPort(process.Id);
			messagePort = GetMessagePort();
			address = "127.0.0.1";
			runtime = "Editor";
			pid = process.Id;
			if(process.MainModule != null)
			{
				var processName = Path.GetFileNameWithoutExtension(process.MainModule.ModuleName).Trim('\0');
				name = $"{processName} {runtime}, pid:{pid}, debugPort:{debugPort}";
			}
		}
		public UnityProcess(string inAddress, string inName, long guid, string infoText)
		{
			address = inAddress;
			debugPort = 56000 + (int)(guid % 1000);
			messagePort = GetMessagePort();
			runtime = "Player";
			description = infoText;
			name = $"{inName}, debugPort:{debugPort}";
		}
		public UnityProcess(string inAddress, string inName, int inDebugPort, int inMessagePort)
		{
			address = inAddress;
			debugPort = inDebugPort;
			messagePort = inMessagePort;
			runtime = "Player";
			name = $"{inName.Trim('\0')}, debugPort:{debugPort}";
		}
		public bool Compare(UnityProcess inProcess)
		{
			return name == inProcess.name && address == inProcess.address && debugPort == inProcess.debugPort && messagePort == inProcess.messagePort;
		}
		static int GetDebugPort(int inId)
		{
			return 56000 + (inId % 1000);
		}
		int GetMessagePort()
		{
			return debugPort + 2;
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
				Console.WriteLine($"error: {ex}");
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

