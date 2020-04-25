using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SimpleHttpProxy.Config;
using static SimpleHttpProxy.Program;

namespace SimpleHttpProxy
{
	static class Config
	{
		public const int PORT = 3143; //this is an hommage to acng, which uses 3142
		public const string ROOT_DIR = "/var/cache/likeu-cacher-ng/";
		public const string CACHE_DIR = ROOT_DIR + "cache/";
		public const string CACHE_DICT = CACHE_DIR + "_dict";
		public static readonly string[] TYPES_TO_CACHE = new[] {
			"application/x-debian-package",
			"application/x-msdos-program",
			"application/zip",
			"application/x-sh",
			"application/x-tar",
		};
		public static readonly Random RANDOM = new Random();

		/// <summary>
		/// this is Environment.NewLine, but better
		/// </summary>
		public static char NewLine => '\n';

		//helper funcs

		public static int FirstIndexOf(this string s, char c)
		{
			for (int i = 0; i < s.Length; i++)
				if (s[i] == c)
					return i;
			return -1;
		}

		public static void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			if (contents == null)
				throw new ArgumentNullException(nameof(contents));
			if (encoding == null)
				throw new ArgumentNullException(nameof(encoding));
			if (path.Length == 0)
				throw new ArgumentException(nameof(path));

			using var w = new StreamWriter(path, false, encoding);
			foreach (string line in contents)
				w.Write(line + NewLine);
		}

		public static string[] ReadAllLines(string path)
		{
			return File.ReadAllText(path, Encoding.UTF8).Split(NewLine);
		}
	}

	class Cache
	{
		static Dictionary<string, string> cachedfiles = new Dictionary<string, string>();

		public Cache()
		{
			if (!Directory.Exists(CACHE_DIR))
				Directory.CreateDirectory(CACHE_DIR);
			if (!File.Exists(CACHE_DICT))
				save();
			load();
		}

		string NewID => RANDOM.Next().ToString("x");

		void load()
		{
			cachedfiles = new Dictionary<string, string>();
			foreach (string l in ReadAllLines(CACHE_DICT))
				if(l.Contains(" "))
					cachedfiles.Add(l.Substring(l.FirstIndexOf(' ')), l.Substring(0, l.FirstIndexOf(' ')));
				else
					Console.WriteLine("Invalid line in cache dict: " + l);
		}

		public void save()
		{
			Stream s = File.Open(CACHE_DICT, FileMode.Create, FileAccess.Write);
			foreach (KeyValuePair<string, string> file in cachedfiles)
				s.Write(Encoding.UTF8.GetBytes($"{file.Value} {file.Key}" + NewLine));
			s.Close();
		}

		public void addfile(string url, byte[] content)
		{
			string id = NewID;
			while (File.Exists(CACHE_DIR + id))
				id = NewID;
			File.WriteAllBytes(CACHE_DIR + id, content);
			cachedfiles.Add(url, id);
			save();
		}

		public bool hasfile(string url) => cachedfiles.ContainsKey(url);

		public string getfile(string url) => CACHE_DIR + cachedfiles[url];
	}

	class Program
	{
		public static Cache cache;

		static void Main(string[] args)
		{
			var listener = new HttpListener();
			listener.Prefixes.Add($"http://*:{PORT}/");
			cache = new Cache();
			listener.Start();
			Console.WriteLine("Listening...");
			while (true)
				new Thread(new Relay(listener.GetContext()).ProcessRequest).Start();
		}
	}

	class Relay
	{
		private readonly HttpListenerContext originalContext;

		public Relay(HttpListenerContext originalContext)
		{
			this.originalContext = originalContext;
		}

		public void ProcessRequest()
		{
			string rawUrl = originalContext.Request.RawUrl;
			Console.WriteLine("Received request for: " + rawUrl);
			if (cache.hasfile(rawUrl))
			{
				Console.WriteLine("Returning from cache…");
				Task.Run(async () =>
				{
					Stream s = File.Open(cache.getfile(rawUrl), FileMode.Open, FileAccess.Read);
					await s.CopyToAsync(originalContext.Response.OutputStream);
					s.Close();
				});
			}
			else
			{
				var relayRequest = (HttpWebRequest)WebRequest.Create(rawUrl);
				relayRequest.KeepAlive = false;
				relayRequest.Proxy.Credentials = CredentialCache.DefaultCredentials;
				relayRequest.UserAgent = originalContext.Request.UserAgent;

				var requestData = new RequestState(relayRequest, originalContext);
				relayRequest.BeginGetResponse(ResponseCallBack, requestData);
			}
		}

		static bool IsCachable(string ct)
		{
			foreach(var s in Config.TYPES_TO_CACHE)
				if(ct.Contains(s))
					return true;
			return false;
		}

		static void ResponseCallBack(IAsyncResult asynchronousResult)
		{
			var requestData = (RequestState) asynchronousResult.AsyncState;
			Console.WriteLine("Got response from " + requestData.context.Request.RawUrl);
			
			using (var responseFromWebSite = (HttpWebResponse) requestData.webRequest.EndGetResponse(asynchronousResult))
			{
				using (var responseStreamFromWebSite = responseFromWebSite.GetResponseStream())
				{
					var originalResponse = requestData.context.Response;
					if (IsCachable(responseFromWebSite.ContentType))
					{
						Console.WriteLine("Saving to cache…");
						long size = responseStreamFromWebSite.Length;
						byte[] byteArray = new byte[size];
						int j;
						for (long i = 0; (j = responseStreamFromWebSite.ReadByte()) != -1; i++)
							byteArray[i] = (byte) j;
						cache.addfile(requestData.context.Request.RawUrl, byteArray);
						foreach (byte b in byteArray)
							originalResponse.OutputStream.WriteByte(b);
					}
					else
					{
						responseStreamFromWebSite.CopyTo(originalResponse.OutputStream);
					}
					originalResponse.OutputStream.Close();
				}
			}
		}
	}

	class RequestState
	{
		public readonly HttpWebRequest webRequest;
		public readonly HttpListenerContext context;

		public RequestState(HttpWebRequest request, HttpListenerContext context)
		{
			webRequest = request;
			this.context = context;
		}
	}

}
