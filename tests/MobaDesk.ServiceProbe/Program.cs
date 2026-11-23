using System.Diagnostics;
using System.Text;
using MobaDesk.Models;
using MobaDesk.Services;
using MobaDesk.Services.FileSystems;

namespace MobaDesk.ServiceProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--update")
        {
            return await ProbeUpdateAsync(args[1], args.Contains("--download"));
        }

        var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
        var port = int.TryParse(args.ElementAtOrDefault(1), out var parsedPort) ? parsedPort : 2222;
        var user = args.ElementAtOrDefault(2) ?? "root";
        var password = args.ElementAtOrDefault(3) ?? "MobaTest!234";

        var profile = new ConnectionProfile
        {
            Name = "probe",
            Protocol = ProtocolKind.Ssh,
            Host = host,
            Port = port,
            Username = user,
            Auth = AuthKind.Password
        };
        var credentials = new SshCredentials(password);
        var failures = 0;

        // The probe is an explicit test harness: trust the test host without a UI prompt.
        SshAuth.HostKeyVerifier = info =>
        {
            Console.WriteLine($"   trusting host key {info.Host}:{info.Port} {info.Fingerprint}");
            return true;
        };

        Console.WriteLine($"== SSH {user}@{host}:{port} ==");
        var session = new SshSession();
        var output = new StringBuilder();
        session.Output += data =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(data));
            }
        };
        session.Closed += message => Console.WriteLine($"!! SSH closed: {message}");
        session.HostKey += fp => Console.WriteLine($"   host key: {fp}");

        try
        {
            await session.ConnectAsync(profile, credentials, 120, 30);
            Console.WriteLine($"   connected={session.IsConnected}");
            await Task.Delay(800);
            session.WriteText("echo MOBADESK_PROBE_OK\n");
            await Task.Delay(1200);
            session.WriteText("echo TERM=$TERM; uname -srm\n");
            await Task.Delay(1200);

            string text;
            lock (output)
            {
                text = output.ToString();
            }

            Console.WriteLine("--- terminal output (escaped) ---");
            Console.WriteLine(text.Replace("\u001b", "<ESC>"));
            Console.WriteLine("--- end ---");
            if (!text.Contains("MOBADESK_PROBE_OK", StringComparison.Ordinal))
            {
                Console.WriteLine("FAIL: terminal did not echo the probe marker.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS: interactive shell roundtrip.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: SSH connect: " + ex);
            failures++;
        }

        Console.WriteLine("== SFTP ==");
        SftpRemoteFileSystem? sftp = null;
        try
        {
            sftp = new SftpRemoteFileSystem(profile);
            await sftp.ConnectAsync(credentials, CancellationToken.None);
            var initial = sftp.InitialPath;
            var entries = await sftp.ListAsync(initial, CancellationToken.None);
            Console.WriteLine($"   home={initial}, entries={entries.Count}");
            Console.WriteLine("   sample: " + string.Join(", ",
                entries.Take(5).Select(e => e.Name + (e.IsDirectory ? "/" : ""))));

            var local = Path.Combine(Path.GetTempPath(), "mobadesk_probe_" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(local, "MobaDesk SFTP roundtrip " + DateTime.Now.ToString("O"));
            var remote = RemotePath.Combine(initial, "mobadesk_probe.txt");
            await sftp.UploadAsync(local, remote, new Progress<double>(), CancellationToken.None);
            var remoteEntry = (await sftp.ListAsync(initial, CancellationToken.None))
                .First(e => e.Name == "mobadesk_probe.txt");
            var downloaded = local + ".back";
            await sftp.DownloadAsync(remoteEntry, downloaded, new Progress<double>(), CancellationToken.None);
            var content = await File.ReadAllTextAsync(downloaded);
            Console.WriteLine("   roundtrip=" + content);
            if (content != await File.ReadAllTextAsync(local))
            {
                Console.WriteLine("FAIL: SFTP content mismatch.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS: SFTP upload/download.");
            }

            await sftp.DeleteAsync(remoteEntry, CancellationToken.None);
            File.Delete(local);
            File.Delete(downloaded);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: SFTP: " + ex);
            failures++;
        }
        finally
        {
            if (sftp != null)
            {
                await sftp.DisposeAsync();
            }
        }

        Console.WriteLine("== FTP ==");
        FtpRemoteFileSystem? ftp = null;
        try
        {
            var ftpPort = int.TryParse(args.ElementAtOrDefault(4), out var parsedFtpPort) ? parsedFtpPort : 2121;
            var ftpUser = args.ElementAtOrDefault(5) ?? "ftpuser";
            var ftpPassword = args.ElementAtOrDefault(6) ?? "FtpTest!234";
            var ftpProfile = new ConnectionProfile
            {
                Name = "ftp-probe",
                Protocol = ProtocolKind.Ftp,
                Host = host,
                Port = ftpPort,
                Username = ftpUser,
                Auth = AuthKind.Password
            };
            var ftpCredentials = new SshCredentials(ftpPassword);

            ftp = new FtpRemoteFileSystem(ftpProfile);
            await ftp.ConnectAsync(ftpCredentials, CancellationToken.None);
            var initial = ftp.InitialPath;
            var listing = await ftp.ListAsync(initial, CancellationToken.None);
            Console.WriteLine($"   home={initial}, entries={listing.Count}");

            var local = Path.Combine(Path.GetTempPath(), "mobadesk_ftp_" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(local, "MobaDesk FTP roundtrip " + DateTime.Now.ToString("O"));
            var remote = RemotePath.Combine(initial, "mobadesk_ftp_probe.txt");
            await ftp.UploadAsync(local, remote, new Progress<double>(), CancellationToken.None);
            var remoteEntry = (await ftp.ListAsync(initial, CancellationToken.None))
                .First(e => e.Name == "mobadesk_ftp_probe.txt");
            var downloaded = local + ".back";
            await ftp.DownloadAsync(remoteEntry, downloaded, new Progress<double>(), CancellationToken.None);
            var content = await File.ReadAllTextAsync(downloaded);
            Console.WriteLine("   roundtrip=" + content);
            if (content != await File.ReadAllTextAsync(local))
            {
                Console.WriteLine("FAIL: FTP content mismatch.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS: FTP upload/download.");
            }

            await ftp.DeleteAsync(remoteEntry, CancellationToken.None);
            File.Delete(local);
            File.Delete(downloaded);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: FTP: " + ex);
            failures++;
        }
        finally
        {
            if (ftp != null)
            {
                await ftp.DisposeAsync();
            }
        }

        Console.WriteLine("== RDP FILE ==");
        try
        {
            var rdpProfile = new ConnectionProfile
            {
                Name = "rdp-probe",
                Protocol = ProtocolKind.Rdp,
                Host = "example.test",
                Port = 3389,
                Username = "admin",
                Rdp = new RdpSettings
                {
                    Width = 1280,
                    Height = 720,
                    Domain = "EXAMPLE",
                    RedirectDrives = true,
                    RedirectClipboard = true
                }
            };

            var file = RdpLauncher.CreateRdpFile(rdpProfile, "P@ssw0rd!");
            var content = await File.ReadAllTextAsync(file);
            Console.WriteLine($"   generated {file} ({content.Length} chars)");
            if (!content.Contains("full address:s:example.test:3389", StringComparison.Ordinal) ||
                !content.Contains("password 51:b:", StringComparison.Ordinal) ||
                !content.Contains("desktopwidth:i:1280", StringComparison.Ordinal))
            {
                Console.WriteLine("FAIL: RDP file content unexpected.");
                failures++;
            }
            else
            {
                Console.WriteLine("PASS: RDP file generation.");
            }

            File.Delete(file);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: RDP: " + ex);
            failures++;
        }

        Console.WriteLine("== MONITOR ==");
        try
        {
            await using var monitor = new MonitorService();
            await monitor.ConnectAsync(profile, credentials);
            var stopwatch = Stopwatch.StartNew();
            var metrics = await monitor.PollAsync();
            stopwatch.Stop();
            Console.WriteLine($"   host={metrics.HostName} os={metrics.OsName} kernel={metrics.Kernel}");
            Console.WriteLine($"   cpu={metrics.CpuPercent:0.0}% mem={metrics.MemPercent:0.0}% ({metrics.MemText}) swap={metrics.SwapText}");
            Console.WriteLine($"   load={metrics.LoadText} uptime={metrics.UptimeText} netIn={metrics.NetInText} netOut={metrics.NetOutText}");
            Console.WriteLine($"   disks={metrics.Disks.Count} procs={metrics.Processes.Count} rtt={metrics.RttMs}ms");
            foreach (var disk in metrics.Disks.Take(3))
            {
                Console.WriteLine($"   disk {disk.MountPoint} {disk.PercentText} {disk.DetailText}");
            }
            foreach (var process in metrics.Processes.Take(3))
            {
                Console.WriteLine($"   proc {process.Pid} {process.Name} cpu={process.CpuText} mem={process.MemText}");
            }

            if (metrics.HostName.Length == 0 || metrics.MemTotalBytes == 0 || metrics.Processes.Count == 0)
            {
                Console.WriteLine("FAIL: monitor returned incomplete data.");
                Console.WriteLine("--- raw monitor output ---");
                Console.WriteLine(monitor.LastRawOutput);
                Console.WriteLine("--- end raw ---");
                failures++;
            }
            else
            {
                Console.WriteLine($"PASS: monitor snapshot in {stopwatch.ElapsedMilliseconds}ms.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: monitor: " + ex);
            failures++;
        }

        session.Dispose();
        Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : $"{failures} TEST(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Probe used by tests and release scripts: --update &lt;feed&gt; [--download]</summary>
    private static async Task<int> ProbeUpdateAsync(string feed, bool download)
    {
        Console.WriteLine($"== update {feed} ==");
        Console.WriteLine($"   current={UpdateService.CurrentVersion}");
        try
        {
            var update = await UpdateService.CheckAsync(feed, CancellationToken.None);
            Console.WriteLine($"   latest={update.Manifest.Version} newer={update.IsNewer}");
            Console.WriteLine($"   installer={update.InstallerUrl}");

            if (download)
            {
                var path = await UpdateService.DownloadAsync(update, null, CancellationToken.None);
                var length = new FileInfo(path).Length;
                Console.WriteLine($"   downloaded={path} size={length}");
                if (length == 0)
                {
                    Console.WriteLine("FAIL: downloaded installer is empty.");
                    return 1;
                }
            }

            Console.WriteLine("PASS: update manifest read, compared and verified.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: update: " + ex);
            return 1;
        }
    }
}
