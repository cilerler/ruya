using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Formatting;
using System.Reflection;
using System.Web.Http;
using Microsoft.Owin;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;
using Microsoft.Owin.StaticFiles.ContentTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Owin;
using Ruya.Host;
using Ruya.Diagnostics;

[assembly: OwinStartup(typeof(Startup))]

namespace Ruya.Host
{
    public static class Startup
    {
        public class CustomContentTypeProvider : FileExtensionContentTypeProvider
        {
            public CustomContentTypeProvider()
            {
                Mappings.Add(".svclog", "text/xml");
            }
        }

        public static void Configuration(IAppBuilder appBuilder)
        {
            Ruya.SignalR.Startup.Configuration(appBuilder);
            
            var paths = new Dictionary<string, string>
                        {
                            {
                                "wwwroot", "/ui"
                            },
                            {
                                "_logs","/logs"
                            }
                        };

            string rootDirectory = Directory.GetCurrentDirectory();
            foreach (KeyValuePair<string, string> path in paths)
            {
                string directory = Path.Combine(rootDirectory, path.Key);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var fileServerOptions = new FileServerOptions
                                        {
                                            EnableDirectoryBrowsing = true,
                                            FileSystem = new PhysicalFileSystem(directory),
                                            RequestPath = new PathString(path.Value)
                                        };
                fileServerOptions.StaticFileOptions.ContentTypeProvider = new CustomContentTypeProvider();
                appBuilder.UseFileServer(fileServerOptions);
            }

            using (var httpConfiguration = new HttpConfiguration())
            {
                var defaultJsonSettings = new JsonSerializerSettings
                                      {
                                          Formatting = Formatting.Indented,
                                          ContractResolver = new CamelCasePropertyNamesContractResolver(),
                                          Converters = new List<JsonConverter>
                                                       {
                                                           new StringEnumConverter
                                                           {
                                                               CamelCaseText = true
                                                           },
                                                       }
                                      };
                JsonConvert.DefaultSettings = () => defaultJsonSettings;

                //Specify JSON as the default media type
                httpConfiguration.Formatters.Clear();
                httpConfiguration.Formatters.Add(new JsonMediaTypeFormatter());
                httpConfiguration.Formatters.JsonFormatter.SerializerSettings = defaultJsonSettings;

                httpConfiguration.Routes.MapHttpRoute(name: "DefaultApi", routeTemplate: "api/{controller}/{action}/{id}", defaults: new
                                                                                                                                     {
                                                                                                                                         id = RouteParameter.Optional
                                                                                                                                     });

                appBuilder.UseWebApi(httpConfiguration);
            }

            // following part shouldn’t be requried, but I ran into some issues that where it wasn’t set, SignalR was throwing null reference, but I’m guessing this will be fixed sooner or later.
            Assembly executingAssembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersionInfo = executingAssembly.GetFileVersionInfo();
            appBuilder.Properties["host.AppName"] = fileVersionInfo.ProductName;

        }
    }
}