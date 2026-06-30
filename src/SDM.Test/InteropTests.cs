using System;
using System.Threading;
using System.Threading.Tasks;
using SDM.Application;
using SDM.Domain;
using Xunit;

namespace SDM.Test.Interop
{
    public class CSharpFSharpBoundaryTests
    {
        [Fact]
        public async Task AvaloniaUI_CanSafely_PauseOrCancel_FSharpBackgroundTasks()
        {
            // 1. Arrange UI state variables
            using var uiCancellationTokenSource = new CancellationTokenSource();
            long reportedUiProgress = 0;
            string finalState = "Executing";

            // Callback from F# masuk ke mari, mensimulasikan update Progress Bar UI Avalonia
            Action<long> onProgressReceived = (bytes) =>
            {
                reportedUiProgress = bytes;
            };

            // 2. Act: Panggil Engine F# menggunakan Task .NET Standar
            // F# Core WAJIB mengekspos API berupa `Task` kepada C#, bukan FSharpAsync.
            var fsharpTask = DownloadManagerMock.StartDownloadAsync(
                "http://fake.url/file.exe",
                "C:\\Downloads\\file.exe",
                onProgressReceived,
                uiCancellationTokenSource.Token
            );

            // Biarkan stream berjalan sesaat (Simulasi waktu berjalan UI thread)
            await Task.Delay(50);

            // 3. Simulasi user menekan tombol PAUSE di layar UI Avalonia
            uiCancellationTokenSource.Cancel();

            // 4. Await fungsi. 
            // Core F# yg baik (JIT ready) akan menelan OperationCanceledException
            // lalu mengembalikan Result object ke C# UI, agar ViewModel tidak crash berdarah-darah.
            var result = await fsharpTask;

            // 5. Assert Lifecycle UI dan kesatuan state
            if (result.IsCancelled)
            {
                finalState = "Paused";
            }

            Assert.Equal("Paused", finalState);
            Assert.True(reportedUiProgress > 0, "Beberapa byte harusnya sempat mengalir dari F# ke C# sebelum paused.");
        }
    }

    // Mock boundary representation Module from F# when read on C# side
    public static class DownloadManagerMock
    {
        // Parameter Task dengan CancellationToken adalah golden-standard JIT Interop
        public static async Task<InteropResult> StartDownloadAsync(string url, string path, Action<long> progress, CancellationToken ct)
        {
            try
            {
                for (int i = 1; i <= 20; i++)
                {
                    // Memastikan token UI merespon di boundary F# dengan aman loop demi loop
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(10, ct);
                    progress(i * 1024);
                }
                return new InteropResult { IsSuccess = true };
            }
            catch (OperationCanceledException)
            {
                // Engine meng-handle cancellation sbg business logic yang lazim, bukan exception fatal.
                return new InteropResult { IsCancelled = true };
            }
        }
    }

    public class InteropResult
    {
        public bool IsSuccess { get; set; }
        public bool IsCancelled { get; set; }
    }
}
