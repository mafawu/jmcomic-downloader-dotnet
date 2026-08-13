using JmComic.Core.Logging;

namespace JmComic.Core.Tests;

public class FileLoggerTests
{
    private static string NewDir() =>
        Path.Combine(Path.GetTempPath(), "jm-log-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_Formatted_Lines_With_Exception_Details()
    {
        var dir = NewDir();
        try
        {
            using (var logger = new FileLogger(dir))
            {
                logger.Info("hello");
                logger.Error("boom", new InvalidOperationException("detail message"));
            }

            var file = Directory.GetFiles(dir, "app-*.log").Single();
            var text = File.ReadAllText(file);
            Assert.Contains("[INFO] hello", text);
            Assert.Contains("[ERROR] boom", text);
            Assert.Contains("InvalidOperationException: detail message", text);
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Rolls_To_New_File_On_Day_Change()
    {
        var dir = NewDir();
        try
        {
            var now = new DateTime(2026, 8, 13, 10, 0, 0);
            Func<DateTime> clock = () => now;
            using (var logger = new FileLogger(dir, 7, clock))
            {
                logger.Info("day1");
                now = now.AddDays(1);
                logger.Info("day2");
            }

            Assert.True(File.Exists(Path.Combine(dir, "app-20260813.log")));
            Assert.True(File.Exists(Path.Combine(dir, "app-20260814.log")));
            Assert.Contains("day1", File.ReadAllText(Path.Combine(dir, "app-20260813.log")));
            Assert.Contains("day2", File.ReadAllText(Path.Combine(dir, "app-20260814.log")));
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Removes_Old_Logs_Beyond_Retention_Days()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "app-20200101.log"), "old");
            File.WriteAllText(Path.Combine(dir, "app-20260110.log"), "old2");
            File.WriteAllText(Path.Combine(dir, "app-20260812.log"), "recent");
            File.WriteAllText(Path.Combine(dir, "not-a-log.txt"), "keep");

            using var logger = new FileLogger(dir, 7, () => new DateTime(2026, 8, 13));

            Assert.False(File.Exists(Path.Combine(dir, "app-20200101.log")));
            Assert.False(File.Exists(Path.Combine(dir, "app-20260110.log")));
            Assert.True(File.Exists(Path.Combine(dir, "app-20260812.log")));
            Assert.True(File.Exists(Path.Combine(dir, "not-a-log.txt")));
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Concurrent_Writes_Do_Not_Lose_Lines()
    {
        var dir = NewDir();
        try
        {
            var logger = new FileLogger(dir);
            var tasks = Enumerable.Range(0, 50)
                .Select(i => Task.Run(() => logger.Info($"line {i}")))
                .ToArray();
            await Task.WhenAll(tasks);
            logger.Dispose(); // 先关闭写句柄，避免与读取共享冲突

            var file = Directory.GetFiles(dir, "app-*.log").Single();
            var lineCount = File.ReadLines(file).Count(l => l.Contains("[INFO] line "));
            Assert.Equal(50, lineCount);
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }
}

