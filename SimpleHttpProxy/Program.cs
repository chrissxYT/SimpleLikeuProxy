using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace SimpleHttpProxy
{
    class Config
    {
        public static int PORT = 3143; //this is an hommage to acng, which uses 3142
    }

    class Program
    {
        static void Main(string[] args)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://*:{Config.PORT}/");
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

        public void ProcessRequest() //if in cache read
        {
            string rawUrl = originalContext.Request.RawUrl;
            if(rawUrl[0] == '/') rawUrl = rawUrl.Substring(1);
            ConsoleUtilities.WriteRequest("Received request for: " + rawUrl);

            var relayRequest = (HttpWebRequest) WebRequest.Create(rawUrl);
            relayRequest.KeepAlive = false;
            relayRequest.Proxy.Credentials = CredentialCache.DefaultCredentials;
            relayRequest.UserAgent = this.originalContext.Request.UserAgent;
           
            var requestData = new RequestState(relayRequest, originalContext);
            relayRequest.BeginGetResponse(ResponseCallBack, requestData);
        }

        static void ResponseCallBack(IAsyncResult asynchronousResult)
        {
            RequestState requestData = asynchronousResult.AsyncState;
            ConsoleUtilities.WriteResponse("Got response from " + requestData.context.Request.RawUrl);
            
            using (var responseFromWebSite = (HttpWebResponse) requestData.webRequest.EndGetResponse(asynchronousResult))
            {
                using (var responseStreamFromWebSite = responseFromWebSite.GetResponseStream())
                {
                    var originalResponse = requestData.context.Response;

                    if (responseFromWebSite.ContentType.Contains("application/x-debian-package"))
                    {
                        int size = responseStreamFromWebSite.Length;
                        byte[] byteArray = new byte[size];
                        responseStreamFromWebSite.Read(byteArray, 0, length);
                        //save to cache
                        var stream = new MemoryStream(byteArray);
                        originalResponse.OutputStream.Write(byteArray, 0, length);
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

    public static class ConsoleUtilities
    {
        public static void WriteRequest(string info)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(info);
            Console.ResetColor();
        }
        public static void WriteResponse(string info)
        {
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(info);
            Console.ResetColor();
        }
    }

    public class RequestState
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