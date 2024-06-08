using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

public class YouTubeDownloader
{
    private static string CleanFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        fileName = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
        return fileName;
    }

    private static async Task DownloadVideoAndAudioSeparately(string videoUrl)
    {
        try
        {
            Console.WriteLine("\nProcurando vídeo...");
            var youtube = new YoutubeClient();

            var video = await youtube.Videos.GetAsync(videoUrl);
            var title = CleanFileName(video?.Title);
            var duration = video.Duration; 
            Console.WriteLine($"Vídeo encontrado: \"{title}\"");
            Console.WriteLine($"Duração: {duration}\n");

            Console.WriteLine("Processando...");

            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

            var audioStreamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
            var videoStreamInfo = streamManifest
                .GetVideoOnlyStreams()
                .Where(s => s.Container == Container.Mp4)
                .GetWithHighestVideoQuality();

            if (audioStreamInfo == null || videoStreamInfo == null)
            {
                Console.WriteLine("Vídeo ou áudio não encontrado, tente novamente.");
                return;
            }

            var audioFilePath = $"cache\\audio.{audioStreamInfo.Container}";
            var videoFilePath = $"cache\\video.{videoStreamInfo.Container}";
            var outputFilePath = $"videos\\{title}.mp4";

            if (File.Exists(outputFilePath))
            {
                Console.WriteLine("Vídeo já baixado.");
                return;
            }

            var progressHandler = new Progress<(string message, double progress, double speed)>(p => DisplayProgress(p.message, p.progress, p.speed));

            var downloadAudioTask = DownloadStreamAsync(youtube, audioStreamInfo, audioFilePath, progressHandler);
            var downloadVideoTask = DownloadStreamAsync(youtube, videoStreamInfo, videoFilePath, progressHandler);

            await Task.WhenAll(downloadAudioTask, downloadVideoTask);

            Console.WriteLine("\nDownload concluído!\n");

            Console.WriteLine("Muxing em andamento:");
            await MuxVideoAndAudioAsync(videoFilePath, audioFilePath, outputFilePath, progressHandler);

            Console.WriteLine("Muxing concluído!\n");

            await Task.Delay(500);

            File.Delete(audioFilePath);
            File.Delete(videoFilePath);

            Console.WriteLine("Vídeo baixado com sucesso!\n");

            bool shouldContinue = true;
            while (shouldContinue)
            {
                Console.Write("Deseja baixar outro vídeo? (s/n): ");
                var choice = Console.ReadLine();
                if (choice.ToLower() == "s")
                {
                    Console.Write("\nInsira a URL do Vídeo: ");
                    var nextVideoUrl = Console.ReadLine();
                    await DownloadVideoAndAudioSeparately(nextVideoUrl);
                    shouldContinue = false;
                }
                else if (choice.ToLower() == "n")
                {
                    Console.WriteLine("\nObrigado por usar.\n");
                    shouldContinue = false;
                }
                else
                {
                    Console.WriteLine("Opção inválida. Por favor, escolha 's' para sim ou 'n' para não.\n");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ocorreu um erro: {ex.Message}");
        }
    }

    private static async Task DownloadStreamAsync(YoutubeClient youtube, IStreamInfo streamInfo, string filePath, IProgress<(string message, double progress, double speed)> progressHandler)
    {
        var lastBytesDownloaded = 0L;
        var lastUpdateTime = DateTime.Now;

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, progress: new Progress<double>(progress =>
        {
            stopwatch.Stop();

            var currentTime = DateTime.Now;
            var timeDifference = (currentTime - lastUpdateTime).TotalSeconds;
            var bytesDownloaded = new FileInfo(filePath).Length;
            var bytesDelta = bytesDownloaded - lastBytesDownloaded;

            if (timeDifference >= 1) 
            {
                if (bytesDelta > 0)
                {
                    var speed = bytesDelta / timeDifference / (1024 * 1024); 
                    progressHandler.Report(("Download em andamento:", progress, speed));
                }

                lastBytesDownloaded = bytesDownloaded;
                lastUpdateTime = currentTime;
            }

            if (progress >= 1) 
            {
                progressHandler.Report(("Download em andamento:", 1, 0));
            }

            stopwatch.Start();
        }));

        stopwatch.Stop();
    }

    private static async Task MuxVideoAndAudioAsync(string videoFilePath, string audioFilePath, string outputFilePath, IProgress<(string message, double progress, double speed)> progressHandler)
    {
        var ffmpegPath = "ffmpeg-7.0.1-full_build\\bin\\ffmpeg.exe";

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{videoFilePath}\" -i \"{audioFilePath}\" -c copy \"{outputFilePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = Process.Start(startInfo))
        {
            process.WaitForExit();
        }

        await Task.CompletedTask;
    }

    private static void DisplayProgress(string message, double progress, double speed)
    {
        const int barWidth = 10;
        int progressWidth = (int)(barWidth * progress);
        string progressBar = new string('#', progressWidth) + new string('-', barWidth - progressWidth);
        Console.Write($"\r{message} ({speed:F2} MB/s) [{progressBar}] {progress:P1} ");
    }

    public static async Task Main(string[] args)
    {
        Directory.CreateDirectory("cache");
        Directory.CreateDirectory("videos");

        Console.Write("\nInsira a URL do Vídeo: ");
        var videoUrl = Console.ReadLine();

        await DownloadVideoAndAudioSeparately(videoUrl);
    }
}
