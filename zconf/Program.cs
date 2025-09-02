using Zeroconf;
namespace zconf
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			var zconf = new Zconf();
			await zconf.StartMobdev();
			Console.WriteLine("Hello, World!");
		}
	}
	public class Zconf
	{
		public async Task Start()
		{
			var domains = await ZeroconfResolver.BrowseDomainsAsync();
			foreach(var domain in domains)
			{
				Console.WriteLine($"Domain: {domain.Key}");
				var hosts = await ZeroconfResolver.ResolveAsync(domain.Key);
				foreach(var host in hosts)
				{
					Console.WriteLine($"  Host: {host.DisplayName}");
					foreach(var service in host.Services)
					{
						Console.WriteLine($"    Service: {service.Key}:{service.Value.Port}");
					}
					foreach(var address in host.IPAddresses)
					{
						Console.WriteLine($"    Address: {address}");
					}
				}
			}
		}
		public async Task StartMobdev()
		{
			var domain = "_apple-mobdev2._tcp.local.";
			var hosts = await ZeroconfResolver.ResolveAsync(domain);
			foreach(var host in hosts)
			{
				Console.WriteLine($"  Host: {host.DisplayName}");
				foreach(var service in host.Services)
				{
					Console.WriteLine($"    Service: {service.Key}:{service.Value.Port}");
				}
				foreach(var address in host.IPAddresses)
				{
					Console.WriteLine($"    Address: {address}");
				}
			}
		}
	}
}
