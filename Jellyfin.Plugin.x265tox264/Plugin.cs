using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.x265tox264.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.x265tox264
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, ILibraryPostScanTask
    {
        public override string Name => "x265 to x264";
        public override Guid Id => Guid.Parse("18C22B64-6624-4D72-A74A-0F1EE7D34CCA");

        public ILibraryManager LibraryManager { get; set; }
        public ILogger Logger { get; set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILibraryManager libraryManager, ILoggerFactory loggerFactory) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            LibraryManager = libraryManager;
            Logger = loggerFactory.CreateLogger("x265tox264");
        }

        public static Plugin Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = string.Format("{0}.Configuration.configPage.html", GetType().Namespace)
                }
            };
        }

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                IEnumerable<Video> transcodableItems = LibraryManager.GetItemList(new InternalItemsQuery()).Where(item => item is Video).Cast<Video>().Where(video => ShouldTranscode(video));
                double totalItems = transcodableItems.Count();
                double processedItems = 0;

                Logger.LogInformation((int)totalItems + " files to transcode");

                foreach (Video item in transcodableItems)
                {
                    await Transcode(item);
                    processedItems += 1;
                    progress.Report(processedItems / totalItems);

                    if (cancellationToken.IsCancellationRequested) break;
                }
            }, cancellationToken);
        }

        HashSet<string> _currentlyTranscoding = new();
        public async Task Transcode(Video item)
        {
            if (!ShouldTranscode(item)) return;

            Logger.LogInformation("Transcoding video at: " + item.Path);

            string newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(item.Path), System.IO.Path.GetFileNameWithoutExtension(item.Path) + "-tox264" + System.IO.Path.GetExtension(item.Path));

            Process ffmpegProcess = Execute("ffmpeg", "-i \"" + item.Path + "\" -c:v libx264 -crf 23 -c:a aac -b:a 128k \"" + newPath + "\"");

            _currentlyTranscoding.Add(item.Path);
            await ffmpegProcess.WaitForExitAsync();
            _currentlyTranscoding.Remove(item.Path);

            if (ffmpegProcess.ExitCode == 0) System.IO.File.Delete(item.Path);
            else System.IO.File.Delete(newPath);
        }

        public bool ShouldTranscode(Video item) => item != null && item.Path != null && item.GetDefaultVideoStream().Codec == "hevc" && !_currentlyTranscoding.Contains(item.Path);

        public static Process Execute(string command, string arguments) => Process.Start(new ProcessStartInfo(command, arguments) { UseShellExecute = true });
    }
}
